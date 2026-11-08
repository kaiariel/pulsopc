using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class LimpiezaPage
    {
        void BuildItems()
        {
            muteCheck = true;
            lv.BeginUpdate(); lv.Items.Clear(); items.Clear();
            foreach (var c in CleanerService.Cats)
            {
                var it = new ListViewItem(c.Name) { Tag = c, Group = lv.Groups[c.Group] };
                it.SubItems.Add("Calculando..."); it.SubItems.Add(""); it.SubItems.Add(c.Desc);
                it.ToolTipText = c.Desc + (c.Note.Length > 0 ? "  (" + c.Note + ")" : "");
                items[c] = it; lv.Items.Add(it);
            }
            lv.EndUpdate(); muteCheck = false;
            UpdateTotals();
        }

        bool NeedsAdminBlocked(CleanCat c) { return c.NeedsAdmin && !Program.IsAdmin; }

        void UpdateItem(CleanCat c, bool firstTime)
        {
            ListViewItem it;
            if (IsDisposed || !items.TryGetValue(c, out it)) return;
            muteCheck = true;
            try
            {
                if (c.Size < 0) { it.SubItems[1].Text = "Calculando..."; return; }
                if (c.HideIfEmpty && c.Size == 0 && c.Kind != CatKind.RecycleBin) { it.Remove(); items.Remove(c); return; }
                it.SubItems[1].Text = Fmt.Bytes(c.Size); it.SubItems[1].Tag = c.Size;
                it.SubItems[2].Text = c.Files > 0 ? c.Files.ToString("N0") : ""; it.SubItems[2].Tag = c.Files;
                string det = c.Desc + (c.Note.Length > 0 ? "  [" + c.Note + "]" : "");
                if (NeedsAdminBlocked(c)) { det = "Requiere permisos de administrador"; it.ForeColor = UI.Mute; it.Checked = false; }
                else if (firstTime) it.Checked = c.DefaultOn && c.Size > 0;
                it.SubItems[3].Text = det;
            }
            finally { muteCheck = false; UpdateTotals(); }
        }

        void SetChecks(Func<CleanCat, bool> rule)
        {
            muteCheck = true;
            foreach (var kv in items) kv.Value.Checked = rule(kv.Key) && kv.Key.Size > 0 && !NeedsAdminBlocked(kv.Key);
            muteCheck = false; UpdateTotals();
        }

        void UpdateTotals()
        {
            long sel = 0, all = 0;
            foreach (var kv in items) { if (kv.Key.Size > 0) { all += kv.Key.Size; if (kv.Value.Checked) sel += kv.Key.Size; } }
            lblTotal.Text = "Seleccionado: " + Fmt.Bytes(sel) + "   ·   Recuperable: " + Fmt.Bytes(all);
            lblTotal.Location = new Point(lblTotal.Parent.Width - lblTotal.Width, UI.S(8));
            btnClean.Enabled = !busy && !loading && sel > 0;
        }

        async void Analyze(bool force)
        {
            if (busy || loading) return;
            bool fresh = !force && !CleanerService.IsAnalyzing && CleanerService.LastAnalysis != DateTime.MinValue && (DateTime.Now - CleanerService.LastAnalysis).TotalMinutes < 10;
            loading = true; btnAn.Enabled = false; btnClean.Enabled = false;
            BuildItems();
            if (!fresh) ShowProgress("Calculando el tamaño de cada categoría...", false);
            try
            {
                if (!fresh) await CleanerService.AnalyzeAllAsync();
            }
            catch { }
            if (IsDisposed) return;
            foreach (var c in CleanerService.Cats.ToList()) UpdateItem(c, true);
            loading = false; btnAn.Enabled = true; HideProgress();
            lv.SetSort(1, false);
            UpdateTotals();
            RefreshTools();
        }

        void ShowProgress(string text, bool cancellable)
        {
            progress.Visible = true; lblStatus.Text = text; btnCancel.Visible = cancellable; pb.Visible = cancellable; pb.Value = 0;
        }
        void HideProgress() { progress.Visible = false; }

        async void DoClean()
        {
            var sel = items.Where(kv => kv.Value.Checked && kv.Key.Size > 0 && !NeedsAdminBlocked(kv.Key)).Select(kv => kv.Key).ToList();
            if (sel.Count == 0) { UI.Toast(this, "No hay nada seleccionado para limpiar."); return; }
            long total = sel.Sum(c => c.Size);
            string list = string.Join("\n", sel.Take(14).Select(c => "  •  " + c.Name + "  (" + Fmt.Bytes(c.Size) + ")").ToArray()) + (sel.Count > 14 ? "\n  ...y " + (sel.Count - 14) + " más" : "");
            string msg = "Se van a eliminar unos " + Fmt.Bytes(total) + " de estas categorías:\n\n" + list;
            if (sel.Any(c => c.Group == "Navegadores" || c.Group == "Aplicaciones")) msg += "\n\nCierra los navegadores y programas abiertos para liberar todo el espacio.";
            if (sel.Any(c => c.Id == "recycle")) msg += "\n\nLa papelera se vaciará de forma definitiva.";
            msg += "\n\n¿Continuar?";
            if (!UI.Confirm(this, msg, "Limpiar archivos", false)) return;

            busy = true; btnAn.Enabled = false; btnClean.Enabled = false;
            cts = new CancellationTokenSource(); var ct = cts.Token;
            ShowProgress("Preparando...", true);
            long freed = 0; int done = 0;
            await Task.Run(() =>
            {
                foreach (var c in sel)
                {
                    if (ct.IsCancellationRequested) break;
                    var cc = c; int idx = done;
                    Ui(() => { lblStatus.Text = "Limpiando: " + cc.Name + "  (" + (idx + 1) + " de " + sel.Count + ")"; pb.Value = idx * 100.0 / sel.Count; });
                    long f = 0;
                    try { f = CleanerService.Clean(cc, ct, m => Ui(() => lblStatus.Text = m)); } catch (OperationCanceledException) { break; } catch (Exception ex) { Log.Write("Error limpiando " + cc.Name + ": " + ex.Message); }
                    freed += f; done++;
                    CleanerService.Reanalyze(cc);
                    Ui(() => UpdateItem(cc, false));
                }
            });
            busy = false; HideProgress(); btnAn.Enabled = true; UpdateTotals(); RefreshTools();
            Log.Write("Limpieza total: " + Fmt.Bytes(freed) + " liberados en " + done + " categorías");
            UI.Toast(this, freed > 0 ? "Listo. Se liberaron " + Fmt.Bytes(freed) + "." : "No se pudo liberar espacio: los archivos estaban en uso. Cierra los programas e inténtalo de nuevo.");
        }
    }
}
