using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PulsoPC
{
    class TweakRow : Control
    {
        public readonly Tweak T; public readonly Button Btn; public bool Busy;

        public TweakRow(Tweak t)
        {
            T = t;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = UI.S(92); Margin = new Padding(0, 0, 0, UI.S(8));
            Btn = UI.Btn("Aplicar", BtnKind.Primary, UI.S(104)); Btn.Margin = new Padding(0);
            Controls.Add(Btn); RefreshState();
        }

        public void RefreshState()
        {
            Btn.Text = Busy ? "Aplicando..." : T.Applied ? "Revertir" : "Aplicar";
            UI.Style(Btn, T.Applied ? BtnKind.Secondary : BtnKind.Primary);
            Btn.Enabled = !Busy; Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Btn == null) return;
            Btn.Location = new Point(Width - Btn.Width - UI.S(16), (Height - Btn.Height) / 2);
        }

        void Pill(Graphics g, string text, Color fg, Color bg, int x, int y)
        {
            var sz = TextRenderer.MeasureText(text, UI.SmallBold);
            var r = new RectangleF(x, y, sz.Width + UI.S(14), UI.S(22));
            using (var path = UI.Round(r, r.Height / 2)) using (var br = new SolidBrush(bg)) g.FillPath(br, path);
            TextRenderer.DrawText(g, text, UI.SmallBold, new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UI.Bg);
            using (var path = UI.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), UI.S(10)))
            {
                using (var br = new SolidBrush(Color.White)) g.FillPath(br, path);
                using (var pen = new Pen(UI.Line)) g.DrawPath(pen, path);
            }
            int pillW = UI.S(96);
            int right = Btn.Left - pillW - UI.S(28);
            var tsz = TextRenderer.MeasureText(T.Title, UI.BodyBold);
            TextRenderer.DrawText(g, T.Title, UI.BodyBold, new Rectangle(UI.S(18), UI.S(10), Math.Min(tsz.Width + 4, right - UI.S(110)), UI.S(22)), UI.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            int cx = UI.S(18) + Math.Min(tsz.Width, right - UI.S(110)) + UI.S(10);
            if (T.Risk != "Seguro") Pill(g, T.Risk, UI.Warn, UI.WarnLt, cx, UI.S(11));
            else if (T.Recommended) Pill(g, "Recomendado", UI.Good, UI.GoodLt, cx, UI.S(11));
            string desc = T.Desc + (T.Note.Length > 0 ? "  " + T.Note : "");
            TextRenderer.DrawText(g, desc, UI.Body, new Rectangle(UI.S(18), UI.S(36), right - UI.S(18), UI.S(46)), ColorTranslator.FromHtml("#47546B"), TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (T.Applied) Pill(g, "Aplicado", UI.Good, UI.GoodLt, Btn.Left - pillW + UI.S(14), (Height - UI.S(22)) / 2);
            else Pill(g, "Sin aplicar", UI.Mute, ColorTranslator.FromHtml("#EEF1F6"), Btn.Left - pillW + UI.S(6), (Height - UI.S(22)) / 2);
        }
    }
}
