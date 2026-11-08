using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    class ReparaPage : PageBase
    {
        DBTable list; TextBox txt; Label lblPartial, lblState; ComboBox cmbDrive; Button btnCancel, btnCopy, btnClear, btnReboot;
        readonly List<RepairRow> rows = new List<RepairRow>();
        CancellationTokenSource cts; bool running;

        public ReparaPage() : base("Reparar Windows", "Herramientas oficiales de Windows para arreglar archivos dañados, el disco, la red y más. Algunas tardan varios minutos.")
        {
            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.Bg };
            var l = UI.Lbl("Unidad para revisar y optimizar:", UI.Body, UI.Mute); l.Location = new Point(0, UI.S(12));
            cmbDrive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UI.Body, Width = UI.S(110), Location = new Point(UI.S(220), UI.S(8)) };
            foreach (var d in EspacioPage.FixedDrives()) cmbDrive.Items.Add(d.Substring(0, 2));
            cmbDrive.SelectedIndex = Math.Max(0, cmbDrive.Items.IndexOf(Env.SysDrive.Substring(0, 2)));
            top.Controls.Add(l); top.Controls.Add(cmbDrive);

            list = new DBTable { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = UI.Bg, Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 0) };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var console = new Card { Dock = DockStyle.Bottom, Height = UI.S(236), Padding = UI.P(14, 10, 14, 10) };
            var bar = new DBPanel { Dock = DockStyle.Top, Height = UI.S(40), BackColor = Color.White };
            lblState = UI.Lbl("Consola", UI.H2); lblState.Location = new Point(0, UI.S(4));
            btnCancel = UI.Btn("Cancelar", BtnKind.Danger); btnCopy = UI.Btn("Copiar"); btnClear = UI.Btn("Limpiar"); btnReboot = UI.Btn("Reiniciar equipo");
            foreach (var b in new[] { btnCancel, btnCopy, btnClear, btnReboot }) { b.Height = UI.S(30); b.Anchor = AnchorStyles.Top | AnchorStyles.Right; bar.Controls.Add(b); }
            bar.Controls.Add(lblState);
            bar.Resize += (s, e) => { int x = bar.Width; foreach (var b in new[] { btnReboot, btnClear, btnCopy, btnCancel }) { x -= b.Width + UI.S(8); b.Location = new Point(x, UI.S(2)); } };
            btnCancel.Enabled = false;
            btnCancel.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            btnCopy.Click += (s, e) => { if (txt.TextLength > 0) Clipboard.SetText(txt.Text); };
            btnClear.Click += (s, e) => txt.Clear();
            btnReboot.Click += (s, e) => Reboot();
            lblPartial = UI.Lbl("", UI.Mono, ColorTranslator.FromHtml("#8DB4FF")); lblPartial.AutoSize = false; lblPartial.Dock = DockStyle.Bottom; lblPartial.Height = UI.S(24); lblPartial.BackColor = ColorTranslator.FromHtml("#0B1220"); lblPartial.Padding = UI.P(8, 3, 0, 0);
            txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = UI.Mono, BackColor = ColorTranslator.FromHtml("#0B1220"), ForeColor = ColorTranslator.FromHtml("#D6DEEB"), BorderStyle = BorderStyle.None };
            console.Controls.Add(txt); console.Controls.Add(lblPartial); console.Controls.Add(bar);

            Body.Controls.Add(list);
            Body.Controls.Add(new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(10), BackColor = UI.Bg });
            Body.Controls.Add(console);
            Body.Controls.Add(top);
        }

        protected override void OnFirstShow()
        {
            foreach (var a in RepairActions.Build())
            {
                var row = new RepairRow(a) { Dock = DockStyle.Fill };
                row.Btn.Enabled = !a.NeedsAdmin || Program.IsAdmin;
                var act = a; row.Btn.Click += (s, e) => RunAction(act);
                rows.Add(row); list.Controls.Add(row);
            }
            list.FinishRows();
            Append("Elige una herramienta de la lista. Aquí verás lo que hace Windows en tiempo real.");
        }

        void Append(string line)
        {
            if (txt.TextLength > 150000) txt.Text = txt.Text.Substring(txt.TextLength - 80000);
            txt.AppendText(line + Environment.NewLine);
        }

        void SetRunning(bool on)
        {
            running = on; btnCancel.Enabled = on; cmbDrive.Enabled = !on; btnReboot.Enabled = !on;
            foreach (var r in rows) r.Btn.Enabled = !on && (!r.A.NeedsAdmin || Program.IsAdmin);
            lblState.Text = on ? "Consola  (ejecutando...)" : "Consola";
        }

        async void RunAction(RepairAction a)
        {
            if (running) return;
            if (a.NeedsAdmin && !Program.IsAdmin) { UI.Toast(this, "Esta herramienta necesita permisos de administrador."); return; }
            if (a.Confirm != null && !UI.Confirm(this, a.Confirm, a.Title, false)) return;
            SetRunning(true); cts = new CancellationTokenSource();
            var runner = new Runner { Ct = cts.Token, Drive = (string)cmbDrive.SelectedItem ?? Env.SysDrive.Substring(0, 2) };
            runner.OnLine = ln => Ui(() => Append(ln)); runner.OnPartial = p => Ui(() => lblPartial.Text = p);
            Append(""); Append("=== " + a.Title + "  (" + DateTime.Now.ToString("HH:mm:ss") + ") ===");
            var sw = Stopwatch.StartNew(); int code = 0;
            try { code = await Task.Run(() => a.Run(runner)); }
            catch (Exception ex) { Append("Error: " + ex.Message); code = -3; }
            Append("=== Terminado en " + (int)sw.Elapsed.TotalMinutes + " min " + sw.Elapsed.Seconds + " s" + (code == 0 ? "" : "  (código " + code + ")") + " ===");
            Log.Write("Reparar: " + a.Title + " (código " + code + ", " + (int)sw.Elapsed.TotalSeconds + " s)");
            lblPartial.Text = ""; SetRunning(false);
        }

        void Reboot()
        {
            if (!UI.Confirm(this, "El equipo se reiniciará en 30 segundos. Guarda tu trabajo antes.\n\nPara cancelarlo, abre el menú Inicio, escribe cmd y ejecuta:  shutdown /a\n\n¿Reiniciar ahora?", "Reiniciar equipo")) return;
            try { Process.Start("shutdown.exe", "/r /t 30 /c " + Q.Arg("PulsoPC: reinicio para completar reparaciones")); Log.Write("Reinicio solicitado desde Reparar"); }
            catch (Exception ex) { UI.Toast(this, ex.Message); }
        }
    }
}
