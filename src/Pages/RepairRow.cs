using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PulsoPC
{
    class RepairRow : Control
    {
        public readonly RepairAction A; public readonly Button Btn;

        public RepairRow(RepairAction a)
        {
            A = a;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = UI.S(84); Margin = new Padding(0, 0, 0, UI.S(8));
            Btn = UI.Btn("Ejecutar", BtnKind.Secondary, UI.S(104)); Btn.Margin = new Padding(0);
            Controls.Add(Btn);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Btn == null) return;
            Btn.Location = new Point(Width - Btn.Width - UI.S(16), (Height - Btn.Height) / 2);
        }

        int Pill(Graphics g, string text, Color fg, Color bg, int x, int y)
        {
            var sz = TextRenderer.MeasureText(text, UI.SmallBold);
            var r = new RectangleF(x, y, sz.Width + UI.S(14), UI.S(22));
            using (var path = UI.Round(r, r.Height / 2)) using (var br = new SolidBrush(bg)) g.FillPath(br, path);
            TextRenderer.DrawText(g, text, UI.SmallBold, new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return (int)r.Width;
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
            int right = Btn.Left - UI.S(20);
            var tsz = TextRenderer.MeasureText(A.Title, UI.BodyBold);
            int tw = Math.Min(tsz.Width + 4, right - UI.S(200));
            TextRenderer.DrawText(g, A.Title, UI.BodyBold, new Rectangle(UI.S(18), UI.S(10), tw, UI.S(22)), UI.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            int cx = UI.S(18) + tw + UI.S(10);
            if (A.Tag.Length > 0) cx += Pill(g, A.Tag, UI.Mute, ColorTranslator.FromHtml("#EEF1F6"), cx, UI.S(11)) + UI.S(6);
            if (A.NeedsAdmin && !Program.IsAdmin) Pill(g, "Requiere administrador", UI.Warn, UI.WarnLt, cx, UI.S(11));
            TextRenderer.DrawText(g, A.Desc, UI.Body, new Rectangle(UI.S(18), UI.S(36), right - UI.S(18), UI.S(44)), ColorTranslator.FromHtml("#47546B"), TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
