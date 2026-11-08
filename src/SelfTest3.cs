using System;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace PulsoPC
{
    static partial class SelfTest
    {
        static string Dump(string path, string[] names)
        {
            return string.Join("|", names.Select(n => n + "=" + Convert.ToString(Reg.Get(Registry.CurrentUser, path, n, "(sin valor)"))).ToArray());
        }

        static void RoundTrip(string id, string path, string[] names)
        {
            var t = Tweaks.All.First(x => x.Id == id);
            if (t.NeedsAdmin && !Program.IsAdmin) { Info("ajuste '" + id + "' omitido: requiere administrador"); return; }
            string before = Dump(path, names);
            t.Apply(); bool on = t.IsApplied();
            t.Revert(); string after = Dump(path, names);
            Check("ajuste '" + id + "': se aplica", on);
            Check("ajuste '" + id + "': revertir deja el registro como estaba", before == after, "antes=" + before + "  después=" + after);
        }

        static void TestTweak()
        {
            RoundTrip("dvr", @"System\GameConfigStore", new[] { "GameDVR_Enabled" });
            RoundTrip("dvr", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", new[] { "AppCaptureEnabled" });
            RoundTrip("web", @"Software\Microsoft\Windows\CurrentVersion\Search", new[] { "BingSearchEnabled", "CortanaConsent" });
            RoundTrip("web", @"Software\Policies\Microsoft\Windows\Explorer", new[] { "DisableSearchBoxSuggestions" });
            RoundTrip("sugg", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", new[] { "ContentDeliveryAllowed", "SystemPaneSuggestionsEnabled", "SilentInstalledAppsEnabled", "SubscribedContent-338388Enabled" });
        }

        static void TestInfo()
        {
            var rb = CleanerService.Cats.First(c => c.Kind == CatKind.RecycleBin);
            CleanerService.Reanalyze(rb);
            Check("papelera: consulta de tamaño", rb.Size >= 0, Fmt.Bytes(rb.Size) + ", " + rb.Files + " elementos");
            var live = new LiveMetrics(); live.Update(); Thread.Sleep(600); live.Update();
            Check("lectura de memoria", live.RamTotal > 0 && live.RamLoad > 0, live.RamLoad + " % de " + Fmt.Bytes((long)live.RamTotal));
            Check("lectura de CPU en rango", live.Cpu >= 0 && live.Cpu <= 100, Math.Round(live.Cpu) + " %");
            var d = Hw.Disks();
            Check("detecta discos físicos", d.Count > 0, string.Join("; ", d.Select(x => x.Label + " " + x.Name + (x.IsSystem ? " (sistema)" : "")).ToArray()));
            string g, n; var k = Hw.PowerPlan(out g, out n);
            Info("Plan de energía: " + k + " (" + n + ")");
            Info("Tareas de telemetría (0 no existe, 1 activa, 2 deshabilitada): " + string.Join(",", Tweaks.TelTasks.Select(t => Tweaks.TaskState(t).ToString()).ToArray()));
            Info("Servicios: DiagTrack=" + Tweaks.SvcStart("DiagTrack") + " SysMain=" + Tweaks.SvcStart("SysMain") + " WSearch=" + Tweaks.SvcStart("WSearch"));
            var states = Tweaks.All.Select(t => { bool a; try { a = t.IsApplied(); } catch { a = false; } return t.Id + "=" + (a ? "sí" : "no"); });
            Info("Ajustes ya aplicados: " + string.Join(", ", states.ToArray()));
            Info("Categorías de limpieza definidas: " + CleanerService.Cats.Count);
        }
    }
}
