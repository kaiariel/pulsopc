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
    partial class NodeModulesView : UserControl
    {
        readonly SortableList lv = new SortableList();
        Button btnGo, btnStop, btnDel; Label lblInfo, lblRoot;
        CancellationTokenSource cts; bool busy; string rootPath = Env.Profile;

        public NodeModulesView()
        {
            BackColor = UI.Bg; DoubleBuffered = true;
            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.Bg };
            var l1 = UI.Lbl("Buscar en:", UI.Body, UI.Mute); l1.Location = new Point(0, UI.S(12));
            lblRoot = UI.Lbl(rootPath, UI.BodyBold); lblRoot.Location = new Point(UI.S(76), UI.S(12));
            var bPick = UI.Btn("Elegir carpeta..."); bPick.Location = new Point(UI.S(520), UI.S(6));
            bPick.Click += (s, e) => { using (var f = new FolderBrowserDialog { Description = "Carpeta donde buscar proyectos" }) { if (f.ShowDialog(FindForm()) == DialogResult.OK) { rootPath = f.SelectedPath; lblRoot.Text = rootPath; } } };
            btnGo = UI.Btn("Buscar", BtnKind.Primary); btnGo.Location = new Point(UI.S(520) + bPick.Width + UI.S(8), UI.S(6)); btnGo.Click += (s, e) => Search();
            btnStop = UI.Btn("Detener", BtnKind.Danger); btnStop.Location = btnGo.Location; btnStop.Visible = false; btnStop.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            top.Controls.Add(l1); top.Controls.Add(lblRoot); top.Controls.Add(bPick); top.Controls.Add(btnGo); top.Controls.Add(btnStop);

            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            lv.Dock = DockStyle.Fill; lv.CheckBoxes = true;
            lv.Columns.Add("Proyecto", UI.S(240)); lv.Columns.Add("node_modules", UI.S(110), HorizontalAlignment.Right); lv.Columns.Add("Sin tocar", UI.S(120)); lv.Columns.Add("Ubicación", UI.S(500));
            lv.SetSort(1, false); lv.ItemChecked += (s, e) => UpdateInfo();
            card.Controls.Add(lv);

            var bar = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(54), BackColor = UI.Bg };
            var bOld = UI.Btn("Marcar los de más de 90 días"); bOld.Location = new Point(0, UI.S(12));
            bOld.Click += (s, e) => { foreach (ListViewItem it in lv.Items) it.Checked = it.SubItems[2].Tag is DateTime && (DateTime.Now - (DateTime)it.SubItems[2].Tag).TotalDays > 90; };
            btnDel = UI.Btn("Eliminar los marcados", BtnKind.Danger); btnDel.Location = new Point(bOld.Width + UI.S(8), UI.S(12)); btnDel.Click += (s, e) => DeleteChecked();
            lblInfo = UI.Lbl("Busca carpetas node_modules de proyectos que ya no usas. Se regeneran con npm install.", UI.Body, UI.Mute); lblInfo.Location = new Point(bOld.Width + btnDel.Width + UI.S(28), UI.S(20));
            bar.Controls.Add(bOld); bar.Controls.Add(btnDel); bar.Controls.Add(lblInfo);
            Controls.Add(card); Controls.Add(bar); Controls.Add(top);
        }

        void Ui(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        void UpdateInfo()
        {
            var chk = lv.CheckedItems.Cast<ListViewItem>().ToList();
            long sum = chk.Sum(i => i.SubItems[1].Tag is long ? (long)i.SubItems[1].Tag : 0);
            btnDel.Enabled = chk.Count > 0 && !busy;
            if (!busy && lv.Items.Count > 0) lblInfo.Text = chk.Count + " marcados  ·  " + Fmt.Bytes(sum) + " por liberar";
        }

        static bool SkipDir(DirectoryInfo d)
        {
            string n = d.Name;
            if (n.StartsWith(".") || n.Equals("AppData", StringComparison.OrdinalIgnoreCase) || n.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.Equals("Windows", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Program Files", StringComparison.OrdinalIgnoreCase) || n.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static DateTime LastActivity(string projectDir)
        {
            DateTime best = DateTime.MinValue;
            try
            {
                foreach (var e in new DirectoryInfo(projectDir).EnumerateFileSystemInfos())
                    if (!e.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) && e.LastWriteTime > best) best = e.LastWriteTime;
            }
            catch { }
            return best;
        }
    }
}
