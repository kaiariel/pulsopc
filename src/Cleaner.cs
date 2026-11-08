using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace PulsoPC
{
    enum CatKind { Files, RecycleBin, Thumbnails }

    class Target
    {
        public string Dir; public bool Recursive = true; public Func<FileInfo, bool> Filter; public int MinAgeHours;
    }

    class CleanCat
    {
        public string Id, Name, Desc, Group, Note = "";
        public bool DefaultOn = true, NeedsAdmin, HideIfEmpty = true;
        public CatKind Kind = CatKind.Files;
        public string[] Services = new string[0];
        public Func<List<Target>> GetTargets;
        public long Size = -1, Files; public bool Analyzed, Hidden;
    }

    static class FsWalk
    {
        static readonly string LongPrefix = new string((char)92, 2) + "?" + (char)92;
        static readonly string UncPrefix = new string((char)92, 2);
        public static string NoPrefix(string p) { return p != null && p.StartsWith(LongPrefix) ? p.Substring(4) : p; }

        // Recorre archivos sin seguir enlaces simbolicos ni uniones
        public static void Files(string root, bool recursive, Action<FileInfo> onFile, Action<DirectoryInfo> onDir, CancellationToken ct, Func<DirectoryInfo, bool> skipDir = null)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            var stack = new Stack<string>(); stack.Push(root);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                DirectoryInfo di;
                string cur = stack.Pop();
                if (cur.Length >= 245 && !cur.StartsWith(LongPrefix) && !cur.StartsWith(UncPrefix)) cur = LongPrefix + cur;
                try { di = new DirectoryInfo(cur); } catch { continue; }
                try
                {
                    foreach (var fi in (onFile != null ? di.EnumerateFiles() : Enumerable.Empty<FileInfo>()))
                    {
                        try { onFile(fi); } catch (OperationCanceledException) { throw; } catch { }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                if (!recursive) continue;
                try
                {
                    foreach (var sub in di.EnumerateDirectories())
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (skipDir != null && skipDir(sub)) continue;
                        if (onDir != null) onDir(sub);
                        stack.Push(sub.FullName);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }

        public static long SizeOf(string root, CancellationToken ct)
        {
            long total = 0;
            Files(root, true, fi => { if (!Safe.IsCloudOnly(fi.Attributes)) total += fi.Length; }, null, ct);
            return total;
        }
    }

    static partial class CleanerService
    {
        public static readonly List<CleanCat> Cats = CleanCatalog.Build();
        public static event Action<CleanCat> CategoryDone;
        public static DateTime LastAnalysis = DateTime.MinValue;
        static Task running; static readonly object lk = new object();
        static CancellationTokenSource cts = new CancellationTokenSource();

        public static void CancelAnalysis() { try { cts.Cancel(); } catch { } }
        public static bool IsAnalyzing { get { lock (lk) return running != null && !running.IsCompleted; } }

        public static Task AnalyzeAllAsync()
        {
            lock (lk)
            {
                if (running != null && !running.IsCompleted) return running;
                foreach (var c in Cats) { c.Size = -1; c.Files = 0; c.Analyzed = false; c.Hidden = false; }
                cts = new CancellationTokenSource(); var ct = cts.Token;
                running = Task.Run(() =>
                {
                    try
                    {
                        Parallel.ForEach(Cats, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, c =>
                        {
                            try { AnalyzeOne(c, ct); } catch (OperationCanceledException) { throw; } catch { c.Size = 0; }
                            c.Analyzed = true;
                            var h = CategoryDone; if (h != null) h(c);
                        });
                        LastAnalysis = DateTime.Now;
                    }
                    catch (OperationCanceledException) { }
                    catch (AggregateException) { }
                });
                return running;
            }
        }

        static void AnalyzeOne(CleanCat c, CancellationToken ct)
        {
            if (c.Kind == CatKind.RecycleBin)
            {
                var info = new Native.SHQUERYRBINFO(); info.cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO));
                if (Native.SHQueryRecycleBin(null, ref info) == 0) { c.Size = info.i64Size; c.Files = info.i64NumItems; } else { c.Size = 0; }
                return;
            }
            long size = 0, files = 0;
            foreach (var t in c.GetTargets())
            {
                var tt = t;
                FsWalk.Files(tt.Dir, tt.Recursive, fi =>
                {
                    if (Safe.IsCloudOnly(fi.Attributes)) return;
                    if (tt.Filter != null && !tt.Filter(fi)) return;
                    size += fi.Length; files++;
                }, null, ct);
            }
            c.Size = size; c.Files = files;
        }
    }
}
