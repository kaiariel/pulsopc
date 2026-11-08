using System;
using System.Collections.Generic;
using System.Linq;

namespace PulsoPC
{
    // Reglas aprendidas de casos reales: servicios rotos, memoria paginando al disco y apps que no arrancan (DCOM)
    static partial class DiagRules
    {
        // "comet (26 procesos, 3,0 GB)"
        public static string AppMem(ProcRow r)
        {
            return r.Name + " (" + (r.Count > 1 ? r.Count + " procesos, " : "") + Fmt.Bytes(r.Ws) + ")";
        }

        static string TopApps(SysSnapshot s, int n)
        {
            return string.Join(", ", s.Sample.Rows.Where(r => !Procs.Critical.Contains(r.Name)).OrderByDescending(r => r.Ws).Take(n).Select(AppMem).ToArray());
        }

        static void Paging(SysSnapshot s)
        {
            if (s.PagesIn < 0) return;
            double availGb = s.RamAvail / 1073741824.0;
            bool tight = availGb < 2.5 || s.RamLoad >= 85;
            if (tight && s.PagesIn >= 1000)
            {
                var lvl = s.PagesIn >= 5000 || s.PagesInMax >= 20000 ? Lvl.Bad : Lvl.Warn;
                s.Add(lvl, "Windows está usando el disco como memoria",
                    "Trae " + s.PagesIn.ToString("N0") + " páginas por segundo del disco (lo normal es menos de 100) porque la RAM no alcanza. Así todo se traba. Los que más ocupan: " + TopApps(s, 3) + ".",
                    "procesos", "Ver procesos");
            }
            // Una sola app que se reparte en muchos procesos y ocupa mucho (navegadores y apps Electron)
            var heavy = s.Sample.Rows.Where(r => r.Count >= 10 && r.Ws >= 1610612736L && !Procs.Critical.Contains(r.Name)).OrderByDescending(r => r.Ws).FirstOrDefault();
            if (heavy != null && s.RamLoad >= 70)
                s.Add(Lvl.Info, heavy.Name + " ocupa " + Fmt.Bytes(heavy.Ws) + " repartido en " + heavy.Count + " procesos",
                    "Es una app hecha con tecnología de navegador: cada pestaña o ventana es un proceso. Cerrarla cuando no la uses libera esa memoria de golpe.", "procesos", "Ver procesos");
        }

        static void Services(SysSnapshot s)
        {
            foreach (var b in s.Broken.Take(3))
            {
                var bs = b;
                string fails = b.Failures > 0 ? " Falló " + b.Failures + " veces esta semana." : "";
                var lvl = b.Failures >= 20 ? Lvl.Warn : Lvl.Info;
                s.Add(lvl, "Servicio deshabilitado que rompe a otro: " + b.DepName,
                    "«" + b.SvcName + "» depende de él y no puede arrancar." + fails + " Volverlo a Manual (el valor de Windows) lo arregla.");
                var f = s.Findings[s.Findings.Count - 1];
                f.ActionText = "Arreglar";
                f.Fix = owner =>
                {
                    if (!UI.Confirm(owner, "Se va a cambiar el servicio «" + bs.DepName + "» de Deshabilitado a Manual y arrancar «" + bs.SvcName + "».\n\nEl valor anterior queda guardado en el registro de cambios.", "Arreglar servicio", false)) return "";
                    return ServiceHealth.Fix(bs);
                };
            }
        }

        static void Profiles(SysSnapshot s)
        {
            if (s.TempLogons == 0 && s.TempProfiles < 3) return;
            var lvl = s.TempLogons >= 10 ? Lvl.Bad : s.TempLogons > 0 ? Lvl.Warn : Lvl.Info;
            string title = s.TempLogons > 0
                ? "Windows te dio un perfil temporal " + s.TempLogons + (s.TempLogons == 1 ? " vez" : " veces") + " en 30 días"
                : "Quedaron " + s.TempProfiles + " carpetas de perfiles temporales";
            s.Add(lvl, title,
                "Al entrar, tu perfil estaba bloqueado por un servicio que no lo soltó al apagar (" + s.HiveUnloadFails.ToString("N0") + " avisos). Quedan " + s.TempProfiles + " carpetas TEMP en C:\\Users: se borran en Espacio > Sobrantes. Desactivar el Inicio rápido suele evitarlo.",
                "optimizar", "Optimizar");
        }

        static void Dcom(SysSnapshot s)
        {
            foreach (var d in s.Dcom.Where(x => x.Count >= 2).Take(3))
            {
                var lvl = d.Count >= 6 ? Lvl.Warn : Lvl.Info;
                s.Add(lvl, d.Friendly + " no arrancó a tiempo (" + d.Count + " veces)",
                    "Windows lo esperó y se rindió; mientras tanto la barra de tareas o el Explorador pueden quedar trabados. " + d.Advice, "eventos", "Ver eventos");
            }
        }
    }
}
