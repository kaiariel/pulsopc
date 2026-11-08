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
    partial class FolderView : UserControl
    {
        readonly SortableList lv = new SortableList();
        TextBox txtPath; Button btnUp, btnOpen, btnDel; Label lblInfo;
        CancellationTokenSource cts; string current; bool started;

        public FolderView()
        {
            BackColor = UI.Bg; DoubleBuffered = true;
            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.Bg };
            btnUp = UI.Btn("Subir"); btnUp.Location = new Point(0, UI.S(6)); btnUp.Click += (s, e) => GoUp();
            var bRef = UI.Btn("Actualizar"); bRef.Location = new Point(btnUp.Width + UI.S(8), UI.S(6)); bRef.Click += (s, e) => Navigate(current);
            var bPick = UI.Btn("Elegir carpeta..."); bPick.Location = new Point(btnUp.Width + bRef.Width + UI.S(16), UI.S(6));
            bPick.Click += (s, e) => { using (var f = new FolderBrowserDialog()) { if (f.ShowDialog(FindForm()) == DialogResult.OK) Navigate(f.SelectedPath); } };
            txtPath = new TextBox { ReadOnly = true, Font = UI.Body, BackColor = Color.White, Location = new Point(btnUp.Width + bRef.Width + bPick.Width + UI.S(28), UI.S(10)), Width = UI.S(400), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            top.Controls.Add(btnUp); top.Controls.Add(bRef); top.Controls.Add(bPick); top.Controls.Add(txtPath);
            top.Resize += (s, e) => txtPath.Width = Math.Max(UI.S(150), top.Width - txtPath.Left);

            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Nombre", UI.S(380)); lv.Columns.Add("Tamaño", UI.S(110), HorizontalAlignment.Right); lv.Columns.Add("Del total", UI.S(90), HorizontalAlignment.Right);
            lv.Columns.Add("Tipo", UI.S(100)); lv.Columns.Add("Modificado", UI.S(160));
            lv.SetSort(1, false); lv.MultiSelect = true;
            lv.DoubleClick += (s, e) => { if (lv.SelectedItems.Count == 1 && lv.SelectedItems[0].Tag is DirectoryInfo) Navigate(((DirectoryInfo)lv.SelectedItems[0].Tag).FullName); };
            lv.SelectedIndexChanged += (s, e) => UpdateButtons();
            card.Controls.Add(lv);

            var bar = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(54), BackColor = UI.Bg };
            btnOpen = UI.Btn("Abrir ubicación"); btnOpen.Location = new Point(0, UI.S(12));
            btnOpen.Click += (s, e) => { var fs = Sel().FirstOrDefault(); if (fs != null) ShellOpen.Location(fs.FullName); };
            btnDel = UI.Btn("Enviar a la papelera", BtnKind.Danger); btnDel.Location = new Point(btnOpen.Width + UI.S(8), UI.S(12)); btnDel.Click += (s, e) => DeleteSel();
            lblInfo = UI.Lbl("Doble clic en una carpeta para entrar.", UI.Body, UI.Mute); lblInfo.Location = new Point(btnOpen.Width + btnDel.Width + UI.S(28), UI.S(20));
            bar.Controls.Add(btnOpen); bar.Controls.Add(btnDel); bar.Controls.Add(lblInfo);
            Controls.Add(card); Controls.Add(bar); Controls.Add(top);
            VisibleChanged += (s, e) => { if (Visible && !started) { started = true; Navigate(null); } };
            UpdateButtons();
        }

        IEnumerable<FileSystemInfo> Sel() { return lv.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<FileSystemInfo>(); }
        void UpdateButtons() { bool any = Sel().Any(); btnOpen.Enabled = any; btnDel.Enabled = any; btnUp.Enabled = current != null; }
        void Ui(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        void GoUp()
        {
            if (current == null) return;
            var p = Directory.GetParent(current);
            Navigate(p == null ? null : p.FullName);
        }
    }
}
