using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using Microsoft.Win32;

namespace PulsoPC
{
    // Un servicio que arranca solo pero depende de otro que está deshabilitado
    class BrokenService
    {
        public string Svc = "", SvcName = "", Dep = "", DepName = "";
        public int Failures; public DateTime Last;
    }

    // Una app que Windows intentó abrir por DCOM y no respondió a tiempo (evento 10010)
    class DcomIssue
    {
        public string Clsid = "", Raw = "", Friendly = "", Advice = "";
        public int Count; public DateTime Last;
    }

    static class ServiceHealth
    {
        const string SvcKey = @"SYSTEM\CurrentControlSet\Services\";

        public static List<BrokenService> Find(int days)
        {
            var found = new Dictionary<string, BrokenService>(StringComparer.OrdinalIgnoreCase);
            ServiceController[] all;
            try { all = ServiceController.GetServices(); } catch { return new List<BrokenService>(); }
            var byDisplay = new Dictionary<string, ServiceController>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in all) { try { if (!byDisplay.ContainsKey(s.DisplayName)) byDisplay[s.DisplayName] = s; } catch { } }

            // 1) Servicios automáticos con una dependencia deshabilitada
            foreach (var s in all)
            {
                try
                {
                    if (s.StartType != ServiceStartMode.Automatic) continue;
                    foreach (var d in s.ServicesDependedOn)
                    {
                        try { if (d.StartType == ServiceStartMode.Disabled) Pair(found, s.ServiceName, s.DisplayName, d.ServiceName, d.DisplayName); } catch { }
                    }
                }
                catch { }
            }

            // 2) Errores 7001 del administrador de servicios: "X depende de Y, que no pudo iniciarse"
            try
            {
                long ms = days * 86400000L;
                string q = "*[System[Provider[@Name='Service Control Manager'] and EventID=7001 and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
                using (var r = new EventLogReader(new EventLogQuery("System", PathType.LogName, q) { ReverseDirection = true }))
                {
                    EventRecord ev; int n = 0;
                    while (n++ < 3000 && (ev = r.ReadEvent()) != null)
                    {
                        using (ev)
                        {
                            if (ev.Properties.Count < 2) continue;
                            string a = Convert.ToString(ev.Properties[0].Value), b = Convert.ToString(ev.Properties[1].Value);
                            ServiceController sa, sb;
                            if (!byDisplay.TryGetValue(b, out sb)) continue;
                            bool depDisabled; try { depDisabled = sb.StartType == ServiceStartMode.Disabled; } catch { continue; }
                            if (!depDisabled) continue;
                            string svc = byDisplay.TryGetValue(a, out sa) ? sa.ServiceName : a;
                            var bs = Pair(found, svc, a, sb.ServiceName, sb.DisplayName);
                            bs.Failures++;
                            var t = ev.TimeCreated ?? DateTime.MinValue; if (t > bs.Last) bs.Last = t;
                        }
                    }
                }
            }
            catch { }
            return found.Values.OrderByDescending(x => x.Failures).ToList();
        }

        // Para la autoprueba: los pares "servicio <- dependencia" de los errores 7001, sin mirar el estado actual
        public static List<string> RawPairs(int days, int max)
        {
            var l = new List<string>();
            try
            {
                string q = "*[System[Provider[@Name='Service Control Manager'] and EventID=7001 and TimeCreated[timediff(@SystemTime) <= " + days * 86400000L + "]]]";
                using (var r = new EventLogReader(new EventLogQuery("System", PathType.LogName, q) { ReverseDirection = true }))
                {
                    EventRecord ev;
                    while (l.Count < max && (ev = r.ReadEvent()) != null)
                        using (ev) if (ev.Properties.Count >= 2) l.Add(Convert.ToString(ev.Properties[0].Value) + " <- " + Convert.ToString(ev.Properties[1].Value));
                }
            }
            catch { }
            return l;
        }

