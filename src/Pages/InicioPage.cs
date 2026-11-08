using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    class InicioPage : PageBase
    {
        readonly SortableList lv = new SortableList();
        Button btnOff, btnOn, btnOpen, btnRefresh; Label lblInfo;

        public InicioPage() : base("Programas de inicio", "Lo que arranca junto con Windows. Deshabilitar un programa no lo desinstala: solo evita que se abra solo.")
        {
            btnRefresh = UI.Btn("Actualizar"); btnRefresh.Click += (s, e) => LoadItems();
            Actions.Controls.Add(btnRefresh);
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Programa", UI.S(230)); lv.Columns.Add("Estado", UI.S(120)); lv.Columns.Add("Editor", UI.S(190)); lv.Columns.Add("Recomendación", UI.S(270)); lv.Columns.Add("Origen", UI.S(220));
            lv.SelectedIndexChanged += (s, e) => UpdateButtons();
            card.Controls.Add(lv);

            var bar = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(54), BackColor = UI.Bg, Padding = UI.P(0, 12, 0, 0) };
            btnOff = UI.Btn("Deshabilitar", BtnKind.Primary); btnOn = UI.Btn("Habilitar"); btnOpen = UI.Btn("Abrir ubicación");
            btnOff.Location = new Point(0, UI.S(12)); btnOn.Location = new Point(btnOff.Width + UI.S(8), UI.S(12)); btnOpen.Location = new Point(btnOff.Width + btnOn.Width + UI.S(16), UI.S(12));
            btnOff.Click += (s, e) => Toggle(false); btnOn.Click += (s, e) => Toggle(true);
            btnOpen.Click += (s, e) => { var it = Selected().FirstOrDefault(); if (it != null) ShellOpen.Location(it.Target); };
            lblInfo = UI.Lbl("", UI.Body, UI.Mute); lblInfo.Location = new Point(btnOff.Width + btnOn.Width + btnOpen.Width + UI.S(34), UI.S(20));
            bar.Controls.Add(btnOff); bar.Controls.Add(btnOn); bar.Controls.Add(btnOpen); bar.Controls.Add(lblInfo);
            Body.Controls.Add(card); Body.Controls.Add(bar);
            UpdateButtons();
        }

        protected override void OnFirstShow() { LoadItems(); }

        IEnumerable<StartupItem> Selected() { return lv.SelectedItems.Cast<ListViewItem>().Select(i => (StartupItem)i.Tag); }

        void UpdateButtons()
        {
            var sel = Selected().ToList();
            btnOff.Enabled = sel.Any(i => i.Enabled); btnOn.Enabled = sel.Any(i => !i.Enabled); btnOpen.Enabled = sel.Count > 0;
        }

        async void LoadItems()
        {
            btnRefresh.Enabled = false; lblInfo.Text = "Leyendo...";
            var data = await Task.Run(() => Startup.List());
            if (IsDisposed) return;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var it in data)
            {
                var li = new ListViewItem(it.Name) { Tag = it };
                li.SubItems.Add(it.Enabled ? "Activo" : "Deshabilitado"); li.SubItems.Add(it.Publisher); li.SubItems.Add(it.Advice); li.SubItems.Add(it.Source);
                li.ToolTipText = it.Command;
                if (!it.Enabled) li.ForeColor = UI.Mute;
                lv.Items.Add(li);
            }
            lv.EndUpdate(); lv.FitLast();
            btnRefresh.Enabled = true; UpdateButtons();
            lblInfo.Text = data.Count(i => i.Enabled) + " activos de " + data.Count + " programas";
        }

        void Toggle(bool enable)
        {
            var sel = Selected().Where(i => i.Enabled != enable).ToList();
            if (sel.Count == 0) return;
            if (!enable && !UI.Confirm(this, "Estos programas dejarán de iniciarse con Windows:\n\n" + string.Join("\n", sel.Select(i => "  •  " + i.Name).ToArray()) + "\n\nSe pueden volver a habilitar cuando quieras.", "Deshabilitar al inicio", false)) return;
            var errors = new List<string>();
            foreach (var it in sel)
            {
                if (it.Machine && !Program.IsAdmin) { errors.Add(it.Name + ": requiere administrador"); continue; }
                string err = Startup.SetEnabled(it, enable);
                if (err != null) errors.Add(it.Name + ": " + err);
            }
            if (errors.Count > 0) UI.Toast(this, "No se pudo cambiar:\n\n" + string.Join("\n", errors.ToArray()));
            LoadItems();
        }
    }
}
