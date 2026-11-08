using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class OptimizarPage : PageBase
    {
        DBTable list; CheckBox chkRestore; Label lblRam; Button btnRam, btnAll;
        readonly List<TweakRow> rows = new List<TweakRow>();
        bool restoreDone, busy;

        public OptimizarPage() : base("Optimizar Windows", "Ajustes que hacen el equipo más ágil. Cada uno se puede revertir desde aquí con un clic.")
        {
            btnAll = UI.Btn("Aplicar los recomendados", BtnKind.Primary); btnAll.Click += (s, e) => ApplyRecommended();
            Actions.Controls.Add(btnAll);

            var cards = new DBTable { Dock = DockStyle.Top, Height = UI.S(126), ColumnCount = 2, RowCount = 1, BackColor = UI.Bg };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var a = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, UI.S(6), 0), Padding = UI.P(18, 14, 18, 12) };
            chkRestore = new CheckBox { Text = "Crear un punto de restauración antes de aplicar", Checked = true, AutoSize = true, Font = UI.BodyBold, BackColor = Color.White, Location = new Point(UI.S(18), UI.S(14)) };
            var ta = UI.Lbl("Si algo no te convence, Windows puede volver al estado anterior desde Protección del sistema. Además, todos los cambios de esta página se revierten con un clic.", UI.Small, UI.Mute);
            ta.AutoSize = false; ta.Dock = DockStyle.Bottom; ta.Height = UI.S(52);
            a.Controls.Add(chkRestore); a.Controls.Add(ta);

            var b = new Card { Dock = DockStyle.Fill, Margin = new Padding(UI.S(6), 0, 0, 0), Padding = UI.P(18, 14, 18, 12) };
            var tb = UI.Lbl("Liberar memoria RAM ahora", UI.BodyBold); tb.Location = new Point(UI.S(18), UI.S(12));
            var db = UI.Lbl("Vacía la memoria en espera y recorta los procesos inactivos. Sirve cuando Windows tiene la RAM llena de caché.", UI.Small, UI.Mute);
            db.AutoSize = false; db.Location = new Point(UI.S(18), UI.S(38)); db.Size = new Size(UI.S(430), UI.S(36));
            btnRam = UI.Btn("Liberar memoria"); btnRam.Location = new Point(UI.S(18), UI.S(82)); btnRam.Height = UI.S(30); btnRam.Click += (s, e) => FreeRam();
            lblRam = UI.Lbl("", UI.BodyBold, UI.Good); lblRam.Location = new Point(UI.S(18) + btnRam.Width + UI.S(12), UI.S(88));
            b.Controls.Add(tb); b.Controls.Add(db); b.Controls.Add(btnRam); b.Controls.Add(lblRam);
            cards.Controls.Add(a, 0, 0); cards.Controls.Add(b, 1, 0);

            list = new DBTable { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = UI.Bg, Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 0) };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Body.Controls.Add(list);
            Body.Controls.Add(new DBPanel { Dock = DockStyle.Top, Height = UI.S(14), BackColor = UI.Bg });
            Body.Controls.Add(cards);
        }

        protected override void OnFirstShow()
        {
            foreach (var t in Tweaks.All)
            {
                var row = new TweakRow(t) { Dock = DockStyle.Fill };
                var r = row; row.Btn.Click += (s, e) => Toggle(r);
                rows.Add(row); list.Controls.Add(row);
            }
            list.FinishRows();
            RefreshStates();
        }

        async void RefreshStates()
        {
            await Task.Run(() => { foreach (var t in Tweaks.All) { try { t.Applied = t.IsApplied(); } catch { t.Applied = false; } } });
            if (IsDisposed) return;
            foreach (var r in rows) r.RefreshState();
        }
    }
}
