using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace PulsoPC
{
    static partial class CleanerService
    {
        public static long Clean(CleanCat c, CancellationToken ct, Action<string> status)
        {
            long freed = 0;
            if (c.Kind == CatKind.RecycleBin)
            {
                long before = Math.Max(0, c.Size);
                Native.SHEmptyRecycleBin(IntPtr.Zero, null, 7);
                Log.Write("Limpieza: papelera vaciada (" + Fmt.Bytes(before) + ")");
                return before;
            }
            var stopped = new List<ServiceController>();
            try
            {
                foreach (var sv in c.Services)
                {
                    try
                    {
                        var sc = new ServiceController(sv);
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            if (status != null) status("Deteniendo servicio " + sv + "...");
                            sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); stopped.Add(sc);
                        }
                    }
                    catch { }
                }
                if (c.Kind == CatKind.Thumbnails) ExplorerShell.Restart(() => { freed += WipeAll(c, ct); });
                else freed = WipeAll(c, ct);
            }
            finally { foreach (var sc in stopped) { try { sc.Start(); } catch { } } }
            Log.Write("Limpieza: " + c.Name + " liberó " + Fmt.Bytes(freed));
            return freed;
        }

        static long WipeAll(CleanCat c, CancellationToken ct)
        {
            long freed = 0;
            foreach (var t in c.GetTargets()) freed += Wipe(t, ct);
            return freed;
        }

        internal static long Wipe(Target t, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(t.Dir) || t.Dir.Length < 8 || !Directory.Exists(t.Dir)) return 0;
            long freed = 0;
            var dirs = new List<KeyValuePair<string, DateTime>>();
            FsWalk.Files(t.Dir, t.Recursive, fi =>
            {
                if (Safe.IsCloudOnly(fi.Attributes)) return;
                if (t.Filter != null && !t.Filter(fi)) return;
                try
                {
                    long len = fi.Length;
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0) fi.Attributes = FileAttributes.Normal;
                    fi.Delete(); freed += len;
                }
                catch { }
            }, di => dirs.Add(new KeyValuePair<string, DateTime>(di.FullName, di.LastWriteTimeUtc)), ct);
            if (t.Recursive)
            {
                dirs.Reverse();
                foreach (var d in dirs)
                {
                    try
                    {
                        if (t.MinAgeHours > 0 && (DateTime.UtcNow - d.Value).TotalHours < t.MinAgeHours) continue;
                        var di = new DirectoryInfo(d.Key);
                        if (!di.EnumerateFileSystemInfos().Any()) di.Delete();
                    }
                    catch { }
                }
            }
            return freed;
        }

        public static void Reanalyze(CleanCat c) { try { AnalyzeOne(c, CancellationToken.None); } catch { } }

        // Datos informativos (no se borran desde aqui)
        public static long FileSize(string path) { try { var f = new FileInfo(path); return f.Exists ? f.Length : 0; } catch { return 0; } }
        public static long HiberfilSize() { return FileSize(Path.Combine(Env.SysDrive, "hiberfil.sys")); }
        public static long PagefileSize() { return FileSize(Path.Combine(Env.SysDrive, "pagefile.sys")) + FileSize(Path.Combine(Env.SysDrive, "swapfile.sys")); }
    }
}