        static BrokenService Pair(Dictionary<string, BrokenService> d, string svc, string svcName, string dep, string depName)
        {
            string k = dep + "|" + svc; BrokenService b;
            if (!d.TryGetValue(k, out b)) d[k] = b = new BrokenService { Svc = svc, SvcName = svcName, Dep = dep, DepName = depName };
            return b;
        }

        // Vuelve la dependencia a "Manual" (valor de fábrica de casi todos los servicios) y arranca ambos
        public static string Fix(BrokenService b)
        {
            if (!Program.IsAdmin) return "Hace falta abrir PulsoPC como administrador para cambiar servicios.";
            try
            {
                int cur = Tweaks.SvcStart(b.Dep);
                if (cur < 0) return "No se encontró el servicio " + b.Dep + ".";
                string key = "svcfix." + b.Dep;
                if (Store.Get(key) == null) Store.Set(key, cur.ToString());
                Tweaks.SetSvcStart(b.Dep, 3);
                Tweaks.StartSvc(b.Dep);
                Tweaks.StartSvc(b.Svc);
                Log.Write("Servicio " + b.Dep + " (" + b.DepName + ") pasó de deshabilitado a manual para que funcione " + b.Svc + ". Valor anterior guardado en estado.ini (" + key + ").");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    static class DcomHealth
    {
        // Apps conocidas: qué son y qué hacer
        static readonly string[][] Known =
        {
            new[] { "WidgetsPlatformRuntime", "Widgets de Windows (clima y noticias de la barra de tareas)", "Si no los usás, apagalos: clic derecho en la barra de tareas > Configuración de la barra de tareas > Widgets." },
            new[] { "Thumbnail Cache", "Generador de miniaturas del Explorador", "Suele colgarse en carpetas de Google Drive u OneDrive con muchas fotos o videos, porque tiene que bajar cada archivo para dibujar la miniatura. Abrí esas carpetas en vista Lista o Detalles." },
            new[] { "ShellExperienceHost", "Menú Inicio y centro de actividades", "Si se repite, reiniciá el Explorador desde Reparar." },
            new[] { "StartMenuExperienceHost", "Menú Inicio", "Si se repite, reiniciá el Explorador desde Reparar." },
            new[] { "SearchHost", "Búsqueda de Windows", "Si se repite, reconstruí el índice de búsqueda desde Reparar." },
            new[] { "Windows.Photos", "App Fotos", "Suele pasar al abrir fotos de carpetas en la nube." },
            new[] { "Microsoft.WindowsTerminal", "Terminal de Windows", "Actualizá la Terminal desde la Microsoft Store." },
            new[] { "Copilot", "Copilot", "Si no lo usás, quitalo de la barra de tareas." },
        };

        public static List<DcomIssue> Find(int days)
        {
            var map = new Dictionary<string, DcomIssue>(StringComparer.OrdinalIgnoreCase);
            try
            {
                long ms = days * 86400000L;
                string q = "*[System[Provider[@Name='Microsoft-Windows-DistributedCOM'] and EventID=10010 and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
                using (var r = new EventLogReader(new EventLogQuery("System", PathType.LogName, q) { ReverseDirection = true }))
                {
                    EventRecord ev; int n = 0;
                    while (n++ < 2000 && (ev = r.ReadEvent()) != null)
                    {
                        using (ev)
                        {
                            if (ev.Properties.Count < 1) continue;
                            string id = Convert.ToString(ev.Properties[0].Value).Trim();
                            DcomIssue d;
                            if (!map.TryGetValue(id, out d)) map[id] = d = new DcomIssue { Clsid = id };
                            d.Count++;
                            var t = ev.TimeCreated ?? DateTime.MinValue; if (t > d.Last) d.Last = t;
                        }
                    }
                }
            }
            catch { }
            foreach (var d in map.Values) Describe(d);
            // Una misma app puede registrar varios componentes (los Widgets usan dos): se juntan por nombre
            return map.Values.GroupBy(x => x.Friendly).Select(g => new DcomIssue
            {
                Clsid = string.Join(", ", g.Select(x => x.Clsid).ToArray()), Raw = g.First().Raw, Friendly = g.Key, Advice = g.First().Advice,
                Count = g.Sum(x => x.Count), Last = g.Max(x => x.Last)
            }).OrderByDescending(x => x.Count).ToList();
        }

        public static void Describe(DcomIssue d)
        {
            d.Raw = Resolve(d.Clsid);
            foreach (var k in Known)
                if (d.Raw.IndexOf(k[0], StringComparison.OrdinalIgnoreCase) >= 0) { d.Friendly = k[1]; d.Advice = k[2]; return; }
            d.Friendly = d.Raw.Length > 0 ? d.Raw : "Componente " + d.Clsid;
            d.Advice = "Si coincide con las trabas, buscá qué programa es y actualizalo o desactivalo.";
        }

        // Nombre legible de un CLSID: clase COM, paquete de la Tienda o AppID
        public static string Resolve(string clsid)
        {
            try
            {
                using (var k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid))
                {
                    if (k != null)
                    {
                        string name = k.GetValue("") as string;
                        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                        string app = k.GetValue("AppID") as string;
                        if (!string.IsNullOrEmpty(app))
                            using (var a = Registry.ClassesRoot.OpenSubKey(@"AppID\" + app))
                            {
                                string an = a == null ? null : a.GetValue("") as string;
                                if (!string.IsNullOrWhiteSpace(an)) return an.Trim();
                            }
                    }
                }
                using (var p = Registry.ClassesRoot.OpenSubKey(@"PackagedCom\ClassIndex\" + clsid))
                {
                    if (p != null)
                    {
                        string pkg = p.GetSubKeyNames().FirstOrDefault();
                        if (!string.IsNullOrEmpty(pkg)) { int u = pkg.IndexOf('_'); return u > 0 ? pkg.Substring(0, u) : pkg; }
                    }
                }
            }
            catch { }
            return "";
        }
    }

    // Inicios de sesión con perfil temporal: el perfil del usuario quedó bloqueado por un servicio
    static class ProfileHealth
    {
        // {sesiones con perfil temporal (1511), avisos de registro sin descargar (1512), carpetas TEMP* en Users}
        public static int[] Find(int days)
        {
            var r = new int[3];
            string sid = null;
            try { sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value; } catch { }
            long ms = days * 86400000L;
            foreach (var id in new[] { 1511, 1512 })
            {
                try
                {
                    string q = "*[System[Provider[@Name='Microsoft-Windows-User Profiles Service'] and EventID=" + id + " and TimeCreated[timediff(@SystemTime) <= " + ms + "]" + (sid != null && id == 1511 ? " and Security[@UserID='" + sid + "']" : "") + "]]";
                    using (var rd = new EventLogReader(new EventLogQuery("Application", PathType.LogName, q)))
                    {
                        EventRecord ev; int n = 0;
                        while (n < 20000 && (ev = rd.ReadEvent()) != null) using (ev) n++;
                        r[id == 1511 ? 0 : 1] = n;
                    }
                }
                catch { }
            }
            try { r[2] = System.IO.Directory.GetDirectories(System.IO.Path.Combine(Env.SysDrive, "Users"), "TEMP*").Length; } catch { }
            return r;
        }
    }

    static class MemPressure
    {
        // Lecturas de página por segundo (memoria que Windows tuvo que traer del disco). Devuelve {promedio, máximo}
        public static double[] PagesIn(int samples, int gapMs)
        {
            var v = new List<double>();
            for (int i = 0; i < samples; i++)
            {
                try
                {
                    using (var s = new ManagementObjectSearcher(Wmi.Scope("root/cimv2"), "SELECT PagesInputPersec FROM Win32_PerfFormattedData_PerfOS_Memory"))
                        foreach (ManagementObject o in s.Get()) { v.Add(Convert.ToDouble(o["PagesInputPersec"])); o.Dispose(); }
                }
                catch { }
                if (i < samples - 1) System.Threading.Thread.Sleep(gapMs);
            }
            if (v.Count == 0) return new[] { -1.0, -1.0 };
            return new[] { v.Average(), v.Max() };
        }
    }
}
