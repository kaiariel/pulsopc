using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class NodeModulesView
    {
        async void Search()
        {
            if (busy) return;
            busy = true; btnGo.Visible = false; btnStop.Visible = true; lv.Items.Clear(); UpdateInfo();
            cts = new CancellationTokenSource(); var ct = cts.Token;
            var found = new List<string>(); long visited = 0; var sw = Stopwatch.StartNew();
            lblInfo.Text = "Buscando proyectos en " + rootPath + " ...";
            Func<DirectoryInfo, bool> skip = di =>
            {
                long n = Interlocked.Increment(ref visited);
                if ((n % 1500) == 0) { int c; lock (found) c = found.Count; Ui(() => lblInfo.Text = "Buscando... " + c + " proyectos encontrados  ·  " + (int)sw.Elapsed.TotalSeconds + " s"); }
                if (di.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    try { if (di.Parent != null && File.Exists(Path.Combine(di.Parent.FullName, "package.json"))) lock (found) found.Add(di.FullName); } catch { }
                    return true;
                }
                return SkipDir(di);
            };
            try
            {
                await Task.Run(() => FsWalk.Files(rootPath, true, null, null, ct, skip));
                lblInfo.Text = "Calculando el tamaño de " + found.Count + " carpetas...";
                await Task.Run(() => Parallel.ForEach(found, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, p =>
                {
                    long s = FsWalk.SizeOf(p, ct); string proj = Path.GetDirectoryName(p); DateTime la = LastActivity(proj);
                    Ui(() => AddItem(p, proj, s, la));
                }));
            }
            catch (OperationCanceledException) { }
            catch (AggregateException) { }
            if (IsDisposed) return;
            busy = false; btnGo.Visible = true; btnStop.Visible = false;
            lv.Sort(); lv.FitLast(); UpdateInfo();
            if (lv.Items.Count == 0) lblInfo.Text = "No se encontraron proyectos con node_modules en esta carpeta.";
        }

        void AddItem(string nm, string proj, long size, DateTime last)
        {
            if (IsDisposed) return;
            var it = new ListViewItem(Path.GetFileName(proj)) { Tag = nm };
            it.SubItems.Add(Fmt.Bytes(size)); it.SubItems[1].Tag = size;
            it.SubItems.Add(last == DateTime.MinValue ? "?" : Fmt.Ago(last)); it.SubItems[2].Tag = last;
            it.SubItems.Add(proj); it.ToolTipText = nm;
            lv.Items.Add(it);
            if (last != DateTime.MinValue && (DateTime.Now - last).TotalDays > 90) it.Checked = true;
        }

        static string LongPath(string p) { return new string((char)92, 2) + "?" + (char)92 + p; }

        async void DeleteChecked()
        {
            var chk = lv.CheckedItems.Cast<ListViewItem>().ToList();
            if (chk.Count == 0) return;
            long sum = chk.Sum(i => (long)i.SubItems[1].Tag);
            if (!UI.Confirm(this, "Se eliminarán de forma permanente " + chk.Count + " carpetas node_modules (" + Fmt.Bytes(sum) + ").\n\nNo se pierde código: se vuelven a crear con npm install cuando abras el proyecto.\n\n¿Continuar?", "Eliminar node_modules")) return;
            busy = true; btnDel.Enabled = false; lblInfo.Text = "Eliminando...";
            int ok = 0, fail = 0; long freed = 0;
            await Task.Run(() =>
            {
                foreach (var it in chk)
                {
                    string p = (string)it.Tag; long sz = (long)it.SubItems[1].Tag;
                    bool valid = Path.GetFileName(p).Equals("node_modules", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Path.GetDirectoryName(p), "package.json"));
                    if (!valid) { fail++; continue; }
                    Sh.Run("cmd.exe", "/c rmdir /s /q " + Q.Arg(LongPath(p)), 900000);
                    if (Directory.Exists(p)) { fail++; continue; }
                    ok++; freed += sz; Log.Write("node_modules eliminado: " + p + " (" + Fmt.Bytes(sz) + ")");
                    var cap = it; Ui(() => cap.Remove());
                }
            });
            busy = false; UpdateInfo();
            lblInfo.Text = "Eliminadas " + ok + " carpetas, " + Fmt.Bytes(freed) + " liberados" + (fail > 0 ? "  ·  " + fail + " no se pudieron borrar" : "") + ".";
        }
    }
}
