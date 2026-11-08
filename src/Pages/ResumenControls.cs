using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PulsoPC
{
    class MetricCard : Card
    {
        readonly Label title, value, sub; readonly Bar bar;

        public MetricCard(string t)
        {
            Padding = UI.P(16, 12, 16, 10);
            title = UI.Lbl(t.ToUpperInvariant(), UI.SmallBold, UI.Mute); title.AutoSize = false; title.Dock = DockStyle.Top; title.Height = UI.S(20);
            value = UI.Lbl("...", UI.Big); value.AutoSize = false; value.Dock = DockStyle.Top; value.Height = UI.S(46);
            var wrap = new DBPanel { Dock = DockStyle.Top, Height = UI.S(18), Padding = UI.P(0, 5, 0, 5), BackColor = Color.White };
            bar = new Bar { Dock = DockStyle.Fill }; wrap.Controls.Add(bar);
            sub = UI.Lbl("", UI.Small, UI.Mute); sub.AutoSize = false; sub.Dock = DockStyle.Fill;
            Controls.Add(sub); Controls.Add(wrap); Controls.Add(value); Controls.Add(title);
        }

        public void Set(string val, double pct, string subText, bool invert = false)
        {
            value.Text = val; bar.Invert = invert; bar.Value = pct; sub.Text = subText;
        }
    }

    class ScoreRing : Control
    {
        int score = 100; bool busy = true;
        public ScoreRing() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        public void SetScore(int s) { score = s; busy = false; Invalidate(); }
        public void SetBusy() { busy = true; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            int side = Math.Min(Width, Height) - UI.S(8);
            var r = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
            float pw = UI.S(11);
            r.Inflate(-(int)(pw / 2), -(int)(pw / 2));
            using (var pen = new Pen(ColorTranslator.FromHtml("#E7EBF2"), pw)) g.DrawEllipse(pen, r);
            Color c = busy ? UI.Line : score >= 80 ? UI.Good : score >= 55 ? UI.Warn : UI.Bad;
            if (!busy) using (var pen = new Pen(c, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, r, -90, 360f * score / 100f);
            string txt = busy ? "..." : score.ToString();
            var f = UI.Big;
            var sz = TextRenderer.MeasureText(txt, f);
            TextRenderer.DrawText(g, txt, f, new Point((Width - sz.Width) / 2, (Height - sz.Height) / 2 - UI.S(6)), busy ? UI.Mute : c, TextFormatFlags.NoPadding);
            var sm = TextRenderer.MeasureText("de 100", UI.Small);
            TextRenderer.DrawText(g, "de 100", UI.Small, new Point((Width - sm.Width) / 2, (Height + sz.Height) / 2 - UI.S(10)), UI.Mute, TextFormatFlags.NoPadding);
        }
    }

    class FindingRow : Control
    {
        public readonly Finding F; readonly Button btn;
        public event Action<Finding> ActionClick;

        public FindingRow(Finding f)
        {
            F = f;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = UI.S(f.Level == Lvl.Ok ? 40 : 78); Margin = new Padding(0, 0, 0, UI.S(8));
            if (!string.IsNullOrEmpty(f.Page) || f.Fix != null)
            {
                btn = UI.Btn(f.ActionText ?? "Abrir", f.Level == Lvl.Bad ? BtnKind.Primary : BtnKind.Secondary, UI.S(110));
                btn.Margin = new Padding(0);
                btn.Click += (s, e) => { var h = ActionClick; if (h != null) h(F); };
                Controls.Add(btn);
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (btn != null) btn.Location = new Point(Width - btn.Width - UI.S(14), (Height - btn.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            Color bg = F.Level == Lvl.Bad ? UI.BadLt : F.Level == Lvl.Warn ? UI.WarnLt : F.Level == Lvl.Info ? UI.AccentLt : ColorTranslator.FromHtml("#F5F8FB");
            using (var path = UI.Round(new RectangleF(0, 0, Width - 1, Height - 1), UI.S(8))) using (var br = new SolidBrush(bg)) g.FillPath(br, path);
            Color dot = UI.LevelColor(F.Level);
            int dy = F.Level == Lvl.Ok ? (Height - UI.S(10)) / 2 : UI.S(16);
            using (var br = new SolidBrush(dot)) g.FillEllipse(br, UI.S(16), dy, UI.S(10), UI.S(10));
            int right = Width - UI.S(16) - (btn != null ? btn.Width + UI.S(12) : 0);
            int x = UI.S(38);
            if (F.Level == Lvl.Ok)
            {
                TextRenderer.DrawText(g, F.Title, UI.Body, new Rectangle(x, 0, right - x, Height), UI.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                return;
            }
            TextRenderer.DrawText(g, F.Title, UI.BodyBold, new Rectangle(x, UI.S(10), right - x, UI.S(22)), UI.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, F.Detail, UI.Body, new Rectangle(x, UI.S(33), right - x, UI.S(40)), ColorTranslator.FromHtml("#47546B"), TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
