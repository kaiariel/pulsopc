using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PulsoPC
{
    class MainForm : Form
    {
        public static MainForm Instance;
        readonly DBPanel host = new DBPanel();
        readonly Dictionary<string, PageBase> pages = new Dictionary<string, PageBase>();
        readonly Dictionary<string, NavButton> navs = new Dictionary<string, NavButton>();
        PageBase cur;

        static string Gl(int code) { return ((char)code).ToString(); }

        public MainForm()
        {
            Instance = this;
            Text = "PulsoPC - Optimizador de Windows";
            Font = UI.Body; BackColor = UI.Bg; DoubleBuffered = true;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(UI.S(1360), wa.Width - UI.S(40)), Math.Min(UI.S(800), wa.Height - UI.S(40)));
            MinimumSize = new Size(Math.Min(UI.S(1020), wa.Width), Math.Min(UI.S(660), wa.Height));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            host.Dock = DockStyle.Fill; host.BackColor = UI.Bg;
            Controls.Add(host);
            Controls.Add(BuildSidebar());
            FormClosing += (s, e) => { CleanerService.CancelAnalysis(); HangMonitor.Stop(); };
            if (Store.Get("monitor.auto") == "1") HangMonitor.Start();
            Go("resumen");
        }

        // Ctrl+Alt+P desde cualquier ventana marca una traba en el monitor
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        public static bool HotkeyOk;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HotkeyOk = RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (uint)Keys.P);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, 1);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1)
            {
                if (HangMonitor.Running) TrabasPage.RaiseMarked(HangMonitor.Mark());
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate(); Go("trabas");
                return;
            }
            base.WndProc(ref m);
        }

        Control BuildSidebar()
        {
            var side = new DBPanel { Dock = DockStyle.Left, Width = UI.S(236), BackColor = UI.SideBg };
            var nav = new DBPanel { Dock = DockStyle.Fill, BackColor = UI.SideBg, Padding = UI.P(0, 4, 0, 0) };
            var items = new[] {
                new[] { "resumen", "Resumen", Gl(0xE80F) }, new[] { "limpieza", "Limpieza", Gl(0xE74D) }, new[] { "espacio", "Espacio en disco", Gl(0xEDA2) },
                new[] { "inicio", "Programas de inicio", Gl(0xE7E8) }, new[] { "procesos", "Procesos", Gl(0xE9D9) }, new[] { "trabas", "Monitor de trabas", Gl(0xE916) },
                new[] { "optimizar", "Optimizar", Gl(0xE945) }, new[] { "reparar", "Reparar", Gl(0xE90F) } };
            for (int i = items.Length - 1; i >= 0; i--)
            {
                var b = new NavButton { Text = items[i][1], Glyph = items[i][2], Dock = DockStyle.Top, Tag = items[i][0] };
                b.Click += (s, e) => Go((string)((Control)s).Tag);
                nav.Controls.Add(b); navs[items[i][0]] = b;
            }
            var brand = new DBPanel { Dock = DockStyle.Top, Height = UI.S(92), BackColor = UI.SideBg };
            brand.Paint += PaintBrand;
            side.Controls.Add(nav); side.Controls.Add(BuildBottom()); side.Controls.Add(brand);
            return side;
        }

        Control BuildBottom()
        {
            var p = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(104), BackColor = UI.SideBg, Padding = UI.P(22, 8, 12, 8) };
            var admin = UI.Lbl(Program.IsAdmin ? "Administrador activo" : "Sin permisos de administrador", UI.Small, Program.IsAdmin ? UI.Good : ColorTranslator.FromHtml("#F5B754"));
            admin.Location = new Point(UI.S(22), UI.S(8));
            p.Controls.Add(admin);
            int y = UI.S(32);
            if (!Program.IsAdmin)
            {
                var re = new LinkLabel { Text = "Reiniciar como administrador", Font = UI.Small, AutoSize = true, LinkColor = ColorTranslator.FromHtml("#8DB4FF"), ActiveLinkColor = Color.White, BackColor = Color.Transparent, Location = new Point(UI.S(22), y) };
                re.LinkClicked += (s, e) => { if (Program.RestartAsAdmin()) Close(); };
                p.Controls.Add(re); y += UI.S(22);
            }
            var log = new LinkLabel { Text = "Ver registro de cambios", Font = UI.Small, AutoSize = true, LinkColor = ColorTranslator.FromHtml("#8DB4FF"), ActiveLinkColor = Color.White, BackColor = Color.Transparent, Location = new Point(UI.S(22), y) };
            log.LinkClicked += (s, e) =>
            {
                try { if (!System.IO.File.Exists(Log.FilePath)) Log.Write("Registro iniciado"); Process.Start("notepad.exe", Q.Arg(Log.FilePath)); } catch { }
            };
            p.Controls.Add(log);
            var ver = UI.Lbl("PulsoPC 1.1", UI.Small, ColorTranslator.FromHtml("#64748B")); ver.Location = new Point(UI.S(22), UI.S(80)); p.Controls.Add(ver);
            return p;
        }

        void PaintBrand(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(UI.S(22), UI.S(28), UI.S(38), UI.S(38));
            using (var path = UI.Round(r, UI.S(10)))
            using (var br = new LinearGradientBrush(r, UI.Accent, ColorTranslator.FromHtml("#06B6D4"), 45f)) g.FillPath(br, path);
            using (var pen = new Pen(Color.White, Math.Max(2f, UI.S(3))) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                g.DrawLines(pen, new[] { new PointF(x + w * 0.14f, y + h * 0.54f), new PointF(x + w * 0.34f, y + h * 0.54f), new PointF(x + w * 0.43f, y + h * 0.28f),
                    new PointF(x + w * 0.55f, y + h * 0.76f), new PointF(x + w * 0.64f, y + h * 0.5f), new PointF(x + w * 0.86f, y + h * 0.5f) });
            }
            TextRenderer.DrawText(g, "PulsoPC", UI.H2, new Point(UI.S(70), UI.S(26)), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Mantenimiento de Windows", UI.Small, new Point(UI.S(71), UI.S(50)), UI.SideText, TextFormatFlags.NoPadding);
        }

        PageBase Create(string key)
        {
            switch (key)
            {
                case "resumen": return new ResumenPage();
                case "limpieza": return new LimpiezaPage();
                case "espacio": return new EspacioPage();
                case "inicio": return new InicioPage();
                case "procesos": return new ProcesosPage();
                case "trabas": return new TrabasPage();
                case "optimizar": return new OptimizarPage();
                case "reparar": return new ReparaPage();
            }
            return null;
        }

        public void Go(string key)
        {
            if (key == "eventos") { using (var f = new EventsForm()) f.ShowDialog(this); return; }
            PageBase page;
            if (!pages.TryGetValue(key, out page))
            {
                page = Create(key);
                if (page == null) return;
                pages[key] = page; host.Controls.Add(page);
            }
            if (cur != null && cur != page) { cur.OnHidden(); cur.Visible = false; }
            cur = page; page.Visible = true; page.BringToFront();
            foreach (var kv in navs) kv.Value.Active = kv.Key == key;
            page.Activate();
        }
    }
}
