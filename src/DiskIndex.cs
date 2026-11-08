using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PulsoPC
{
    // Una carpeta del índice con sus totales (propios y de todo lo que contiene)
    class DNode
    {
        public string Name; public DNode Parent; public List<DNode> Kids;
        public long Size, Alloc, FilesSize, FilesAlloc; // totales y solo de los archivos directos
        public int Files, Dirs, DirectFiles;
        public long Modified; // FILETIME más reciente de lo que contiene
        public FileAttributes Attr; public bool Denied, Reparse;

        public string FullPath
        {
            get
            {
                var parts = new List<string>(); var n = this;
                while (n != null) { parts.Add(n.Name); n = n.Parent; }
                parts.Reverse();
                string root = parts[0];
                return parts.Count == 1 ? root : Path.Combine(root, string.Join("\\", parts.Skip(1).ToArray()));
            }
        }
        public int Items { get { return Files + Dirs; } }
    }

    class ExtStat { public string Ext = ""; public long Size, Alloc; public int Count; }

    class BigFile { public DNode Dir; public string Name; public long Size, Modified; public string Path { get { return System.IO.Path.Combine(Dir.FullPath, Name); } } }

    // Índice de una unidad completa armado en un solo recorrido en paralelo (como WizTree, pero sin leer la MFT todavía)
    class DiskIndex
    {
        public DNode Root; public string Drive = "";
        public long Total, Free, Cluster = 4096;
        public int ScannedFiles, ScannedDirs, DeniedDirs, HardLinks; public long ScannedBytes;
        public TimeSpan Elapsed; public bool Cancelled;
        public Dictionary<string, ExtStat> Ext = new Dictionary<string, ExtStat>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<BigFile>> TopByExt = new Dictionary<string, List<BigFile>>(StringComparer.OrdinalIgnoreCase);
        public List<BigFile> TopFiles = new List<BigFile>();
        public volatile string Current = "";
        public string Method = "recorrido de carpetas";
        public static DiskIndex Last; // el último mapa completo de esta sesión (para el informe)
        public static bool ForceWalk; // para comparar: obliga al recorrido de carpetas aunque se pueda leer la MFT
        const int TopKeep = 3000, TopExtKeep = 40;

        // Pack = 4: las fechas (FILETIME) son dos enteros de 32 bits, sin relleno antes
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes; public long ftCreationTime, ftLastAccessTime, ftLastWriteTime;
            public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindFirstFileExW(string name, int infoLevel, out WIN32_FIND_DATAW data, int searchOp, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool FindNextFileW(IntPtr h, out WIN32_FIND_DATAW data);
        [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetCompressedFileSizeW(string name, out uint high);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool GetDiskFreeSpaceW(string root, out uint spc, out uint bps, out uint free, out uint total);
        static readonly IntPtr Invalid = new IntPtr(-1);

        const uint DIR = 0x10, REPARSE = 0x400, SPARSE = 0x200, COMPRESSED = 0x800, OFFLINE = 0x1000, RECALL_OPEN = 0x40000, RECALL_ACCESS = 0x400000;

        // Unidades locales que conviene analizar (NTFS o ReFS; no las virtuales de Google Drive)
        public static List<DriveInfo> Drives()
        {
            var l = new List<DriveInfo>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady && (d.DriveFormat == "NTFS" || d.DriveFormat == "ReFS")) l.Add(d); } catch { }
            }
            return l;
        }

        // "into" permite que la interfaz lea el progreso (contadores y carpeta actual) mientras se escanea
        public static DiskIndex Scan(string drive, CancellationToken ct, DiskIndex into = null)
        {
            var ix = into ?? new DiskIndex(); ix.Drive = drive;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { var di = new DriveInfo(drive); ix.Total = di.TotalSize; ix.Free = di.TotalFreeSpace; } catch { }
            uint spc, bps, f, t; if (GetDiskFreeSpaceW(drive, out spc, out bps, out f, out t)) ix.Cluster = (long)spc * bps;
            ix.Root = new DNode { Name = drive.TrimEnd('\\') + "\\", Attr = FileAttributes.Directory };

            // Camino rápido: leer la MFT directamente (NTFS + administrador). Si algo falla, se recorre carpeta por carpeta.
            if (!ForceWalk && drive.Length <= 3 && MftReader.Available(drive))
            {
                try
                {
                    MftReader.Scan(ix, drive, ct);
                    ix.Method = "lectura de la MFT"; ix.Elapsed = sw.Elapsed;
                    return ix;
                }
                catch (Exception ex)
                {
                    Log.Write("Mapa del disco: no se pudo leer la MFT de " + drive + " (" + ex.Message + "); uso el recorrido de carpetas.");
                    ix.Root = new DNode { Name = drive.TrimEnd('\\') + "\\", Attr = FileAttributes.Directory };
                    ix.Ext.Clear(); ix.TopByExt.Clear(); ix.TopFiles.Clear(); ix.ScannedFiles = 0; ix.ScannedDirs = 0; ix.ScannedBytes = 0; ix.Cancelled = false;
                    ix.Method = "recorrido de carpetas (la MFT falló: " + ex.Message + ")";
                }
            }

            var queue = new ConcurrentQueue<DNode>(); queue.Enqueue(ix.Root);
            int pending = 1;
            int workers = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
            var locals = new List<Local>(); var threads = new List<Thread>();
            for (int i = 0; i < workers; i++)
            {
                var loc = new Local(); locals.Add(loc);
                var th = new Thread(() =>
                {
                    var spin = new SpinWait();
                    while (Volatile.Read(ref pending) > 0)
                    {
                        if (ct.IsCancellationRequested) return;
                        DNode n;
                        if (!queue.TryDequeue(out n)) { spin.SpinOnce(); continue; }
                        spin.Reset();
                        try { ix.ReadDir(n, queue, ref pending, loc); }
                        catch { n.Denied = true; }
                        Interlocked.Decrement(ref pending);
                    }
                }) { IsBackground = true, Name = "Escaneo " + i };
                threads.Add(th); th.Start();
            }
            foreach (var th in threads) th.Join();
            ix.Cancelled = ct.IsCancellationRequested;

            // Juntar lo de cada hilo
            var tops = new List<BigFile>();
            foreach (var loc in locals)
            {
                foreach (var kv in loc.Ext)
                {
                    ExtStat e; if (!ix.Ext.TryGetValue(kv.Key, out e)) ix.Ext[kv.Key] = e = new ExtStat { Ext = kv.Key };
                    e.Size += kv.Value.Size; e.Alloc += kv.Value.Alloc; e.Count += kv.Value.Count;
                }
                foreach (var kv in loc.TopExt)
                {
                    List<BigFile> l; if (!ix.TopByExt.TryGetValue(kv.Key, out l)) ix.TopByExt[kv.Key] = l = new List<BigFile>();
                    l.AddRange(kv.Value.Items);
                }
                tops.AddRange(loc.Top.Items);
            }
            ix.TopFiles = tops.OrderByDescending(x => x.Size).Take(TopKeep).ToList();
            foreach (var k in ix.TopByExt.Keys.ToList()) ix.TopByExt[k] = ix.TopByExt[k].OrderByDescending(x => x.Size).Take(TopExtKeep).ToList();
            Aggregate(ix.Root);
            ix.Elapsed = sw.Elapsed;
            return ix;
        }

        // Datos propios de cada hilo (sin bloqueos durante el recorrido)
        class Local
        {
            public readonly Dictionary<string, ExtStat> Ext = new Dictionary<string, ExtStat>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, TopN> TopExt = new Dictionary<string, TopN>(StringComparer.OrdinalIgnoreCase);
            public readonly TopN Top = new TopN(TopKeep);
        }

        void ReadDir(DNode n, ConcurrentQueue<DNode> queue, ref int pending, Local loc)
        {
            string path = n.FullPath;
            Current = path;
            string pattern = (path.StartsWith(@"\\") ? path : @"\\?\" + path).TrimEnd('\\') + @"\*";
            WIN32_FIND_DATAW fd;
            IntPtr h = FindFirstFileExW(pattern, 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Invalid) { n.Denied = true; Interlocked.Increment(ref DeniedDirs); return; }
            Interlocked.Increment(ref ScannedDirs);
            int files = 0; long fsize = 0, falloc = 0, newest = 0;
            try
            {
                do
                {
                    string name = fd.cFileName;
                    if (name == "." || name == "..") continue;
                    if ((fd.dwFileAttributes & DIR) != 0)
                    {
                        var c = new DNode { Name = name, Parent = n, Attr = (FileAttributes)fd.dwFileAttributes, Modified = fd.ftLastWriteTime };
                        if (n.Kids == null) n.Kids = new List<DNode>();
                        n.Kids.Add(c);
                        // Las uniones y enlaces (junctions) apuntan a otra carpeta: se muestran pero no se recorren
                        if ((fd.dwFileAttributes & REPARSE) != 0) { c.Reparse = true; continue; }
                        Interlocked.Increment(ref pending); queue.Enqueue(c);
                    }
                    else
                    {
                        long size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                        long alloc = AllocOf(fd.dwFileAttributes, size, path, name);
                        files++; fsize += size; falloc += alloc;
                        if (fd.ftLastWriteTime > newest) newest = fd.ftLastWriteTime;
                        string ext = ExtOf(name);
                        ExtStat e; if (!loc.Ext.TryGetValue(ext, out e)) loc.Ext[ext] = e = new ExtStat { Ext = ext };
                        e.Size += size; e.Alloc += alloc; e.Count++;
                        if (size >= 1048576)
                        {
                            if (loc.Top.Wants(size)) loc.Top.Add(new BigFile { Dir = n, Name = name, Size = size, Modified = fd.ftLastWriteTime });
                            TopN te; if (!loc.TopExt.TryGetValue(ext, out te)) loc.TopExt[ext] = te = new TopN(TopExtKeep);
                            if (te.Wants(size)) te.Add(new BigFile { Dir = n, Name = name, Size = size, Modified = fd.ftLastWriteTime });
                        }
                    }
                } while (FindNextFileW(h, out fd));
            }
            finally { FindClose(h); }
            n.DirectFiles = files; n.FilesSize = fsize; n.FilesAlloc = falloc;
            if (newest > n.Modified) n.Modified = newest;
            Interlocked.Add(ref ScannedFiles, files); Interlocked.Add(ref ScannedBytes, fsize);
        }

        // Espacio que ocupa de verdad en el disco: 0 si está solo en la nube; comprimidos y dispersos se consultan
        long AllocOf(uint attr, long size, string dir, string name)
        {
            if ((attr & (OFFLINE | RECALL_OPEN | RECALL_ACCESS)) != 0) return 0;
            if ((attr & (SPARSE | COMPRESSED)) != 0)
            {
                uint hi; uint lo = GetCompressedFileSizeW(@"\\?\" + Path.Combine(dir, name), out hi);
                if (lo != 0xFFFFFFFF || Marshal.GetLastWin32Error() == 0) return ((((long)hi << 32) | lo) + Cluster - 1) / Cluster * Cluster;
            }
            return (size + Cluster - 1) / Cluster * Cluster;
        }

        public static string ExtOf(string name)
        {
            int dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1 || name.Length - dot > 12) return "";
            return name.Substring(dot + 1).ToLowerInvariant();
        }

        // Totales de abajo hacia arriba, sin recursión (hay árboles muy profundos)
        internal static void Aggregate(DNode root)
        {
            var order = new List<DNode>(); var st = new Stack<DNode>(); st.Push(root);
            while (st.Count > 0) { var n = st.Pop(); order.Add(n); if (n.Kids != null) foreach (var k in n.Kids) st.Push(k); }
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var n = order[i];
                n.Size = n.FilesSize; n.Alloc = n.FilesAlloc; n.Files = n.DirectFiles; n.Dirs = 0;
                if (n.Kids == null) continue;
                foreach (var k in n.Kids)
                {
                    n.Size += k.Size; n.Alloc += k.Alloc; n.Files += k.Files; n.Dirs += k.Dirs + 1;
                    if (k.Modified > n.Modified) n.Modified = k.Modified;
                }
                n.Kids.Sort((a, b) => b.Alloc != a.Alloc ? b.Alloc.CompareTo(a.Alloc) : b.Size.CompareTo(a.Size));
            }
        }

        // Descuenta del índice un archivo que se mandó a la papelera
        public void RemoveFile(DNode dir, long size)
        {
            long alloc = (size + Cluster - 1) / Cluster * Cluster;
            dir.DirectFiles--; dir.FilesSize -= size; dir.FilesAlloc -= alloc;
            for (var p = dir; p != null; p = p.Parent) { p.Size -= size; p.Alloc -= alloc; p.Files--; }
        }

        // Descuenta del índice una carpeta entera que se mandó a la papelera
        public void RemoveDir(DNode d)
        {
            if (d.Parent == null) return;
            if (d.Parent.Kids != null) d.Parent.Kids.Remove(d);
            for (var p = d.Parent; p != null; p = p.Parent) { p.Size -= d.Size; p.Alloc -= d.Alloc; p.Files -= d.Files; p.Dirs -= d.Dirs + 1; }
        }

        // Los N archivos más grandes con un montículo mínimo
        public class TopN
        {
            readonly int cap; readonly List<BigFile> h = new List<BigFile>();
            public TopN(int capacity) { cap = capacity; }
            public IEnumerable<BigFile> Items { get { return h; } }
            public bool Wants(long size) { return h.Count < cap || size > h[0].Size; }
            public void Add(BigFile f)
            {
                if (h.Count < cap) { h.Add(f); Up(h.Count - 1); }
                else if (f.Size > h[0].Size) { h[0] = f; Down(0); }
            }
            void Up(int i) { while (i > 0) { int p = (i - 1) / 2; if (h[p].Size <= h[i].Size) break; var t = h[p]; h[p] = h[i]; h[i] = t; i = p; } }
            void Down(int i)
            {
                for (;;)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < h.Count && h[l].Size < h[m].Size) m = l;
                    if (r < h.Count && h[r].Size < h[m].Size) m = r;
                    if (m == i) return;
                    var t = h[m]; h[m] = h[i]; h[i] = t; i = m;
                }
            }
        }

        // Archivos directos de una carpeta (para mostrarlos al desplegar), los más grandes primero
        public static List<BigFile> FilesOf(DNode n, int max)
        {
            var l = new List<BigFile>();
            string path = n.FullPath;
            WIN32_FIND_DATAW fd;
            IntPtr h = FindFirstFileExW(@"\\?\" + path.TrimEnd('\\') + @"\*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Invalid) return l;
            try
            {
                do
                {
                    if ((fd.dwFileAttributes & DIR) != 0) continue;
                    l.Add(new BigFile { Dir = n, Name = fd.cFileName, Size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow, Modified = fd.ftLastWriteTime });
                } while (FindNextFileW(h, out fd));
            }
            finally { FindClose(h); }
            return l.OrderByDescending(x => x.Size).Take(max).ToList();
        }
    }
}
