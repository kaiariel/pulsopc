using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    class EventsForm : Form
    {
        readonly SortableList lv = new SortableList();
        readonly Label info;

        public EventsForm()
        {
            Text = "Errores críticos del sistema (últimos 7 días)"; Font = UI.Body; BackColor = UI.Bg;
            Size = new Size(UI.S(1040), UI.S(600)); StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
            info = UI.Lbl("Leyendo el visor de eventos...", UI.Body, UI.Mute); info.Dock = DockStyle.Top; info.AutoSize = false; info.Height = UI.S(40); info.Padding = UI.P(16, 10, 0, 0);
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Fecha", UI.S(140)); lv.Columns.Add("Nivel", UI.S(70)); lv.Columns.Add("Origen", UI.S(210)); lv.Columns.Add("Id", UI.S(60)); lv.Columns.Add("Mensaje", UI.S(520));
            var bottom = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(52), Padding = UI.P(16, 9, 16, 9), BackColor = UI.Bg };
            var close = UI.Btn("Cerrar", BtnKind.Primary); close.Dock = DockStyle.Right; close.Click += (s, e) => Close();
            var copy = UI.Btn("Copiar lista"); copy.Dock = DockStyle.Right;
            copy.Click += (s, e) => { var sb = new StringBuilder(); foreach (ListViewItem it in lv.Items) sb.AppendLine(string.Join("  |  ", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text).ToArray())); if (sb.Length > 0) Clipboard.SetText(sb.ToString()); };
            bottom.Controls.Add(copy); bottom.Controls.Add(close);
            Controls.Add(lv); Controls.Add(info); Controls.Add(bottom);
            Shown += async (s, e) => await LoadEvents();
        }

        async Task LoadEvents()
        {
            var rows = await Task.Run(() => PulsoPC.Events.Read(7, 200, true));
            if (IsDisposed) return;
            lv.BeginUpdate();
            foreach (var r in rows)
            {
                var it = new ListViewItem(r.Time.ToString("yyyy-MM-dd HH:mm")); it.SubItems[0].Tag = r.Time;
                it.SubItems.Add(r.Level); it.SubItems.Add(r.Provider); var id = it.SubItems.Add(r.Id.ToString()); id.Tag = r.Id; it.SubItems.Add(r.Message);
                it.ToolTipText = r.Message;
                if (r.Level == "Crítico") it.ForeColor = UI.Bad;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            lv.SetSort(0, false);
            info.Text = rows.Count == 0 ? "No hay errores críticos en la última semana (o se necesitan permisos para leerlos)." : rows.Count + " eventos más recientes. Pasa el cursor por un mensaje para leerlo completo.";
        }
    }
}
