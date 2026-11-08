using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace PulsoPC
{
    static partial class Tweaks
    {
        internal static readonly string[] TelTasks = {
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
            @"\Microsoft\Windows\Application Experience\StartupAppTask", @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector" };
        static readonly string[] LiteSvcs = { "MapsBroker", "RetailDemo", "wisvc", "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc", "WpcMonSvc", "lfsvc" };

        // 0 = no existe, 1 = habilitada, 2 = deshabilitada
        internal static int TaskState(string t)
        {
            var r = Sh.Run("schtasks", "/Query /TN " + Q.Arg(t) + " /XML", 15000, System.Text.Encoding.UTF8);
            if (r.Exit != 0) return 0;
            return r.Output.IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
        }
        static void TaskChange(string t, bool enable) { Sh.Run("schtasks", "/Change /TN " + Q.Arg(t) + (enable ? " /Enable" : " /Disable"), 15000); }

        static void AddServices(List<Tweak> l)
        {
            l.Add(new Tweak
            {
                Id = "diag", Title = "Desactivar la telemetría de Windows (DiagTrack)", Recommended = true, NeedsAdmin = true,
                Desc = "Detiene el servicio que recopila y envía datos de uso a Microsoft y que genera actividad constante de disco y CPU.",
                IsApplied = () => SvcStart("DiagTrack") < 0 || SvcStart("DiagTrack") == 4,
                Apply = () => { DisableService("diag", "DiagTrack", 4, true); DisableService("diag", "dmwappushservice", 4, true); },
                Revert = () => { RestoreService("diag", "DiagTrack", 2); RestoreService("diag", "dmwappushservice", 3); }
            });
            l.Add(new Tweak
            {
                Id = "tasks", Title = "Desactivar tareas programadas de telemetría", Recommended = true, NeedsAdmin = true,
                Desc = "Evita que CompatTelRunner y otras tareas de recopilación se ejecuten por sorpresa y saturen el disco.",
                IsApplied = () => TelTasks.All(t => TaskState(t) != 1),
                Apply = () => { var done = new List<string>(); foreach (var t in TelTasks) if (TaskState(t) == 1) { TaskChange(t, false); done.Add(t); } Store.Set("tw.tasks", string.Join("|", done.ToArray())); },
                Revert = () => { foreach (var t in Store.Get("tw.tasks", "").Split('|')) if (t.Length > 0) TaskChange(t, true); Store.Set("tw.tasks", null); }
            });
            l.Add(new Tweak
            {
                Id = "lite", Title = "Pasar a manual servicios que casi nadie usa", Recommended = true, NeedsAdmin = true,
                Desc = "Mapas sin conexión, Xbox, modo demo, Insider, control parental y localización. Arrancan solo si hacen falta.",
                IsApplied = () => !LiteSvcs.Any(s => SvcStart(s) == 2),
                Apply = () => { foreach (var s in LiteSvcs) if (SvcStart(s) == 2) DisableService("lite", s, 3, false); },
                Revert = () => { foreach (var s in LiteSvcs) RestoreService("lite", s, 2); }
            });
            l.Add(new Tweak
            {
                Id = "sysmain", Title = "Desactivar SysMain (Superfetch)", Recommended = false, NeedsAdmin = true, Risk = "Moderado",
                Desc = "Precarga programas en memoria. Con disco mecánico y poca RAM puede mantener el disco al 100 %; en SSD no aporta casi nada.",
                IsApplied = () => SvcStart("SysMain") == 4,
                Apply = () => DisableService("sysmain", "SysMain", 4, true),
                Revert = () => RestoreService("sysmain", "SysMain", 2)
            });
            l.Add(new Tweak
            {
                Id = "wsearch", Title = "Detener la indexación de búsqueda (Windows Search)", Recommended = false, NeedsAdmin = true, Risk = "Moderado",
                Desc = "Deja de indexar archivos en segundo plano. Buscar en el Explorador y en Inicio será más lento.",
                IsApplied = () => SvcStart("WSearch") == 4,
                Apply = () => DisableService("wsearch", "WSearch", 4, true),
                Revert = () => RestoreService("wsearch", "WSearch", 2)
            });
        }
    }
}
