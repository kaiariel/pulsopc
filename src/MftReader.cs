using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PulsoPC
{
    // Lo que se saca de cada registro de la MFT, en arreglos (un lugar por número de registro)
    class MftTable
    {
        public const byte InUse = 1, Dir = 2, HasName = 4, AttrList = 8;
        public readonly int Count;
        public readonly int[] Parent, Ext;
        public readonly byte[] Flags;
        public readonly long[] Size, Alloc, Mod;
        public readonly uint[] Attr;
        public readonly Dictionary<int, string> Names = new Dictionary<int, string>();   // carpetas y archivos grandes
        public readonly List<string> ExtNames = new List<string>();
        public readonly Dictionary<string, int> ExtIdx = new Dictionary<string, int>(StringComparer.Ordinal);
        public const long BigName = 1048576; // se guarda el nombre de los archivos de 1 MB o más
        public struct Link { public int Rec, Parent, Ext; }
        public readonly List<Link> Links = new List<Link>(); // nombres extra de un mismo archivo (enlaces duros)

        public MftTable(int n)
        {
            Count = n; Parent = new int[n]; Ext = new int[n]; Flags = new byte[n];
            Size = new long[n]; Alloc = new long[n]; Mod = new long[n]; Attr = new uint[n];
        }
    }

    // Lectura directa de la tabla maestra de archivos (MFT) de una unidad NTFS. Requiere administrador.
    static class MftReader
    {
        public static bool Available(string drive)
        {
            if (!Program.IsAdmin) return false;
            try { return new System.IO.DriveInfo(drive).DriveFormat == "NTFS"; } catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NTFS_VOLUME_DATA_BUFFER
        {
            public long VolumeSerialNumber, NumberSectors, TotalClusters, FreeClusters, TotalReserved;
            public uint BytesPerSector, BytesPerCluster, BytesPerFileRecordSegment, ClustersPerFileRecordSegment;
            public long MftValidDataLength, MftStartLcn, Mft2StartLcn, MftZoneStart, MftZoneEnd;
        }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, out NTFS_VOLUME_DATA_BUFFER outBuf, int outSize, out int ret, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFilePointerEx(SafeFileHandle h, long dist, out long newPos, uint method);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, byte[] buf, int toRead, out int read, IntPtr ov);

        // Arma el índice completo leyendo la MFT; deja ix con la misma forma que el recorrido de carpetas
        public static void Scan(DiskIndex ix, string drive, CancellationToken ct)
        {
            string vol = @"\\.\" + drive.TrimEnd('\\');
            using (var h = CreateFileW(vol, 0x80000000, 0x1 | 0x2, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) throw new InvalidOperationException("No se pudo abrir la unidad (" + Marshal.GetLastWin32Error() + ")");
                NTFS_VOLUME_DATA_BUFFER vd; int ret;
                if (!DeviceIoControl(h, 0x00090064, IntPtr.Zero, 0, out vd, Marshal.SizeOf(typeof(NTFS_VOLUME_DATA_BUFFER)), out ret, IntPtr.Zero))
                    throw new InvalidOperationException("La unidad no respondió como NTFS (" + Marshal.GetLastWin32Error() + ")");
                int rec = (int)vd.BytesPerFileRecordSegment; long cluster = vd.BytesPerCluster;
                if (rec < 512 || rec > 65536 || cluster < 512) throw new InvalidOperationException("Geometría NTFS inesperada");

                // Registro 0 = la propia MFT: de ahí salen los tramos (runs) donde está guardada
                var first = new byte[Math.Max(rec, (int)cluster)];
                Read(h, vd.MftStartLcn * cluster, first, first.Length);
                if (!Fixup(first, 0, rec)) throw new InvalidOperationException("El registro de la MFT está dañado");
                var runs = MftRuns(first, rec, cluster, (pos, n) => { var bb = new byte[n]; Read(h, pos, bb, n); return bb; });
                if (runs == null || runs.Count == 0) throw new InvalidOperationException("No se encontraron los tramos de la MFT");

                long total = vd.MftValidDataLength / rec;
                if (total <= 0 || total > int.MaxValue) throw new InvalidOperationException("Tamaño de la MFT inesperado");
                var t = new MftTable((int)total);

                const int chunkBytes = 16 * 1048576;
                var buf = new byte[chunkBytes];
                long index = 0;
                foreach (var run in runs)
                {
                    if (run[0] < 0) { index += run[1] * cluster / rec; continue; }
                    long pos = run[0] * cluster, left = run[1] * cluster;
                    while (left > 0 && index < total)
                    {
                        if (ct.IsCancellationRequested) { ix.Cancelled = true; break; }
                        int n = (int)Math.Min(chunkBytes, left);
                        n -= n % (int)Math.Max(rec, vd.BytesPerSector);
                        if (n <= 0) break;
                        Read(h, pos, buf, n);
                        for (int off = 0; off + rec <= n && index < total; off += rec, index++) ParseRecord(buf, off, rec, (int)index, t);
                        pos += n; left -= n;
                        ix.ScannedFiles = (int)index;
                        ix.Current = "Leyendo la tabla maestra (MFT): " + (index * 100 / total) + " %";
                    }
                    if (ix.Cancelled || index >= total) break;
                }
                ix.Current = "Armando el árbol...";
                Build(t, ix);
            }
        }

        static void Read(SafeFileHandle h, long pos, byte[] buf, int n)
        {
            long np;
            if (!SetFilePointerEx(h, pos, out np, 0)) throw new InvalidOperationException("No se pudo posicionar en la unidad (" + Marshal.GetLastWin32Error() + ")");
            int r;
            if (!ReadFile(h, buf, n, out r, IntPtr.Zero)) throw new InvalidOperationException("Falló la lectura de la unidad (" + Marshal.GetLastWin32Error() + ")");
            if (r != n) throw new InvalidOperationException("Lectura incompleta de la unidad (" + r + " de " + n + " bytes)");
        }

        // Tramos de datos de la propia MFT ($MFT, atributo $DATA sin nombre), en orden.
        // Cuando la MFT crece mucho, sus tramos no entran en el registro 0: Windows pone una lista de atributos ($ATTRIBUTE_LIST)
        // que dice en qué otros registros siguen. "read" lee del disco (posición y cantidad alineadas al clúster).
        public static List<long[]> MftRuns(byte[] b, int rec, long cluster, Func<long, int, byte[]> read)
        {
            var segs = new SortedDictionary<long, List<long[]>>(); // VCN inicial del segmento -> tramos
            byte[] attrList = null;
            int a = U16(b, 0x14);
            while (a + 16 <= rec)
            {
                uint type = U32(b, a); if (type == 0xFFFFFFFF) break;
                int len = (int)U32(b, a + 4); if (len < 16 || a + len > rec) break;
                if (type == 0x20)
                {
                    if (b[a + 8] == 0) { int vl = (int)U32(b, a + 16), vo = U16(b, a + 20); attrList = new byte[vl]; Buffer.BlockCopy(b, a + vo, attrList, 0, vl); }
                    else attrList = ReadRuns(Runs(b, a + U16(b, a + 32), a + len), I64(b, a + 48), cluster, read);
                }
                else if (type == 0x80 && b[a + 8] == 1 && b[a + 9] == 0) segs[I64(b, a + 16)] = Runs(b, a + U16(b, a + 32), a + len);
                a += len;
            }
            if (attrList != null)
            {
                int p = 0;
                while (p + 26 <= attrList.Length)
                {
                    uint type = U32(attrList, p); int el = U16(attrList, p + 4);
                    if (type == 0 || type == 0xFFFFFFFF || el < 26 || p + el > attrList.Length) break;
                    int nl = attrList[p + 6]; long svcn = I64(attrList, p + 8); long seg = I64(attrList, p + 16) & 0xFFFFFFFFFFFFL;
                    if (type == 0x80 && nl == 0 && seg != 0 && !segs.ContainsKey(svcn))
                    {
                        var r = ReadRecord(seg, rec, cluster, segs, read);
                        if (r == null || !Fixup(r, 0, rec)) throw new NotSupportedException("No se pudo leer el registro " + seg + " de la MFT");
                        int q = U16(r, 0x14);
                        while (q + 16 <= rec)
                        {
                            uint t2 = U32(r, q); if (t2 == 0xFFFFFFFF) break;
                            int l2 = (int)U32(r, q + 4); if (l2 < 16 || q + l2 > rec) break;
                            if (t2 == 0x80 && r[q + 8] == 1 && r[q + 9] == 0 && I64(r, q + 16) == svcn) { segs[svcn] = Runs(r, q + U16(r, q + 32), q + l2); break; }
                            q += l2;
                        }
                        if (!segs.ContainsKey(svcn)) throw new NotSupportedException("Falta el tramo " + svcn + " de la MFT");
                    }
                    p += el;
                }
            }
            if (segs.Count == 0) return null;
            var all = new List<long[]>(); foreach (var kv in segs) all.AddRange(kv.Value);
            return all;
        }

        // Lee un registro de la MFT ubicándolo con los tramos que ya se conocen
        static byte[] ReadRecord(long recNo, int rec, long cluster, SortedDictionary<long, List<long[]>> segs, Func<long, int, byte[]> read)
        {
            long off = recNo * rec, vcn = off / cluster, inOff = off % cluster;
            foreach (var kv in segs)
            {
                long v = kv.Key;
                foreach (var run in kv.Value)
                {
                    if (vcn >= v && vcn < v + run[1])
                    {
                        if (run[0] < 0) return null;
                        int n = (int)(((inOff + rec + cluster - 1) / cluster) * cluster);
                        var buf = read((run[0] + (vcn - v)) * cluster, n);
                        var r = new byte[rec]; Buffer.BlockCopy(buf, (int)inOff, r, 0, rec);
                        return r;
                    }
                    v += run[1];
                }
            }
            return null;
        }

        // Junta el contenido de un atributo no residente (por ejemplo, una lista de atributos grande)
        static byte[] ReadRuns(List<long[]> runs, long size, long cluster, Func<long, int, byte[]> read)
        {
            var res = new byte[size]; long got = 0;
            foreach (var run in runs)
            {
                if (got >= size) break;
                int n = (int)(run[1] * cluster);
                var part = run[0] < 0 ? new byte[n] : read(run[0] * cluster, n);
                int take = (int)Math.Min(n, size - got);
                Buffer.BlockCopy(part, 0, res, (int)got, take); got += take;
            }
            return res;
        }

        // Decodifica una lista de tramos NTFS: devuelve {LCN absoluto o -1 si es disperso, cantidad de clústeres}
        public static List<long[]> Runs(byte[] b, int p, int end)
        {
            var l = new List<long[]>(); long lcn = 0;
            while (p < end && b[p] != 0)
            {
                int hdr = b[p++], lb = hdr & 0xF, ob = hdr >> 4;
                if (lb == 0 || lb > 8 || ob > 8 || p + lb + ob > end) break;
                long len = 0; for (int i = 0; i < lb; i++) len |= (long)b[p + i] << (8 * i);
                p += lb;
                if (ob == 0) { l.Add(new[] { -1L, len }); continue; }
                long off = 0; for (int i = 0; i < ob; i++) off |= (long)b[p + i] << (8 * i);
                if ((b[p + ob - 1] & 0x80) != 0 && ob < 8) off |= -1L << (8 * ob);
                p += ob; lcn += off;
                l.Add(new[] { lcn, len });
            }
            return l;
        }

        // Protección de varios sectores: los 2 últimos bytes de cada 512 se guardan aparte y hay que reponerlos
        public static bool Fixup(byte[] b, int off, int rec)
        {
            int usaOff = U16(b, off + 4), usaCount = U16(b, off + 6);
            if (usaCount < 2 || usaOff + usaCount * 2 > rec) return false;
            int usn = U16(b, off + usaOff);
            for (int i = 1; i < usaCount; i++)
            {
                int pos = off + i * 512 - 2;
                if (pos + 2 > off + rec) break;
                if (U16(b, pos) != usn) return false;
                b[pos] = b[off + usaOff + i * 2]; b[pos + 1] = b[off + usaOff + i * 2 + 1];
            }
            return true;
        }

        // Lee un registro FILE y vuelca lo útil en la tabla (los registros de extensión suman a su registro base)
        public static void ParseRecord(byte[] b, int off, int rec, int index, MftTable t)
        {
            if (b[off] != 0x46 || b[off + 1] != 0x49 || b[off + 2] != 0x4C || b[off + 3] != 0x45) return; // "FILE"
            if (!Fixup(b, off, rec)) return;
            int flags = U16(b, off + 0x16);
            if ((flags & 1) == 0) return;
            long baseRef = I64(b, off + 0x20) & 0xFFFFFFFFFFFFL;
            int target = baseRef != 0 ? (int)baseRef : index;
            if (target < 0 || target >= t.Count) return;
            if (baseRef == 0) { t.Flags[target] |= MftTable.InUse; if ((flags & 2) != 0) t.Flags[target] |= MftTable.Dir; }

            int a = off + U16(b, off + 0x14), end = off + rec;
            int bestRank = 9, nameOff = 0, nameLen = 0; long bestParent = -1;
            int nNames = 0;
            while (a + 16 <= end)
            {
                uint type = U32(b, a); if (type == 0xFFFFFFFF) break;
                int len = (int)U32(b, a + 4); if (len < 16 || a + len > end) break;
                byte nonRes = b[a + 8], attrNameLen = b[a + 9];
                if (type == 0x10 && nonRes == 0)
                {
                    int v = a + U16(b, a + 20);
                    if (v + 36 <= end) { t.Mod[target] = I64(b, v + 8); t.Attr[target] = U32(b, v + 32); }
                }
                else if (type == 0x20) t.Flags[target] |= MftTable.AttrList;
                else if (type == 0x30 && nonRes == 0)
                {
                    int v = a + U16(b, a + 20);
                    if (v + 66 <= end)
                    {
                        int ns = b[v + 65], nl = b[v + 64];
                        // Preferir el nombre largo (Win32); el corto de DOS solo si no hay otro
                        int rank = ns == 1 || ns == 3 ? 0 : ns == 0 ? 1 : 2;
                        if (v + 66 + nl * 2 <= end)
                        {
                            if (nNames < cParent.Length && rank < 2) { cParent[nNames] = I64(b, v) & 0xFFFFFFFFFFFFL; cOff[nNames] = v + 66; cLen[nNames] = nl; nNames++; }
                            if (rank < bestRank) { bestRank = rank; bestParent = I64(b, v) & 0xFFFFFFFFFFFFL; nameOff = v + 66; nameLen = nl; }
                        }
                    }
                }
                else if (type == 0x80)
                {
                    if (nonRes == 0) { if (attrNameLen == 0) t.Size[target] = U32(b, a + 16); }
                    else if (I64(b, a + 16) == 0) // solo el primer segmento tiene los tamaños
                    {
                        int af = U16(b, a + 12);
                        long alloc = (af & 0x8001) != 0 && len >= 72 ? I64(b, a + 64) : I64(b, a + 40);
                        if (attrNameLen == 0) t.Size[target] = I64(b, a + 48);
                        t.Alloc[target] += alloc;
                    }
                }
                a += len;
            }
            if (bestParent < 0) return;
            bool isDir = (t.Flags[target] & MftTable.Dir) != 0;
            bool primaryHere = (t.Flags[target] & MftTable.HasName) == 0;
            if (primaryHere)
            {
                t.Flags[target] |= MftTable.HasName;
                t.Parent[target] = bestParent > int.MaxValue ? -1 : (int)bestParent;
                if (isDir) t.Names[target] = Encoding.Unicode.GetString(b, nameOff, nameLen * 2);
                else
                {
                    t.Ext[target] = ExtIndex(b, nameOff, nameLen, t);
                    if (t.Size[target] >= MftTable.BigName || (t.Flags[target] & MftTable.AttrList) != 0) t.Names[target] = Encoding.Unicode.GetString(b, nameOff, nameLen * 2);
                }
            }
            // Enlaces duros: el mismo archivo con otro nombre u otra carpeta. Se muestran, pero su espacio cuenta una sola vez.
            if (isDir) return;
            for (int i = 0; i < nNames; i++)
            {
                if (primaryHere && cOff[i] == nameOff) continue;
                t.Links.Add(new MftTable.Link { Rec = target, Parent = cParent[i] > int.MaxValue ? -1 : (int)cParent[i], Ext = ExtIndex(b, cOff[i], cLen[i], t) });
            }
        }

        static readonly long[] cParent = new long[32]; static readonly int[] cOff = new int[32], cLen = new int[32];

        // Extensión del nombre en UTF-16 sin armar el nombre completo
        static int ExtIndex(byte[] b, int off, int chars, MftTable t)
        {
            int dot = -1;
            for (int i = chars - 1; i >= 1 && i >= chars - 13; i--) if (b[off + i * 2] == 0x2E && b[off + i * 2 + 1] == 0) { dot = i; break; }
            string ext = dot < 0 || dot == chars - 1 ? "" : Encoding.Unicode.GetString(b, off + (dot + 1) * 2, (chars - dot - 1) * 2).ToLowerInvariant();
            int idx;
            if (!t.ExtIdx.TryGetValue(ext, out idx)) { idx = t.ExtNames.Count; t.ExtNames.Add(ext); t.ExtIdx[ext] = idx; }
            return idx;
        }

        // De la tabla al árbol de carpetas con totales, tipos y archivos más grandes
        public static void Build(MftTable t, DiskIndex ix)
        {
            var nodes = new DNode[t.Count];
            const int RootRec = 5;
            if (RootRec < t.Count) nodes[RootRec] = ix.Root;
            ix.Root.Kids = null; ix.Ext.Clear(); ix.TopByExt.Clear();
            int files = 0, dirs = 0; long bytes = 0;
            for (int i = 0; i < t.Count; i++)
            {
                byte f = t.Flags[i];
                if ((f & (MftTable.InUse | MftTable.Dir | MftTable.HasName)) != (MftTable.InUse | MftTable.Dir | MftTable.HasName) || i == RootRec) continue;
                string nm; t.Names.TryGetValue(i, out nm);
                nodes[i] = new DNode { Name = nm ?? ("carpeta " + i), Attr = (System.IO.FileAttributes)t.Attr[i] | System.IO.FileAttributes.Directory, Modified = t.Mod[i], Reparse = (t.Attr[i] & 0x400) != 0 };
            }
            for (int i = 0; i < t.Count; i++)
            {
                var n = nodes[i]; if (n == null || i == RootRec) continue;
                int pi = t.Parent[i];
                var p = pi >= 0 && pi < t.Count ? nodes[pi] : null;
                if (p == null || p == n) p = ix.Root; // huérfanas: se cuelgan de la raíz
                n.Parent = p; if (p.Kids == null) p.Kids = new List<DNode>(); p.Kids.Add(n); dirs++;
            }
            var top = new DiskIndex.TopN(3000); var topExt = new Dictionary<int, DiskIndex.TopN>();
            var ext = new ExtStat[t.ExtNames.Count];
            for (int i = 0; i < t.Count; i++)
            {
                byte f = t.Flags[i];
                if ((f & (MftTable.InUse | MftTable.HasName)) != (MftTable.InUse | MftTable.HasName) || (f & MftTable.Dir) != 0) continue;
                int pi = t.Parent[i];
                var p = pi >= 0 && pi < t.Count && nodes[pi] != null ? nodes[pi] : ix.Root;
                // Registro 8 = $BadClus: su flujo $Bad declara el tamaño de toda la unidad, pero no ocupa nada
                long size = t.Size[i], alloc = i == 8 ? 0 : t.Alloc[i];
                p.DirectFiles++; p.FilesSize += size; p.FilesAlloc += alloc;
                if (t.Mod[i] > p.Modified) p.Modified = t.Mod[i];
                files++; bytes += size;
                int e = t.Ext[i];
                if (e >= 0 && e < ext.Length) { if (ext[e] == null) ext[e] = new ExtStat { Ext = t.ExtNames[e] }; ext[e].Size += size; ext[e].Alloc += alloc; ext[e].Count++; }
                if (size >= MftTable.BigName)
                {
                    string nm; if (!t.Names.TryGetValue(i, out nm)) nm = "(archivo " + i + ")";
                    var bf = new BigFile { Dir = p, Name = nm, Size = size, Modified = t.Mod[i] };
                    if (top.Wants(size)) top.Add(bf);
                    if (e >= 0)
                    {
                        DiskIndex.TopN te; if (!topExt.TryGetValue(e, out te)) topExt[e] = te = new DiskIndex.TopN(40);
                        if (te.Wants(size)) te.Add(bf);
                    }
                }
            }
            // Enlaces duros: aparecen en su carpeta con su tamaño, pero sin sumar espacio en disco (ya lo sumó el nombre principal)
            foreach (var ln in t.Links)
            {
                if (ln.Rec < 0 || ln.Rec >= t.Count || (t.Flags[ln.Rec] & MftTable.InUse) == 0) continue;
                var p = ln.Parent >= 0 && ln.Parent < t.Count && nodes[ln.Parent] != null ? nodes[ln.Parent] : ix.Root;
                long size = t.Size[ln.Rec];
                p.DirectFiles++; p.FilesSize += size; files++; bytes += size; ix.HardLinks++;
                if (ln.Ext >= 0 && ln.Ext < ext.Length) { if (ext[ln.Ext] == null) ext[ln.Ext] = new ExtStat { Ext = t.ExtNames[ln.Ext] }; ext[ln.Ext].Size += size; ext[ln.Ext].Count++; }
            }
            foreach (var x in ext) if (x != null) ix.Ext[x.Ext] = x;
            foreach (var kv in topExt) ix.TopByExt[t.ExtNames[kv.Key]] = new List<BigFile>(kv.Value.Items).OrderByDescendingSize();
            ix.TopFiles = new List<BigFile>(top.Items).OrderByDescendingSize();
            ix.ScannedFiles = files; ix.ScannedDirs = dirs + 1; ix.ScannedBytes = bytes; ix.DeniedDirs = 0;
            DiskIndex.Aggregate(ix.Root);
        }

        static int U16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }
        static uint U32(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
        static long I64(byte[] b, int o) { return (long)U32(b, o) | ((long)U32(b, o + 4) << 32); }

        static List<BigFile> OrderByDescendingSize(this List<BigFile> l) { l.Sort((x, y) => y.Size.CompareTo(x.Size)); return l; }
    }
}
