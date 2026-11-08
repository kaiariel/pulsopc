using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    partial class OptimizarPage
    {
        // Se ejecuta fuera del hilo de la interfaz
        string RunTweak(Tweak t, bool apply, bool wantRestore)
        {
            try
            {
                if (apply && wantRestore && t.NeedsAdmin && !restoreDone)
                {
                    restoreDone = true;
                    string rp = Tweaks.CreateRestorePoint("PulsoPC: antes de optimizar");
                    Log.Write("Punto de restauración: " + (rp == null ? "solicitado" : "no creado (" + rp + ")"));
                }
                if (apply) t.Apply(); else t.Revert();
                t.Applied = t.IsApplied();
                Log.Write((apply ? "Aplicado: " : "Revertido: ") + t.Title);
                return null;
            }
            catch (Exception ex) { Log.Write("Error en '" + t.Title + "': " + ex.Message); return ex.Message; }
        }

        async void Toggle(TweakRow row)
        {
            var t = row.T;
            if (busy) return;
            if (t.NeedsAdmin && !Program.IsAdmin) { UI.Toast(this, "Este ajuste necesita permisos de administrador. Usa Reiniciar como administrador en el menú lateral."); return; }
            bool apply = !t.Applied;
            if (apply && t.Risk != "Seguro" && !UI.Confirm(this, t.Title + "\n\n" + t.Desc + "\n\nSe puede revertir desde esta misma página.\n\n¿Aplicar?", "Ajuste moderado", false)) return;
            busy = true; row.Busy = true; row.RefreshState(); bool wantRp = chkRestore.Checked;
            string err = await Task.Run(() => RunTweak(t, apply, wantRp));
            busy = false; row.Busy = false; row.RefreshState();
            if (err != null) UI.Toast(this, "No se pudo completar el cambio:\n\n" + err);
        }

        async void ApplyRecommended()
        {
            if (busy) return;
            var todo = Tweaks.All.Where(t => t.Recommended && !t.Applied && (!t.NeedsAdmin || Program.IsAdmin)).ToList();
            bool skipped = Tweaks.All.Any(t => t.Recommended && !t.Applied && t.NeedsAdmin && !Program.IsAdmin);
            if (todo.Count == 0) { UI.Toast(this, skipped ? "Los ajustes que faltan necesitan permisos de administrador." : "Ya tienes aplicados todos los ajustes recomendados."); return; }
            string names = string.Join("\n", todo.Select(t => "  •  " + t.Title).ToArray());
            if (!UI.Confirm(this, "Se aplicarán " + todo.Count + " ajustes recomendados:\n\n" + names + "\n\nTodos se pueden revertir desde esta página.\n\n¿Continuar?", "Aplicar recomendados", false)) return;
            busy = true; btnAll.Enabled = false; bool wantRp = chkRestore.Checked;
            var errors = new List<string>();
            foreach (var r in rows.Where(r => todo.Contains(r.T))) { r.Busy = true; r.RefreshState(); }
            await Task.Run(() =>
            {
                foreach (var t in todo)
                {
                    string err = RunTweak(t, true, wantRp);
                    if (err != null) errors.Add(t.Title + ": " + err);
                    var tt = t; Ui(() => { var row = rows.First(x => x.T == tt); row.Busy = false; row.RefreshState(); });
                }
            });
            busy = false; btnAll.Enabled = true;
            foreach (var r in rows) { r.Busy = false; r.RefreshState(); }
            UI.Toast(this, errors.Count == 0 ? "Listo. Se aplicaron " + todo.Count + " ajustes." + (skipped ? "\n\nQuedaron otros que necesitan permisos de administrador." : "") : "Algunos ajustes fallaron:\n\n" + string.Join("\n", errors.ToArray()));
        }

        async void FreeRam()
        {
            if (!Program.IsAdmin) { UI.Toast(this, "Esta acción necesita permisos de administrador."); return; }
            btnRam.Enabled = false; lblRam.Text = "Liberando...";
            var m = new LiveMetrics(); m.Update(); ulong before = m.RamAvail; int r1 = 0, r2 = 0;
            await Task.Run(() =>
            {
                Native.EnablePrivilege("SeProfileSingleProcessPrivilege"); Native.EnablePrivilege("SeIncreaseQuotaPrivilege");
                r1 = Native.MemoryCommand(2); r2 = Native.MemoryCommand(4); Thread.Sleep(1200);
            });
            m.Update(); ulong after = m.RamAvail;
            btnRam.Enabled = true;
            if (r1 != 0 && r2 != 0) { lblRam.ForeColor = UI.Bad; lblRam.Text = "Windows no permitió la operación."; return; }
            long diff = (long)after - (long)before;
            lblRam.ForeColor = UI.Good;
            lblRam.Text = "Libre: " + Fmt.Bytes((long)before) + " → " + Fmt.Bytes((long)after) + (diff > 0 ? "  (+" + Fmt.Bytes(diff) + ")" : "");
            Log.Write("Memoria liberada: " + Fmt.Bytes(diff));
        }
    }
}
