using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PulsoPC
{
    partial class LimpiezaPage : PageBase
    {
        readonly SortableList lv = new SortableList();
        readonly Dictionary<CleanCat, ListViewItem> items = new Dictionary<CleanCat, ListViewItem>();
        Button btnAn, btnClean, btnCancel;
        Label lblTotal, lblStatus; Bar pb; DBPanel progress;
        CancellationTokenSource cts; bool busy, loading, muteCheck;
        Label lblHiber, lblPage, lblOld, lblDown; Button btnHiber, btnOld, btnDown;

        public LimpiezaPage() : base("Limpieza de archivos", "Libera espacio borrando temporales y cachés que Windows y tus programas vuelven a crear solos.")
        {
            btnAn = UI.Btn("Analizar de nuevo"); btnClean = UI.Btn("Limpiar lo seleccionado", BtnKind.Primary);
            btnAn.Click += (s, e) => Analyze(true); btnClean.Click += (s, e) => DoClean();
            Actions.Controls.Add(btnAn); Actions.Controls.Add(btnClean);

            var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            lv.Dock = DockStyle.Fill; lv.CheckBoxes = true; lv.ShowGroups = true; lv.MultiSelect = false;
            lv.Columns.Add("Qué se limpia", UI.S(330)); lv.Columns.Add("Tamaño", UI.S(100), HorizontalAlignment.Right); lv.Columns.Add("Archivos", UI.S(90), HorizontalAlignment.Right); lv.Columns.Add("Detalle", UI.S(520));
            foreach (var g in new[] { "Windows", "Navegadores", "Aplicaciones", "Desarrollo" }) lv.Groups.Add(new ListViewGroup(g, g));
            lv.ItemChecked += (s, e) => { if (!muteCheck) UpdateTotals(); };
            listCard.Controls.Add(lv);

            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(44), BackColor = UI.Bg };
            var bAll = UI.Btn("Marcar todo"); bAll.Height = UI.S(30); bAll.Location = new Point(0, UI.S(2));
            var bRec = UI.Btn("Solo recomendado"); bRec.Height = UI.S(30); bRec.Location = new Point(bAll.Width + UI.S(8), UI.S(2));
            var bNone = UI.Btn("Desmarcar"); bNone.Height = UI.S(30); bNone.Location = new Point(bAll.Width + bRec.Width + UI.S(16), UI.S(2));
            bAll.Click += (s, e) => SetChecks(c => true); bNone.Click += (s, e) => SetChecks(c => false); bRec.Click += (s, e) => SetChecks(c => c.DefaultOn);
            lblTotal = UI.Lbl("", UI.BodyBold); lblTotal.Anchor = AnchorStyles.Top | AnchorStyles.Right; lblTotal.Location = new Point(UI.S(500), UI.S(8));
            top.Controls.Add(bAll); top.Controls.Add(bRec); top.Controls.Add(bNone); top.Controls.Add(lblTotal);
            top.Resize += (s, e) => lblTotal.Location = new Point(top.Width - lblTotal.Width, UI.S(8));

            progress = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(46), BackColor = UI.Bg, Visible = false, Padding = UI.P(0, 8, 0, 0) };
            lblStatus = UI.Lbl("", UI.Body, UI.Mute); lblStatus.Location = new Point(0, UI.S(10));
            pb = new Bar { Width = UI.S(360), Height = UI.S(8), Fixed = UI.Accent, Location = new Point(UI.S(420), UI.S(17)), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnCancel = UI.Btn("Cancelar", BtnKind.Danger); btnCancel.Height = UI.S(30); btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right; btnCancel.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            progress.Controls.Add(lblStatus); progress.Controls.Add(pb); progress.Controls.Add(btnCancel);
            progress.Resize += (s, e) => { btnCancel.Location = new Point(progress.Width - btnCancel.Width, UI.S(6)); pb.Location = new Point(progress.Width - btnCancel.Width - pb.Width - UI.S(12), UI.S(17)); };

            Body.Controls.Add(listCard);
            Body.Controls.Add(BuildTools());
            Body.Controls.Add(new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(10), BackColor = UI.Bg });
            Body.Controls.Add(progress);
            Body.Controls.Add(top);
            CleanerService.CategoryDone += c => Ui(() => UpdateItem(c, true));
        }

        protected override void OnFirstShow() { Analyze(false); }
    }
}
