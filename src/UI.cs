using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PulsoPC
{
    enum BtnKind { Secondary, Primary, Danger }

    static class UI
    {
        public static readonly float Scale;
        static UI()
        {
            float s = 1f;
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f; } catch { }
            Scale = s < 1f ? 1f : s;
        }
        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        public static readonly Color Bg = ColorTranslator.FromHtml("#F2F4F8");
        public static readonly Color Line = ColorTranslator.FromHtml("#E1E6EF");
        public static readonly Color Ink = ColorTranslator.FromHtml("#172033");
        public static readonly Color Mute = ColorTranslator.FromHtml("#667085");
        public static readonly Color Accent = ColorTranslator.FromHtml("#2563EB");
        public static readonly Color AccentDk = ColorTranslator.FromHtml("#1D4ED8");
        public static readonly Color AccentLt = ColorTranslator.FromHtml("#E8EFFE");
        public static readonly Color Good = ColorTranslator.FromHtml("#16A34A");
        public static readonly Color GoodLt = ColorTranslator.FromHtml("#E6F6EC");
        public static readonly Color Warn = ColorTranslator.FromHtml("#D97706");
        public static readonly Color WarnLt = ColorTranslator.FromHtml("#FDF1DD");
        public static readonly Color Bad = ColorTranslator.FromHtml("#DC2626");
        public static readonly Color BadLt = ColorTranslator.FromHtml("#FCE8E8");
        public static readonly Color SideBg = ColorTranslator.FromHtml("#0F172A");
        public static readonly Color SideHover = ColorTranslator.FromHtml("#1B2640");
        public static readonly Color SideText = ColorTranslator.FromHtml("#B6C0D4");

        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.5f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 19f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 23f);
        public static readonly Font Icons = new Font("Segoe MDL2 Assets", 13f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 0f || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Label Lbl(string text, Font f = null, Color? c = null)
        {
            return new Label { Text = text, Font = f ?? Body, ForeColor = c ?? Ink, AutoSize = true, BackColor = Color.Transparent };
        }

        public static Color LevelColor(Lvl l)
        {
            switch (l) { case Lvl.Bad: return Bad; case Lvl.Warn: return Warn; case Lvl.Info: return Accent; default: return Good; }
        }

        public static Button Btn(string text, BtnKind kind = BtnKind.Secondary, int minWidth = 0)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, Font = Body, Cursor = Cursors.Hand, UseVisualStyleBackColor = false, Height = S(34), TextAlign = ContentAlignment.MiddleCenter };
            b.Margin = new Padding(0, 0, S(8), 0);
            int w = TextRenderer.MeasureText(text, Body).Width + S(30);
            b.Width = Math.Max(w, minWidth);
            b.FlatAppearance.BorderSize = 1;
            Style(b, kind);
            return b;
        }

        public static void Style(Button b, BtnKind kind)
        {
            switch (kind)
            {
                case BtnKind.Primary:
                    b.BackColor = Accent; b.ForeColor = Color.White;
                    b.FlatAppearance.BorderColor = Accent; b.FlatAppearance.MouseOverBackColor = AccentDk; b.FlatAppearance.MouseDownBackColor = AccentDk;
                    break;
                case BtnKind.Danger:
                    b.BackColor = Color.White; b.ForeColor = Bad;
                    b.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#F2B8B8"); b.FlatAppearance.MouseOverBackColor = BadLt; b.FlatAppearance.MouseDownBackColor = BadLt;
                    break;
                default:
                    b.BackColor = Color.White; b.ForeColor = Ink;
                    b.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#CBD3E1"); b.FlatAppearance.MouseOverBackColor = ColorTranslator.FromHtml("#EEF2F9"); b.FlatAppearance.MouseDownBackColor = ColorTranslator.FromHtml("#E3E9F4");
                    break;
            }
        }

        public static void Toast(Control owner, string text)
        {
            MessageBox.Show(owner == null ? null : owner.FindForm(), text, "PulsoPC", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        public static bool Confirm(Control owner, string text, string title = "Confirmar", bool warning = true)
        {
            return MessageBox.Show(owner == null ? null : owner.FindForm(), text, title, MessageBoxButtons.YesNo, warning ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }

    class DBPanel : Panel { public DBPanel() { DoubleBuffered = true; } }
    class DBFlow : FlowLayoutPanel { public DBFlow() { DoubleBuffered = true; } }
    class DBTable : TableLayoutPanel { public DBTable() { DoubleBuffered = true; } }

    class Card : Panel
    {
        public int Radius = 10;
        public Card() { DoubleBuffered = true; BackColor = Color.White; Padding = UI.P(16, 14, 16, 14); ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UI.Round(new RectangleF(0.5f, 0.5f, Width - 1.6f, Height - 1.6f), UI.S(Radius)))
            using (var pen = new Pen(UI.Line)) g.DrawPath(pen, path);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0) return;
            using (var path = UI.Round(new RectangleF(0, 0, Width, Height), UI.S(Radius)))
            {
                var old = Region; Region = new Region(path); if (old != null) old.Dispose();
            }
        }
    }

    class Bar : Control
    {
        double v; public Color? Fixed; public bool Invert;
        public Bar() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Height = UI.S(8); }
        public double Value { get { return v; } set { v = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = UI.Round(r, r.Height / 2f)) using (var br = new SolidBrush(ColorTranslator.FromHtml("#E7EBF2"))) g.FillPath(br, path);
            float w = (float)(r.Width * v / 100.0);
            if (w < r.Height) w = v > 0 ? r.Height : 0;
            if (w > 0)
            {
                double eff = Invert ? 100 - v : v;
                Color c = Fixed ?? (eff >= 90 ? UI.Bad : eff >= 75 ? UI.Warn : UI.Accent);
                using (var path = UI.Round(new RectangleF(0, 0, w, r.Height), r.Height / 2f)) using (var br = new SolidBrush(c)) g.FillPath(br, path);
            }
        }
    }

    class NavButton : Control
    {
        public string Glyph = ""; bool hover; bool active;
        public NavButton() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; Height = UI.S(46); }
        public bool Active { get { return active; } set { active = value; Invalidate(); } }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UI.SideBg);
            var r = new Rectangle(UI.S(12), UI.S(3), Width - UI.S(24), Height - UI.S(6));
            if (active || hover)
            {
                using (var path = UI.Round(r, UI.S(8))) using (var br = new SolidBrush(active ? UI.Accent : UI.SideHover)) g.FillPath(br, path);
            }
            Color fg = (active || hover) ? Color.White : UI.SideText;
            TextRenderer.DrawText(g, Glyph, UI.Icons, new Rectangle(r.X + UI.S(10), r.Y, UI.S(30), r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, active ? UI.BodyBold : UI.Body, new Rectangle(r.X + UI.S(48), r.Y, r.Width - UI.S(52), r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // Lista con cabeceras ordenables y filas mas altas
    class SortableList : ListView
    {
        public int SortCol = -1; public bool Asc = true;
        public SortableList()
        {
            View = View.Details; FullRowSelect = true; HideSelection = false; BorderStyle = BorderStyle.None;
            Font = UI.Body; DoubleBuffered = true; UseCompatibleStateImageBehavior = false; ShowItemToolTips = true;
            BackColor = Color.White; MultiSelect = true;
            SmallImageList = new ImageList { ImageSize = new Size(1, UI.S(28)) };
            ListViewItemSorter = new Cmp(this);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { Native.SetWindowTheme(Handle, "explorer", null); } catch { } FitLast(); }
        protected override void OnColumnClick(ColumnClickEventArgs e)
        {
            base.OnColumnClick(e);
            if (SortCol == e.Column) Asc = !Asc;
            else
            {
                SortCol = e.Column;
                bool numeric = Items.Count > 0 && Items[0].SubItems.Count > e.Column && Items[0].SubItems[e.Column].Tag != null && !(Items[0].SubItems[e.Column].Tag is string);
                Asc = !numeric;
            }
            Sort();
        }
        public void SetSort(int col, bool asc) { SortCol = col; Asc = asc; Sort(); }

        // La ultima columna ocupa el ancho sobrante (evita la barra horizontal)
        public void FitLast()
        {
            if (Columns.Count == 0 || View != View.Details || ClientSize.Width < 50) return;
            int used = 0; for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
            Columns[Columns.Count - 1].Width = Math.Max(UI.S(160), ClientSize.Width - used - SystemInformation.VerticalScrollBarWidth - UI.S(2));
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FitLast(); }

        class Cmp : IComparer
        {
            readonly SortableList o;
            public Cmp(SortableList owner) { o = owner; }
            public int Compare(object a, object b)
            {
                if (o.SortCol < 0) return 0;
                var x = (ListViewItem)a; var y = (ListViewItem)b; int c = o.SortCol;
                object ta = x.SubItems.Count > c ? x.SubItems[c].Tag : null;
                object tb = y.SubItems.Count > c ? y.SubItems[c].Tag : null;
                int r;
                if (ta is IComparable && tb is IComparable && ta.GetType() == tb.GetType()) r = ((IComparable)ta).CompareTo(tb);
                else
                {
                    string sa = x.SubItems.Count > c ? x.SubItems[c].Text : "";
                    string sb = y.SubItems.Count > c ? y.SubItems[c].Text : "";
                    r = string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
                }
                return o.Asc ? r : -r;
            }
        }
    }

    abstract class PageBase : UserControl
    {
        protected readonly DBPanel HeaderPanel = new DBPanel();
        protected readonly DBPanel Body = new DBPanel();
        protected readonly DBFlow Actions = new DBFlow();
        protected readonly Label TitleLbl;
        protected readonly Label SubLbl;
        bool shownOnce;

        protected PageBase(string title, string sub)
        {
            Dock = DockStyle.Fill; BackColor = UI.Bg; DoubleBuffered = true; Font = UI.Body;
            Body.Dock = DockStyle.Fill; Body.Padding = UI.P(24, 4, 24, 20); Body.BackColor = UI.Bg;
            HeaderPanel.Dock = DockStyle.Top; HeaderPanel.Height = UI.S(92); HeaderPanel.BackColor = UI.Bg; HeaderPanel.Padding = UI.P(0, 22, 24, 0);
            TitleLbl = UI.Lbl(title, UI.H1); TitleLbl.Location = new Point(UI.S(24), UI.S(14));
            SubLbl = UI.Lbl(sub, UI.Body, UI.Mute); SubLbl.Location = new Point(UI.S(26), UI.S(54)); SubLbl.MaximumSize = new Size(UI.S(720), 0);
            Actions.Dock = DockStyle.Right; Actions.AutoSize = true; Actions.WrapContents = false; Actions.FlowDirection = FlowDirection.LeftToRight; Actions.BackColor = UI.Bg;
            HeaderPanel.Controls.Add(Actions);
            HeaderPanel.Controls.Add(TitleLbl); HeaderPanel.Controls.Add(SubLbl);
            Controls.Add(Body); Controls.Add(HeaderPanel);
        }

        public void Activate()
        {
            if (!shownOnce) { shownOnce = true; OnFirstShow(); }
            OnShown();
        }
        protected virtual void OnFirstShow() { }
        protected virtual void OnShown() { }
        public virtual void OnHidden() { }

        // Ejecuta una accion en el hilo de la interfaz si la pagina sigue viva
        protected void Ui(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
        }
    }
}

namespace PulsoPC
{
    static class TableExt
    {
        // Filas con altura automatica y una fila vacia al final que absorbe el espacio sobrante
        public static void FinishRows(this System.Windows.Forms.TableLayoutPanel t)
        {
            int n = t.Controls.Count;
            t.RowStyles.Clear();
            for (int i = 0; i < n; i++) t.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            t.RowCount = n + 1;
            t.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
        }
    }
}
