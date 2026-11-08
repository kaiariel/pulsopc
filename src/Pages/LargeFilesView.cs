using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class LargeFilesView : UserControl
    {
        readonly SortableList lv = new SortableList();
        ComboBox cmb; NumericUpDown num; Button btnGo, btnStop, btnOpen, btnDel, btnCopy; Label lblInfo;
        CancellationTokenSource cts; bool busy;
        const string Pick = "Elegir otra carpeta...";

        public LargeFilesView()
        {
            BackColor = UI.Bg; DoubleBuffered = true;
            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.Bg };
            var l1 = UI.Lbl("Buscar en:", UI.Body, UI.Mute); l1.Location = new Point(0, UI.S(12));
            cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UI.Body, Width = UI.S(260), Location = new Point(UI.S(76), UI.S(8)) };
            foreach (var d in EspacioPage.FixedDrives()) cmb.Items.Add(d);
            cmb.Items.Add(Pick); cmb.SelectedIndex = Math.Max(0, cmb.Items.IndexOf(Env.SysDrive));
            cmb.SelectedIndexChanged += (s, e) => { if ((string)cmb.SelectedItem == Pick) ChooseFolder(); };
            var l2 = UI.Lbl("Mayores de (MB):", UI.Body, UI.Mute); l2.Location = new Point(UI.S(356), UI.S(12));
            num = new NumericUpDown { Minimum = 10, Maximum = 200000, Value = 500, Increment = 100, Font = UI.Body, Width = UI.S(80), Location = new Point(UI.S(480), UI.S(8)) };
            btnGo = UI.Btn("Buscar", BtnKind.Primary); btnGo.Location = new Point(UI.S(580), UI.S(6)); btnGo.Click += (s, e) => Search();
            btnStop = UI.Btn("Detener", BtnKind.Danger); btnStop.Location = new Point(UI.S(580) + btnGo.Width + UI.S(8), UI.S(6)); btnStop.Visible = false; btnStop.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            top.Controls.Add(l1); top.Controls.Add(cmb); top.Controls.Add(l2); top.Controls.Add(num); top.Controls.Add(btnGo); top.Controls.Add(btnStop);

            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Tamaño", UI.S(100), HorizontalAlignment.Right); lv.Columns.Add("Archivo", UI.S(300)); lv.Columns.Add("Modificado", UI.S(120)); lv.Columns.Add("Tipo", UI.S(70)); lv.Columns.Add("Carpeta", UI.S(500));
            lv.SetSort(0, false); lv.SelectedIndexChanged += (s, e) => UpdateButtons();
            lv.DoubleClick += (s, e) => OpenSel();
            card.Controls.Add(lv);

            var bar = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(54), BackColor = UI.Bg };
            btnOpen = UI.Btn("Abrir ubicación"); btnOpen.Location = new Point(0, UI.S(12)); btnOpen.Click += (s, e) => OpenSel();
            btnCopy = UI.Btn("Copiar ruta"); btnCopy.Location = new Point(btnOpen.Width + UI.S(8), UI.S(12));
            btnCopy.Click += (s, e) => { var p = Paths(); if (p.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, p.ToArray())); };
            btnDel = UI.Btn("Enviar a la papelera", BtnKind.Danger); btnDel.Location = new Point(btnOpen.Width + btnCopy.Width + UI.S(16), UI.S(12)); btnDel.Click += (s, e) => DeleteSel();
            lblInfo = UI.Lbl("Elige dónde buscar y pulsa Buscar. Los archivos que solo están en la nube no se cuentan.", UI.Body, UI.Mute);
            lblInfo.Location = new Point(btnOpen.Width + btnCopy.Width + btnDel.Width + UI.S(36), UI.S(20));
            bar.Controls.Add(btnOpen); bar.Controls.Add(btnCopy); bar.Controls.Add(btnDel); bar.Controls.Add(lblInfo);

            Controls.Add(card); Controls.Add(bar); Controls.Add(top);
            UpdateButtons();
        }

        string root { get { return (string)cmb.SelectedItem; } }

        void ChooseFolder()
        {
            using (var f = new FolderBrowserDialog { Description = "Carpeta donde buscar archivos grandes" })
            {
                if (f.ShowDialog(FindForm()) == DialogResult.OK) { cmb.Items.Insert(cmb.Items.Count - 1, f.SelectedPath); cmb.SelectedIndex = cmb.Items.Count - 2; }
                else cmb.SelectedIndex = Math.Max(0, cmb.Items.IndexOf(Env.SysDrive));
            }
        }

        List<string> Paths() { return lv.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList(); }
        void UpdateButtons() { bool any = lv.SelectedItems.Count > 0; btnOpen.Enabled = any; btnCopy.Enabled = any; btnDel.Enabled = any && !busy; }
        void OpenSel() { var p = Paths().FirstOrDefault(); if (p != null) ShellOpen.Location(p); }
    }
}
