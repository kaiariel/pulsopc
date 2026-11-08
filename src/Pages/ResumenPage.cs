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
    partial class ResumenPage : PageBase
    {
        MetricCard cCpu, cRam, cDisk, cSpace;
        ScoreRing ring; Label lblVerdict, lblEquip, lblStatus; DBTable list; Button btnRe, btnSave;
        readonly Timer timer = new Timer { Interval = 1000 };
        readonly LiveMetrics live = new LiveMetrics();
        SysSnapshot snap; bool running; string cpuShort = "";

        public ResumenPage() : base("Resumen del equipo", "Cómo está tu PC ahora mismo y qué la está frenando.")
        {
            btnSave = UI.Btn("Guardar informe"); btnRe = UI.Btn("Analizar de nuevo", BtnKind.Primary);
            btnSave.Click += SaveReport; btnRe.Click += (s, e) => RunAnalysis();
            var btnAi = UI.Btn("Informe para IA");
            btnAi.Click += (s, e) =>
            {
                if (snap == null) { UI.Toast(this, "Esperá a que termine el análisis."); return; }
                using (var f = new AiReportForm(snap)) f.ShowDialog(FindForm());
            };
            Actions.Controls.Add(btnAi); Actions.Controls.Add(btnSave); Actions.Controls.Add(btnRe);
            cpuShort = ShortCpu(Hw.CpuName());

            var grid = new DBTable { Dock = DockStyle.Top, Height = UI.S(134), ColumnCount = 4, RowCount = 1, BackColor = UI.Bg };
            for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            cCpu = Metric("Procesador", grid, 0); cRam = Metric("Memoria", grid, 1); cDisk = Metric("Disco (actividad)", grid, 2); cSpace = Metric("Espacio en " + Env.SysDrive.TrimEnd((char)92), grid, 3);

            var lower = new DBPanel { Dock = DockStyle.Fill, BackColor = UI.Bg };
            var right = new Card { Dock = DockStyle.Fill, Padding = UI.P(16, 14, 16, 12) };
            list = new DBTable { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = Color.White, Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 0) };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            var head = new DBPanel { Dock = DockStyle.Top, Height = UI.S(36), BackColor = Color.White };
            var h2 = UI.Lbl("Qué está frenando el equipo", UI.H2); h2.Location = new Point(0, 0);
            lblStatus = UI.Lbl("", UI.Small, UI.Mute); lblStatus.Dock = DockStyle.Right; lblStatus.TextAlign = ContentAlignment.MiddleRight;
            head.Controls.Add(h2); head.Controls.Add(lblStatus);
            right.Controls.Add(list); right.Controls.Add(head);

            var gap = new DBPanel { Dock = DockStyle.Left, Width = UI.S(14), BackColor = UI.Bg };
            var left = new Card { Dock = DockStyle.Left, Width = UI.S(270), Padding = UI.P(16, 14, 16, 12) };
            lblEquip = UI.Lbl("", UI.Small, UI.Ink); lblEquip.AutoSize = false; lblEquip.Dock = DockStyle.Fill;
            lblVerdict = UI.Lbl("Analizando...", UI.H2); lblVerdict.AutoSize = false; lblVerdict.Dock = DockStyle.Top; lblVerdict.Height = UI.S(34); lblVerdict.TextAlign = ContentAlignment.MiddleCenter;
            ring = new ScoreRing { Dock = DockStyle.Top, Height = UI.S(150) };
            left.Controls.Add(lblEquip); left.Controls.Add(lblVerdict); left.Controls.Add(ring);
            lower.Controls.Add(right); lower.Controls.Add(gap); lower.Controls.Add(left);

            Body.Controls.Add(lower);
            Body.Controls.Add(new DBPanel { Dock = DockStyle.Top, Height = UI.S(14), BackColor = UI.Bg });
            Body.Controls.Add(grid);
            if (!Program.IsAdmin) AddAdminBanner();
            timer.Tick += (s, e) => UpdateLive();
        }

        MetricCard Metric(string title, TableLayoutPanel grid, int col)
        {
            var c = new MetricCard(title) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, col < 3 ? UI.S(12) : 0, 0) };
            grid.Controls.Add(c, col, 0); return c;
        }

        void AddAdminBanner()
        {
            Body.Controls.Add(new DBPanel { Dock = DockStyle.Top, Height = UI.S(12), BackColor = UI.Bg });
            var b = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.WarnLt, Padding = UI.P(14, 0, 14, 0) };
            var t = UI.Lbl("Sin permisos de administrador: la limpieza del sistema, los servicios y las reparaciones no estarán disponibles.", UI.Body, ColorTranslator.FromHtml("#7A4A05"));
            t.Location = new Point(UI.S(14), UI.S(13));
            var bt = UI.Btn("Reiniciar como administrador"); bt.Height = UI.S(30); bt.Anchor = AnchorStyles.Right | AnchorStyles.Top; bt.Location = new Point(b.Width - bt.Width - UI.S(10), UI.S(8));
            bt.Click += (s, e) => { if (Program.RestartAsAdmin()) FindForm().Close(); };
            b.Controls.Add(t); b.Controls.Add(bt);
            b.Resize += (s, e) => bt.Location = new Point(b.Width - bt.Width - UI.S(10), UI.S(8));
            Body.Controls.Add(b);
        }

        static string ShortCpu(string n)
        {
            n = n.Replace("(R)", "").Replace("(TM)", "").Replace("CPU", "").Replace("Processor", "").Replace("Intel", "");
            n = System.Text.RegularExpressions.Regex.Replace(n, "[0-9]+th Gen", "");
            int at = n.IndexOf('@'); if (at > 0) n = n.Substring(0, at);
            return System.Text.RegularExpressions.Regex.Replace(n, "[ ]{2,}", " ").Trim();
        }

        protected override void OnFirstShow()
        {
            Task.Run(() => live.InitDisk());
            RunAnalysis();
        }
        protected override void OnShown() { timer.Start(); }
        public override void OnHidden() { timer.Stop(); }
    }
}
