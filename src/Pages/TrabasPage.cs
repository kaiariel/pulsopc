using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PulsoPC
{
    class TrabasPage : PageBase
    {
        readonly Button btnToggle, btnMark;
        readonly Label lblState, vTitle, vBody;
        readonly CheckBox chkAuto;
        readonly Spark sRam, sPages, sCpu, sBar;
        readonly SortableList odd = new SortableList();
        readonly Timer timer = new Timer { Interval = HangMonitor.IntervalMs };
        DateTime lastOdd = DateTime.MinValue;

        public static event Action<HangVerdict> Marked;
        public static void RaiseMarked(HangVerdict v) { var h = Marked; if (h != null) h(v); }

        public TrabasPage() : base("Monitor de trabas", "Cuando algo se trabe, marcá el momento y te digo qué pasó.")
        {
            btnToggle = UI.Btn("Iniciar monitor", BtnKind.Secondary, UI.S(150)); btnToggle.Click += (s, e) => Toggle();
            btnMark = UI.Btn("Se trabó recién", BtnKind.Primary, UI.S(150)); btnMark.Click += (s, e) => DoMark();
            Actions.Controls.Add(btnToggle); Actions.Controls.Add(btnMark);

            // Estado y opciones
            var top = new Card { Dock = DockStyle.Top, Height = UI.S(66), Padding = UI.P(16, 10, 16, 8) };
            lblState = UI.Lbl("", UI.Body, UI.Ink); lblState.Location = new Point(UI.S(16), UI.S(10));
            chkAuto = new CheckBox { Text = "Iniciar el monitor al abrir PulsoPC", Font = UI.Small, AutoSize = true, Location = new Point(UI.S(16), UI.S(36)), Checked = Store.Get("monitor.auto") == "1" };
            chkAuto.CheckedChanged += (s, e) => Store.Set("monitor.auto", chkAuto.Checked ? "1" : null);
            var lnk = new LinkLabel { Text = "Abrir carpeta de registros", Font = UI.Small, AutoSize = true, Location = new Point(UI.S(290), UI.S(37)), LinkColor = UI.Accent };
            lnk.LinkClicked += (s, e) => { try { System.IO.Directory.CreateDirectory(Log.Dir); Process.Start("explorer.exe", Q.Arg(Log.Dir)); } catch { } };
            top.Controls.Add(lblState); top.Controls.Add(chkAuto); top.Controls.Add(lnk);

            // Gráficos de los últimos 10 minutos
            var grid = new DBTable { Dock = DockStyle.Top, Height = UI.S(118), ColumnCount = 4, RowCount = 1, BackColor = UI.Bg };
            for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            sRam = AddSpark(grid, 0, "Memoria libre"); sPages = AddSpark(grid, 1, "Disco usado como memoria");
            sCpu = AddSpark(grid, 2, "Procesador"); sBar = AddSpark(grid, 3, "Barra de tareas (respuesta)");

            // Momentos raros y veredicto
            var lower = new DBPanel { Dock = DockStyle.Fill, BackColor = UI.Bg };
            var right = new Card { Dock = DockStyle.Right, Width = UI.S(390), Padding = UI.P(16, 14, 16, 12) };
            var rh = UI.Lbl("Última traba marcada", UI.H2); rh.Dock = DockStyle.Top;
            vTitle = UI.Lbl("Todavía no marcaste ninguna.", UI.BodyBold, UI.Mute); vTitle.AutoSize = false; vTitle.Dock = DockStyle.Top; vTitle.Height = UI.S(44); vTitle.Padding = UI.P(0, 10, 0, 0);
            vBody = UI.Lbl("Cuando el equipo se trabe, apenas se destrabe tocá «Se trabó recién» (o Ctrl+Alt+P desde cualquier ventana). Analizo el minuto y medio anterior.", UI.Body, UI.Ink);
            vBody.AutoSize = false; vBody.Dock = DockStyle.Fill;
            right.Controls.Add(vBody); right.Controls.Add(vTitle); right.Controls.Add(rh);
            var gap = new DBPanel { Dock = DockStyle.Right, Width = UI.S(14), BackColor = UI.Bg };
            var left = new Card { Dock = DockStyle.Fill, Padding = UI.P(16, 14, 16, 12) };
            var lh = UI.Lbl("Momentos raros", UI.H2); lh.Dock = DockStyle.Top;
            odd.Dock = DockStyle.Fill;
            odd.Columns.Add("Hora", UI.S(72)); odd.Columns.Add("Qué pasó", UI.S(250)); odd.Columns.Add("Al frente", UI.S(90)); odd.Columns.Add("Más disco / CPU", UI.S(160));
            odd.SetSort(0, false);
            left.Controls.Add(odd); left.Controls.Add(new DBPanel { Dock = DockStyle.Top, Height = UI.S(8), BackColor = Color.White }); left.Controls.Add(lh);
            lower.Controls.Add(left); lower.Controls.Add(gap); lower.Controls.Add(right);

            Body.Controls.Add(lower);
            Body.Controls.Add(new DBPanel { Dock = DockStyle.Top, Height = UI.S(14), BackColor = UI.Bg });
            Body.Controls.Add(grid);
            Body.Controls.Add(new DBPanel { Dock = DockStyle.Top, Height = UI.S(14), BackColor = UI.Bg });
            Body.Controls.Add(top);

            timer.Tick += (s, e) => Refresh2();
            Marked += v => Ui(() => ShowVerdict(v));
            Disposed += (s, e) => timer.Stop();
        }

        Spark AddSpark(TableLayoutPanel g, int col, string title)
        {
            var sp = new Spark { Title = title, Dock = DockStyle.Fill, Margin = new Padding(0, 0, col < 3 ? UI.S(12) : 0, 0) };
            g.Controls.Add(sp, col, 0); return sp;
        }

        protected override void OnShown() { timer.Start(); Refresh2(); if (HangMonitor.LastVerdict != null) ShowVerdict(HangMonitor.LastVerdict); }
        public override void OnHidden() { timer.Stop(); }

        void Toggle()
        {
            if (HangMonitor.Running) HangMonitor.Stop(); else HangMonitor.Start();
            Refresh2();
        }

        void DoMark()
        {
            if (!HangMonitor.Running) { UI.Toast(this, "Primero iniciá el monitor y dejalo andando. Cuando se trabe, marcá el momento."); return; }
            var v = HangMonitor.Mark();
            RaiseMarked(v);
        }

        void ShowVerdict(HangVerdict v)
        {
            vTitle.Text = v.At.ToString("HH:mm:ss") + "  ·  " + v.Title;
            vTitle.ForeColor = UI.LevelColor(v.Level);
            vBody.Text = string.Join("\n\n", v.Lines.ToArray());
        }

        void Refresh2()
        {
            bool on = HangMonitor.Running;
            btnToggle.Text = on ? "Detener monitor" : "Iniciar monitor";
            lblState.Text = on
                ? "Monitor activo desde las " + HangMonitor.StartedAt.ToString("HH:mm") + "  ·  " + HangMonitor.Count.ToString("N0") + " muestras  ·  " + (MainForm.HotkeyOk ? "Ctrl+Alt+P marca una traba desde cualquier ventana." : "Otra app ocupa Ctrl+Alt+P: marcá con el botón.")
                : "Monitor detenido. Inicialo y dejalo andando mientras usás la PC (consume menos del 1 % de CPU).";
            lblState.ForeColor = on ? UI.Good : UI.Ink;

            var data = HangMonitor.Recent(TimeSpan.FromMinutes(10));
            if (data.Count == 0) { foreach (var sp in new[] { sRam, sPages, sCpu, sBar }) sp.SetData(new float[0], "sin datos", false); return; }
            var last = data[data.Count - 1];
            sRam.SetData(data.Select(x => (float)x.RamFreeMb).ToArray(), (last.RamFreeMb / 1024.0).ToString("0.0") + " GB", last.RamFreeMb < 800, lowIsBad: true);
            sPages.SetData(data.Select(x => (float)Math.Max(0, x.PagesIn)).ToArray(), last.PagesIn < 0 ? "n/d" : last.PagesIn.ToString("N0") + "/s", last.PagesIn >= 5000, ceil: 2000);
            sCpu.SetData(data.Select(x => (float)x.Cpu).ToArray(), last.Cpu + " %", last.Cpu >= 90, ceil: 100);
            sBar.SetData(data.Select(x => (float)Math.Max(0, x.BarMs)).ToArray(), last.BarHung ? "colgada" : last.BarMs < 0 ? "n/d" : last.BarMs + " ms", last.BarHung || last.BarMs >= 1000, ceil: 1000);

            var news = data.Where(x => x.Odd && x.T > lastOdd).ToList();
            if (news.Count > 0)
            {
                odd.BeginUpdate();
                foreach (var x in news)
                {
                    var it = new ListViewItem(x.T.ToString("HH:mm:ss")); it.SubItems[0].Tag = x.T;
                    it.SubItems.Add(string.Join(" · ", x.Flags.ToArray()));
                    it.SubItems.Add(x.FgApp);
                    it.SubItems.Add(string.Join("  ·  ", new[] { x.TopRead, x.TopCpu }.Where(t => t.Length > 0).ToArray()));
                    if (x.Flags.Any(f => f.StartsWith("barra") || f.StartsWith("RAM") || f.StartsWith("hasta"))) it.ForeColor = UI.Bad;
                    odd.Items.Add(it);
                }
                odd.Sort(); odd.EndUpdate();
                lastOdd = news[news.Count - 1].T;
            }
        }
    }

    // Mini gráfico de línea con título y valor actual
    class Spark : Control
    {
        public string Title = ""; float[] data = new float[0]; string value = ""; bool alarm, lowBad; float ceil;
        public Spark() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }

        public void SetData(float[] d, string val, bool alarmNow, float ceil = 0, bool lowIsBad = false)
        {
            data = d; value = val; alarm = alarmNow; this.ceil = ceil; lowBad = lowIsBad; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UI.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = UI.Round(r, UI.S(10))) { g.FillPath(Brushes.White, path); using (var pen = new Pen(UI.Line)) g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, Title, UI.Small, new Point(UI.S(14), UI.S(10)), UI.Mute, TextFormatFlags.NoPadding);
            Color c = alarm ? UI.Bad : UI.Accent;
            TextRenderer.DrawText(g, value, UI.H2, new Point(UI.S(14), UI.S(28)), alarm ? UI.Bad : UI.Ink, TextFormatFlags.NoPadding);
            if (data.Length < 2) return;
            float top = UI.S(58), bottom = Height - UI.S(10), left = UI.S(12), right = Width - UI.S(12);
            float max = Math.Max(ceil, data.Max()); if (max <= 0) max = 1;
            var pts = new PointF[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                float x = left + (right - left) * i / (data.Length - 1);
                float y = bottom - (bottom - top) * Math.Min(1f, data[i] / max);
                pts[i] = new PointF(x, y);
            }
            using (var fill = new GraphicsPath())
            {
                fill.AddLines(pts); fill.AddLine(pts[pts.Length - 1], new PointF(right, bottom)); fill.AddLine(new PointF(right, bottom), new PointF(left, bottom)); fill.CloseFigure();
                using (var br = new SolidBrush(Color.FromArgb(28, c))) g.FillPath(br, fill);
            }
            using (var pen = new Pen(c, Math.Max(1.5f, UI.S(2)))) g.DrawLines(pen, pts);
        }
    }
}
