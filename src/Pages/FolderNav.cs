using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class FolderView
    {
        async void Navigate(string path)
        {
            if (cts != null) cts.Cancel();
            cts = new CancellationTokenSource(); var ct = cts.Token;
            current = path; txtPath.Text = path ?? "Este equipo"; lv.Items.Clear(); UpdateButtons();
            if (path == null)
            {
                foreach (var d in EspacioPage.FixedDrives())
                {
                    try
                    {
                        var di = new DriveInfo(d); long used = di.TotalSize - di.AvailableFreeSpace;
                        var it = new ListViewItem(d + "  " + di.VolumeLabel) { Tag = new DirectoryInfo(d) };
                        it.SubItems.Add(Fmt.Bytes(used)); it.SubItems[1].Tag = used;
                        it.SubItems.Add(Math.Round(used * 100.0 / di.TotalSize) + " %"); it.SubItems[2].Tag = used * 100.0 / di.TotalSize;
                        it.SubItems.Add("Unidad"); it.SubItems.Add("");
                        lv.Items.Add(it);
                    }
                    catch { }
                }
                lblInfo.Text = "Elige una unidad para ver qué ocupa más."; return;
            }
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(path).EnumerateFileSystemInfos().Where(e => (e.Attributes & FileAttributes.ReparsePoint) == 0).ToList(); }
            catch (Exception ex) { lblInfo.Text = "No se puede abrir esta carpeta: " + ex.Message; return; }

            var dirs = new List<KeyValuePair<DirectoryInfo, ListViewItem>>();
            long fileTotal = 0;
            lv.BeginUpdate();
            foreach (var e in entries)
            {
                bool isDir = e is DirectoryInfo;
                var it = new ListViewItem(e.Name) { Tag = e };
                if (isDir) { it.SubItems.Add("calculando..."); it.SubItems[1].Tag = -1L; }
                else
                {
                    long len = Safe.IsCloudOnly(e.Attributes) ? 0 : ((FileInfo)e).Length; fileTotal += len;
                    it.SubItems.Add(Fmt.Bytes(len)); it.SubItems[1].Tag = len;
                }
                it.SubItems.Add(""); it.SubItems[2].Tag = 0.0;
                it.SubItems.Add(isDir ? "Carpeta" : (string.IsNullOrEmpty(e.Extension) ? "Archivo" : e.Extension.TrimStart('.').ToLowerInvariant()));
                it.SubItems.Add(e.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                lv.Items.Add(it);
                if (isDir) dirs.Add(new KeyValuePair<DirectoryInfo, ListViewItem>((DirectoryInfo)e, it));
            }
            lv.EndUpdate(); lv.FitLast();
            lblInfo.Text = "Calculando el tamaño de " + dirs.Count + " carpetas...";
            long total = fileTotal; int done = 0;
            try
            {
                await Task.Run(() => Parallel.ForEach(dirs, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, kv =>
                {
                    long s = 0; try { s = FsWalk.SizeOf(kv.Key.FullName, ct); } catch (OperationCanceledException) { throw; } catch { }
                    Interlocked.Add(ref total, s); int d = Interlocked.Increment(ref done);
                    Ui(() => { if (ct.IsCancellationRequested) return; kv.Value.SubItems[1].Text = Fmt.Bytes(s); kv.Value.SubItems[1].Tag = s; lblInfo.Text = "Calculando... " + d + " de " + dirs.Count; });
                }));
            }
            catch (OperationCanceledException) { return; }
            catch (AggregateException) { return; }
            if (IsDisposed || ct.IsCancellationRequested) return;
            foreach (ListViewItem it in lv.Items)
            {
                long s = it.SubItems[1].Tag is long ? (long)it.SubItems[1].Tag : 0;
                double pct = total > 0 ? s * 100.0 / total : 0;
                it.SubItems[2].Text = pct >= 1 ? Math.Round(pct) + " %" : (s > 0 ? "<1 %" : ""); it.SubItems[2].Tag = pct;
            }
            lv.SetSort(1, false);
            lblInfo.Text = "Total: " + Fmt.Bytes(total) + " en " + entries.Count + " elementos.";
        }

        void DeleteSel()
        {
            var sel = lv.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag is FileSystemInfo).ToList();
            if (sel.Count == 0) return;
            string names = string.Join("\n", sel.Take(8).Select(i => "  •  " + i.Text + "  (" + i.SubItems[1].Text + ")").ToArray()) + (sel.Count > 8 ? "\n  ...y " + (sel.Count - 8) + " más" : "");
            if (!UI.Confirm(this, "Se enviarán a la papelera " + sel.Count + " elemento(s):\n\n" + names + "\n\nLas carpetas se mueven con todo su contenido.", "Enviar a la papelera")) return;
            int fail = 0; string last = "";
            foreach (var it in sel)
            {
                string err = EspacioPage.Recycle(((FileSystemInfo)it.Tag).FullName);
                if (err != null) { fail++; last = err; } else it.Remove();
            }
            lblInfo.Text = "Enviado a la papelera. Vacíala para recuperar el espacio.";
            if (fail > 0) UI.Toast(this, "No se pudieron borrar " + fail + " elemento(s):\n" + last);
        }
    }
}
