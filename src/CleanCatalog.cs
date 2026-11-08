using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PulsoPC
{
    static partial class CleanCatalog
    {
        static Func<FileInfo, bool> Older(int hours)
        {
            return fi => (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalHours >= hours;
        }

        static Target T(string dir, bool rec = true, int ageH = 0, Func<FileInfo, bool> filter = null)
        {
            Func<FileInfo, bool> f = filter;
            if (ageH > 0) { var o = Older(ageH); f = filter == null ? o : (fi => o(fi) && filter(fi)); }
            return new Target { Dir = dir, Recursive = rec, Filter = f, MinAgeHours = ageH };
        }

        static string Loc(params string[] p) { return Path.Combine(Env.Local, Path.Combine(p)); }
        static string Roam(params string[] p) { return Path.Combine(Env.Roaming, Path.Combine(p)); }
        static string Win(params string[] p) { return Path.Combine(Env.Win, Path.Combine(p)); }

        static CleanCat C(string id, string name, string desc, string group, Func<List<Target>> targets, bool on = true, bool admin = false, bool hide = true, string note = "", string[] svc = null)
        {
            return new CleanCat { Id = id, Name = name, Desc = desc, Group = group, GetTargets = targets, DefaultOn = on, NeedsAdmin = admin, HideIfEmpty = hide, Note = note, Services = svc ?? new string[0] };
        }

        static bool NameIs(FileInfo fi, string n) { return fi.Name.Equals(n, StringComparison.OrdinalIgnoreCase); }

        public static List<CleanCat> Build()
        {
            var l = new List<CleanCat>();
            const string W = "Windows";

            l.Add(C("tempuser", "Archivos temporales del usuario", "Restos de instalaciones y programas. No se tocan los de las últimas 24 horas.", W,
                () => new List<Target> { T(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), true, 24) }, true, false, false));
            l.Add(C("tempwin", "Archivos temporales de Windows", "Carpeta Temp del sistema. Solo archivos de más de 24 horas.", W,
                () => new List<Target> { T(Win("Temp"), true, 24) }, true, true, false));
            l.Add(C("wu", "Caché de Windows Update", "Paquetes de actualización ya instalados. Windows los vuelve a bajar si hacen falta.", W,
                () => new List<Target> { T(Win("SoftwareDistribution", "Download")) }, true, true, false, "Detiene el servicio unos segundos", new[] { "wuauserv", "bits" }));
            l.Add(C("do", "Optimización de entrega", "Caché de actualizaciones compartidas con otros equipos.", W,
                () => new List<Target> { T(Win("ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")), T(Win("SoftwareDistribution", "DeliveryOptimization")) },
                true, true, true, "", new[] { "dosvc" }));
            var rb = C("recycle", "Papelera de reciclaje", "Elimina de forma definitiva lo que hay en la papelera.", W, () => new List<Target>(), false, false, false, "Irreversible");
            rb.Kind = CatKind.RecycleBin; l.Add(rb);
            var th = C("thumbs", "Miniaturas e iconos en caché", "Se regeneran solos. El Explorador se reinicia unos segundos.", W,
                () => new List<Target> { T(Loc("Microsoft", "Windows", "Explorer"), false, 0, fi => (fi.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || fi.Name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase)) && fi.Extension.Equals(".db", StringComparison.OrdinalIgnoreCase)) },
                true, false, true, "Reinicia el Explorador");
            th.Kind = CatKind.Thumbnails; l.Add(th);
            l.Add(C("wer", "Informes de errores de Windows", "Reportes de fallos de programas ya enviados o descartados.", W,
                () => new List<Target> { T(Loc("Microsoft", "Windows", "WER")), T(Path.Combine(Env.ProgramData, "Microsoft", "Windows", "WER")) }));
            l.Add(C("dumps", "Volcados de memoria de fallos", "Archivos que Windows guarda cuando se cuelga o reinicia solo.", W,
                () => new List<Target> {
                    T(Win("Minidump")), T(Loc("CrashDumps")),
                    T(Win("LiveKernelReports"), true, 0, fi => fi.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)),
                    T(Env.Win, false, 0, fi => NameIs(fi, "MEMORY.DMP")) }, true, true));
            l.Add(C("logs", "Registros de Windows (CBS y DISM)", "Registros de instalación de más de 3 días.", W,
                () => new List<Target> { T(Win("Logs", "CBS"), true, 72), T(Win("Logs", "DISM"), true, 72) }, true, true));

            AddBrowsers(l);
            AddApps(l);
            AddDev(l);
            return l;
        }
    }
}
