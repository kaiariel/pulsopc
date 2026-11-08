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
    partial class LargeFilesView
    {
        void Ui(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        static bool SkipDir(DirectoryInfo d)
        {
            string n = d.Name;
            if (n.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || n.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) return true;
            return d.FullName.TrimEnd(Path.DirectorySeparatorChar).Equals(Env.Win.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        async void Search()
        {
            if (busy || string.IsNullOrEmpty(root) || root == Pick) return;
            busy = true; btnGo.Enabled = false; btnStop.Visible = true; lv.Items.Clear(); UpdateButtons();
            cts = new CancellationTokenSource(); var ct = cts.Token;
            string r = root; long min = (long)num.Value * 1048576L;
            var found = new List<FileInfo>(); long scanned = 0; var sw = Stopwatch.StartNew();
            lblInfo.Text = "Buscando en " + r + " ...";
            try
            {
                await Task.Run(() => FsWalk.Files(r, true, fi =>
                {
                    long n = Interlocked.Increment(ref scanned);
                    if ((n & 2047) == 0) { long c = n; int f; lock (found) f = found.Count; Ui(() => lblInfo.Text = "Revisados " + c.ToString("N0") + " archivos  ·  " + f + " grandes encontrados  ·  " + (int)sw.Elapsed.TotalSeconds + " s"); }
                    if (Safe.IsCloudOnly(fi.Attributes)) return;
                    if (fi.Length >= min) lock (found) found.Add(fi);
                }, null, ct, SkipDir));
            }
            catch (OperationCanceledException) { }
            catch { }
            if (IsDisposed) return;
            var top = found.OrderByDescending(f => f.Length).Take(1500).ToList();
            lv.BeginUpdate();
            foreach (var f in top)
            {
                var it = new ListViewItem(Fmt.Bytes(f.Length)) { Tag = FsWalk.NoPrefix(f.FullName) };
                it.SubItems[0].Tag = f.Length;
                it.SubItems.Add(f.Name); var m = it.SubItems.Add(f.LastWriteTime.ToString("yyyy-MM-dd")); m.Tag = f.LastWriteTime;
                it.SubItems.Add(f.Extension.TrimStart('.').ToLowerInvariant()); it.SubItems.Add(FsWalk.NoPrefix(f.DirectoryName));
                it.ToolTipText = FsWalk.NoPrefix(f.FullName);
                lv.Items.Add(it);
            }
            lv.EndUpdate(); lv.FitLast(); lv.Sort();
            long total = top.Sum(f => f.Length);
            lblInfo.Text = top.Count + " archivos de más de " + num.Value + " MB, suman " + Fmt.Bytes(total) + (ct.IsCancellationRequested ? "  (búsqueda detenida)" : "") + "  ·  " + (int)sw.Elapsed.TotalSeconds + " s";
            busy = false; btnGo.Enabled = true; btnStop.Visible = false; UpdateButtons();
        }

        void DeleteSel()
        {
            var sel = lv.SelectedItems.Cast<ListViewItem>().ToList();
            if (sel.Count == 0) return;
            long total = sel.Sum(i => (long)i.SubItems[0].Tag);
            string names = string.Join("\n", sel.Take(8).Select(i => "  •  " + i.SubItems[1].Text + "  (" + i.Text + ")").ToArray()) + (sel.Count > 8 ? "\n  ...y " + (sel.Count - 8) + " más" : "");
            if (!UI.Confirm(this, "Se enviarán a la papelera " + sel.Count + " archivo(s), " + Fmt.Bytes(total) + " en total:\n\n" + names + "\n\nSi un archivo es demasiado grande para la papelera, Windows preguntará si quieres borrarlo definitivamente.", "Enviar a la papelera")) return;
            int fail = 0; string last = ""; long freed = 0;
            foreach (var it in sel)
            {
                string err = EspacioPage.Recycle((string)it.Tag);
                if (err != null) { fail++; last = err; } else { freed += (long)it.SubItems[0].Tag; it.Remove(); }
            }
            lblInfo.Text = "Enviados a la papelera: " + Fmt.Bytes(freed) + ". Recuerda vaciarla para recuperar el espacio.";
            if (fail > 0) UI.Toast(this, "No se pudieron borrar " + fail + " archivo(s):\n" + last);
        }
    }
}
