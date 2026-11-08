using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class ProcesosPage : PageBase
    {
        readonly SortableList lv = new SortableList();
        readonly Dictionary<string, ListViewItem> map = new Dictionary<string, ListViewItem>();
        readonly Timer timer = new Timer { Interval = 2000 };
        readonly LiveMetrics live = new LiveMetrics();
        CheckBox chkGroup; TextBox txtFind; Label lblSum;
        Dictionary<int, ProcInfo> prev; DateTime prevTime; bool ticking; List<ProcRow> lastRows = new List<ProcRow>();

        public ProcesosPage() : base("Procesos", "Qué programas están usando el procesador, la memoria y el disco en este momento.")
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("Proceso", UI.S(250)); lv.Columns.Add("CPU", UI.S(80), HorizontalAlignment.Right); lv.Columns.Add("Memoria", UI.S(110), HorizontalAlignment.Right);
            lv.Columns.Add("Disco", UI.S(100), HorizontalAlignment.Right); lv.Columns.Add("Procesos", UI.S(80), HorizontalAlignment.Right); lv.Columns.Add("Qué es", UI.S(400));
            lv.SetSort(1, false);
            card.Controls.Add(lv);

            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.Bg };
            chkGroup = new CheckBox { Text = "Agrupar por programa", Checked = true, AutoSize = true, Font = UI.Body, Location = new Point(0, UI.S(11)), BackColor = UI.Bg };
            chkGroup.CheckedChanged += (s, e) => { Clear(); Tick(); };
            txtFind = new TextBox { Font = UI.Body, Width = UI.S(210), Location = new Point(UI.S(250), UI.S(8)) };
            txtFind.TextChanged += (s, e) => Apply(lastRows);
            var lf = UI.Lbl("Buscar:", UI.Body, UI.Mute); lf.Location = new Point(UI.S(192), UI.S(12));
            lblSum = UI.Lbl("", UI.BodyBold); lblSum.Location = new Point(UI.S(600), UI.S(11));
            top.Controls.Add(chkGroup); top.Controls.Add(lf); top.Controls.Add(txtFind); top.Controls.Add(lblSum);
            top.Resize += (s, e) => lblSum.Location = new Point(top.Width - lblSum.Width, UI.S(11));

            var bar = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(54), BackColor = UI.Bg };
            var bEnd = UI.Btn("Finalizar proceso", BtnKind.Danger); bEnd.Location = new Point(0, UI.S(12));
            var bLow = UI.Btn("Bajar prioridad"); bLow.Location = new Point(bEnd.Width + UI.S(8), UI.S(12));
            var bNorm = UI.Btn("Prioridad normal"); bNorm.Location = new Point(bEnd.Width + bLow.Width + UI.S(16), UI.S(12));
            var bOpen = UI.Btn("Abrir ubicación"); bOpen.Location = new Point(bEnd.Width + bLow.Width + bNorm.Width + UI.S(24), UI.S(12));
            bEnd.Click += (s, e) => EndSelected(); bLow.Click += (s, e) => SetPriority(ProcessPriorityClass.BelowNormal); bNorm.Click += (s, e) => SetPriority(ProcessPriorityClass.Normal);
            bOpen.Click += (s, e) => { var r = SelectedRows().FirstOrDefault(); if (r != null) { string p = Native.ProcessPath(r.Pids.First()); if (p != null) ShellOpen.Location(p); } };
            bar.Controls.Add(bEnd); bar.Controls.Add(bLow); bar.Controls.Add(bNorm); bar.Controls.Add(bOpen);

            Body.Controls.Add(card); Body.Controls.Add(bar); Body.Controls.Add(top);
            timer.Tick += (s, e) => Tick();
        }

        protected override void OnFirstShow() { Task.Run(() => live.InitDisk()); }
        protected override void OnShown() { timer.Start(); Tick(); }
        public override void OnHidden() { timer.Stop(); }

        void Clear() { prev = null; lv.Items.Clear(); map.Clear(); }

        IEnumerable<ProcRow> SelectedRows() { return lv.SelectedItems.Cast<ListViewItem>().Select(i => (ProcRow)i.Tag); }

        async void Tick()
        {
            if (ticking || !Visible) return;
            ticking = true;
            try
            {
                var cur = await Task.Run(() => Procs.Snapshot());
                live.Update();
                var now = DateTime.UtcNow; double dt = prev == null ? 0 : (now - prevTime).TotalSeconds;
                var rows = Procs.Rows(prev, cur, dt, chkGroup.Checked);
                prev = cur; prevTime = now; lastRows = rows;
                if (IsDisposed) return;
                lblSum.Text = "CPU " + Math.Round(live.Cpu) + " %   ·   Memoria " + live.RamLoad + " %   ·   Disco " + (live.Disk >= 0 ? Math.Round(live.Disk) + " %" : "n/d");
                lblSum.Location = new Point(lblSum.Parent.Width - lblSum.Width, UI.S(11));
                Apply(rows);
            }
            catch { }
            finally { ticking = false; }
        }
    }
}
