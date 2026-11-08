using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class ResumenPage
    {
        void UpdateLive()
        {
            if (!Visible) return;
            live.Update();
            cCpu.Set(Math.Round(live.Cpu) + " %", live.Cpu, cpuShort + "  ·  " + Environment.ProcessorCount + " hilos");
            ulong used = live.RamTotal - live.RamAvail;
            cRam.Set(live.RamLoad + " %", live.RamLoad, Fmt.Bytes((long)used) + " en uso de " + Fmt.Bytes((long)live.RamTotal));
            string dsk = snap != null ? snap.DiskSummary : "";
            cDisk.Set(live.Disk >= 0 ? Math.Round(live.Disk) + " %" : "n/d", Math.Max(0, live.Disk), dsk.Length > 0 ? dsk : "Lectura y escritura");
            var sys = DriveInfoSafe();
            if (sys != null) cSpace.Set(Fmt.Bytes(sys.Value.Key) + " libres", 100.0 - sys.Value.Key * 100.0 / sys.Value.Value, "de " + Fmt.Bytes(sys.Value.Value) + " en total");
        }

        KeyValuePair<long, long>? DriveInfoSafe()
        {
            try { var d = new DriveInfo(Env.SysDrive); return new KeyValuePair<long, long>(d.AvailableFreeSpace, d.TotalSize); } catch { return null; }
        }

        async void RunAnalysis()
        {
            if (running) return;
            running = true; btnRe.Enabled = false; ring.SetBusy(); lblVerdict.Text = "Analizando..."; lblStatus.Text = "Preparando...";
            list.Controls.Clear();
            SysSnapshot s = null;
            try { s = await Task.Run(() => Diag.Run(m => Ui(() => lblStatus.Text = m))); }
            catch (Exception ex) { Log.Write("Diagnóstico falló: " + ex); }
            running = false;
            if (IsDisposed) return;
            btnRe.Enabled = true;
            if (s == null) { lblStatus.Text = "No se pudo completar el análisis"; return; }
            snap = s; ShowSnapshot();
            AnalyzeCleanable();
        }

        void ShowSnapshot()
        {
            ring.SetScore(snap.Score);
            lblVerdict.Text = snap.Score >= 80 ? "Buen estado" : snap.Score >= 55 ? "Se puede mejorar" : "Necesita atención";
            lblVerdict.ForeColor = snap.Score >= 80 ? UI.Good : snap.Score >= 55 ? UI.Warn : UI.Bad;
            var sb = new StringBuilder();
            sb.AppendLine("Equipo:  " + snap.Pc);
            sb.AppendLine("Windows:  " + snap.Os);
            sb.AppendLine("CPU:  " + cpuShort + " (" + snap.Threads + " hilos)");
            sb.AppendLine("RAM:  " + Fmt.Bytes((long)snap.RamTotal));
            if (snap.Gpu.Length > 0) sb.AppendLine("Gráfica:  " + snap.Gpu);
            sb.AppendLine("Disco:  " + snap.DiskSummary);
            sb.Append("Encendido hace:  " + Fmt.Dur(snap.Uptime));
            lblEquip.Text = sb.ToString();
            lblStatus.Text = "Analizado a las " + snap.Taken.ToString("HH:mm");
            Render();
        }

        void Render()
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) { list.Controls.Remove(c); c.Dispose(); }
            list.RowStyles.Clear(); list.RowCount = 0;
            foreach (var f in snap.Findings)
            {
                var row = new FindingRow(f) { Dock = DockStyle.Fill };
                row.ActionClick += fd =>
                {
                    if (fd.Fix == null) { MainForm.Instance.Go(fd.Page); return; }
                    string err = fd.Fix(this);
                    if (err == null) { UI.Toast(this, "Listo. Vuelvo a analizar el equipo."); RunAnalysis(); }
                    else if (err.Length > 0) UI.Toast(this, "No se pudo: " + err);
                };
                list.Controls.Add(row);
            }
            list.FinishRows();
            list.ResumeLayout(true);
        }

        async void AnalyzeCleanable()
        {
            try { await CleanerService.AnalyzeAllAsync(); } catch { }
            if (snap == null || IsDisposed) return;
            long total = CleanerService.Cats.Where(c => c.DefaultOn && c.Size > 0 && (!c.NeedsAdmin || Program.IsAdmin)).Sum(c => c.Size);
            snap.Findings.RemoveAll(f => f.Title.StartsWith("Se pueden liberar"));
            if (total >= 500L * 1048576)
                snap.Add(total >= 5L * 1073741824 ? Lvl.Warn : Lvl.Info, "Se pueden liberar " + Fmt.Bytes(total), "Archivos temporales, cachés de navegadores y restos de Windows que no hacen falta.", "limpieza", "Limpiar");
            snap.Recompute(); ring.SetScore(snap.Score);
            lblVerdict.Text = snap.Score >= 80 ? "Buen estado" : snap.Score >= 55 ? "Se puede mejorar" : "Necesita atención";
            lblVerdict.ForeColor = snap.Score >= 80 ? UI.Good : snap.Score >= 55 ? UI.Warn : UI.Bad;
            Render();
        }

        void SaveReport(object sender, EventArgs e)
        {
            if (snap == null) { UI.Toast(this, "Espera a que termine el análisis."); return; }
            using (var dlg = new SaveFileDialog { Title = "Guardar informe", Filter = "Texto (*.txt)|*.txt", FileName = "Informe_PulsoPC_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".txt", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { File.WriteAllText(dlg.FileName, PulsoPC.Report.Build(snap), Encoding.UTF8); ShellOpen.Location(dlg.FileName); }
                catch (Exception ex) { UI.Toast(this, "No se pudo guardar: " + ex.Message); }
            }
        }
    }
}
