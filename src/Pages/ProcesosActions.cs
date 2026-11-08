using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class ProcesosPage
    {
        void Apply(List<ProcRow> rows)
        {
            string q = txtFind.Text.Trim();
            var seen = new HashSet<string>();
            lv.BeginUpdate();
            foreach (var r in rows)
            {
                if (q.Length > 0 && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string key = chkGroup.Checked ? r.Name.ToLowerInvariant() : r.Pid.ToString();
                seen.Add(key);
                ListViewItem it;
                string name = chkGroup.Checked ? r.Name : r.Name + "  (" + r.Pid + ")";
                if (!map.TryGetValue(key, out it))
                {
                    it = new ListViewItem(name); for (int i = 0; i < 5; i++) it.SubItems.Add("");
                    map[key] = it; lv.Items.Add(it);
                }
                it.Tag = r; it.Text = name;
                it.SubItems[1].Text = r.Cpu >= 0.05 ? r.Cpu.ToString("0.0") + " %" : "0 %"; it.SubItems[1].Tag = r.Cpu;
                it.SubItems[2].Text = Fmt.Bytes(r.Ws); it.SubItems[2].Tag = r.Ws;
                double mb = r.IoBps / 1048576.0;
                it.SubItems[3].Text = mb >= 0.05 ? mb.ToString("0.0") + " MB/s" : "-"; it.SubItems[3].Tag = r.IoBps;
                it.SubItems[4].Text = r.Count.ToString(); it.SubItems[4].Tag = r.Count;
                it.SubItems[5].Text = Procs.Note(r.Name);
                it.ForeColor = r.Cpu >= 20 ? UI.Bad : r.Cpu >= 8 ? UI.Warn : UI.Ink;
            }
            foreach (var k in map.Keys.Where(k => !seen.Contains(k)).ToList()) { map[k].Remove(); map.Remove(k); }
            lv.Sort();
            lv.EndUpdate(); lv.FitLast();
        }

        void EndSelected()
        {
            var sel = SelectedRows().ToList();
            if (sel.Count == 0) { UI.Toast(this, "Selecciona un proceso de la lista."); return; }
            var blocked = sel.Where(r => Procs.Critical.Contains(r.Name)).ToList();
            if (blocked.Count > 0) { UI.Toast(this, "Windows necesita estos procesos para funcionar y no se pueden finalizar:\n\n" + string.Join(", ", blocked.Select(b => b.Name).ToArray())); return; }
            int n = sel.Sum(r => r.Pids.Count);
            string names = string.Join("\n", sel.Take(8).Select(r => "  •  " + r.Name + (r.Count > 1 ? "  (x" + r.Count + ")" : "")).ToArray());
            if (!UI.Confirm(this, "Se cerrarán " + n + " proceso(s):\n\n" + names + "\n\nSi tienen trabajo sin guardar, se perderá.", "Finalizar proceso")) return;
            int fail = 0;
            foreach (var r in sel) foreach (int pid in r.Pids) { try { using (var p = Process.GetProcessById(pid)) p.Kill(); } catch { fail++; } }
            Log.Write("Procesos finalizados: " + string.Join(", ", sel.Select(r => r.Name).ToArray()) + (fail > 0 ? " (fallaron " + fail + ")" : ""));
            if (fail > 0) UI.Toast(this, "No se pudieron cerrar " + fail + " proceso(s). Prueba abriendo PulsoPC como administrador.");
            Tick();
        }

        void SetPriority(ProcessPriorityClass pc)
        {
            var sel = SelectedRows().ToList();
            if (sel.Count == 0) { UI.Toast(this, "Selecciona un proceso de la lista."); return; }
            int fail = 0;
            foreach (var r in sel) foreach (int pid in r.Pids) { try { using (var p = Process.GetProcessById(pid)) p.PriorityClass = pc; } catch { fail++; } }
            Log.Write("Prioridad " + pc + ": " + string.Join(", ", sel.Select(r => r.Name).ToArray()));
            if (fail > 0) UI.Toast(this, "No se pudo cambiar la prioridad de " + fail + " proceso(s).");
        }
    }
}
