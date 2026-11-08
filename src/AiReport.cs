using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PulsoPC
{
    // Informe en Markdown pensado para pegarlo en Claude o ChatGPT: datos medidos + instrucciones para la IA
    static class AiReport
    {
        public static string Build(SysSnapshot s, bool anonymize, bool monitor, bool map)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Informe de PulsoPC para análisis con IA");
            sb.AppendLine();
            sb.AppendLine("> **Para la IA:** sos un técnico de Windows con experiencia. Este informe lo generó PulsoPC midiendo mi PC. Con estos datos, y sin inventar nada que no esté acá:");
            sb.AppendLine("> 1. Decime las 3 causas más probables de lentitud o trabas, cada una con la evidencia del informe que la respalda.");
            sb.AppendLine("> 2. Proponé qué hacer en orden, empezando por lo reversible y lo que no requiere instalar nada. Aclará qué se puede deshacer y cómo.");
            sb.AppendLine("> 3. Decime qué NO conviene tocar.");
            sb.AppendLine("> 4. Si para decidir falta un dato, decime exactamente qué medir.");
            sb.AppendLine();
            sb.AppendLine("Generado: " + s.Taken.ToString("yyyy-MM-dd HH:mm") + (s.Admin ? " (como administrador)" : " (sin permisos de administrador)") + " · Salud estimada: " + s.Score + "/100");
            sb.AppendLine();

            sb.AppendLine("## Equipo");
            sb.AppendLine("| Dato | Valor |"); sb.AppendLine("|---|---|");
            Row(sb, "Equipo", s.Pc); Row(sb, "Windows", s.Os); Row(sb, "Procesador", s.Cpu + " (" + s.Threads + " hilos)");
            Row(sb, "Memoria", Fmt.Bytes((long)s.RamTotal) + ", en uso " + s.RamLoad + " %, libre " + Fmt.Bytes((long)s.RamAvail));
            if (!string.IsNullOrEmpty(s.Gpu)) Row(sb, "Gráfica", s.Gpu);
            Row(sb, "Encendido", Fmt.Dur(s.Uptime) + (s.FastStartup ? " (Inicio rápido activado)" : ""));
            Row(sb, "Plan de energía", s.PowerName);
            foreach (var d in s.Disks) Row(sb, "Disco " + d.Name, d.Label + ", " + Fmt.Bytes(d.Size) + ", salud " + d.Health + (d.IsSystem ? " (Windows)" : ""));
            foreach (var d in s.Drives) Row(sb, "Unidad " + d.Letter, Fmt.Bytes(d.Free) + " libres de " + Fmt.Bytes(d.Total) + " (" + Math.Round(d.FreePct) + " %)");
            if (s.TempLogons > 0 || s.TempProfiles > 0) Row(sb, "Perfiles temporales", s.TempLogons + " inicios de sesión con perfil temporal en 30 días, " + s.HiveUnloadFails.ToString("N0") + " avisos de registro sin descargar al cerrar sesión, " + s.TempProfiles + " carpetas TEMP en Users");
            if (s.PagesIn >= 0) Row(sb, "Lecturas de página", s.PagesIn.ToString("N0") + "/s de promedio, " + s.PagesInMax.ToString("N0") + "/s de máximo (lo normal es menos de 100)");
            sb.AppendLine();

            sb.AppendLine("## Hallazgos de PulsoPC");
            foreach (var f in s.Findings.Where(x => x.Level != Lvl.Ok))
                sb.AppendLine("- **[" + Tag(f.Level) + "] " + f.Title + "**" + (string.IsNullOrEmpty(f.Detail) ? "" : ": " + f.Detail));
            var ok = s.Findings.Where(x => x.Level == Lvl.Ok).Select(x => x.Title).ToArray();
            if (ok.Length > 0) sb.AppendLine("- Bien: " + string.Join("; ", ok) + ".");
            sb.AppendLine();

            sb.AppendLine("## Lo que más consume ahora (CPU " + Math.Round(s.Sample.Cpu) + " %, disco " + (s.Sample.Disk < 0 ? "?" : Math.Round(s.Sample.Disk) + " %") + ")");
            sb.AppendLine("| App | Procesos | RAM | CPU | Disco |"); sb.AppendLine("|---|---:|---:|---:|---:|");
            var top = s.Sample.Rows.OrderByDescending(r => r.Ws).Take(10).Union(s.Sample.Rows.OrderByDescending(r => r.Cpu).Take(5)).Distinct().OrderByDescending(r => r.Ws);
            foreach (var r in top) sb.AppendLine("| " + r.Name + " | " + r.Count + " | " + Fmt.Bytes(r.Ws) + " | " + Math.Round(r.Cpu, 1) + " % | " + (r.IoBps / 1048576.0).ToString("0.0") + " MB/s |");
            sb.AppendLine();

            if (s.Broken.Count > 0 || s.Dcom.Count > 0)
            {
                sb.AppendLine("## Servicios y componentes con fallas (7 días)");
                foreach (var b in s.Broken) sb.AppendLine("- Servicio «" + b.SvcName + "» (" + b.Svc + ") depende de «" + b.DepName + "» (" + b.Dep + "), que está deshabilitado. Fallos: " + b.Failures + ".");
                foreach (var d in s.Dcom) sb.AppendLine("- No arrancó a tiempo (DCOM 10010) " + d.Count + (d.Count == 1 ? " vez: " : " veces: ") + d.Friendly + " [" + d.Clsid + "], último " + d.Last.ToString("dd/MM HH:mm") + ".");
                sb.AppendLine();
            }

            sb.AppendLine("## Programas que arrancan con Windows (" + s.StartupEnabled + ")");
            foreach (var i in s.Startup.Where(x => x.Enabled)) sb.AppendLine("- " + i.Name + (string.IsNullOrEmpty(i.Publisher) ? "" : " (" + i.Publisher + ")"));
            sb.AppendLine();

            if (monitor) Monitor(sb);
            if (map) Map(sb);

            sb.AppendLine("---");
            sb.AppendLine("Generado por PulsoPC 1.1. Los datos son mediciones del equipo en ese momento; PulsoPC no envía nada por su cuenta.");
            string text = sb.ToString();
            return anonymize ? Anonymize(text) : text;
        }

        static void Row(StringBuilder sb, string k, string v) { sb.AppendLine("| " + k + " | " + (v ?? "").Replace("|", "/") + " |"); }
        static string Tag(Lvl l) { return l == Lvl.Bad ? "CRÍTICO" : l == Lvl.Warn ? "AVISO" : "INFO"; }

        static void Monitor(StringBuilder sb)
        {
            var data = HangMonitor.Recent(TimeSpan.FromHours(2));
            sb.AppendLine("## Monitor de trabas");
            if (data.Count == 0) { sb.AppendLine("El monitor no estuvo activo en las últimas 2 horas."); sb.AppendLine(); return; }
            sb.AppendLine("Muestras cada " + HangMonitor.IntervalMs / 1000 + " s desde las " + data[0].T.ToString("HH:mm") + " hasta las " + data[data.Count - 1].T.ToString("HH:mm") + " (" + data.Count.ToString("N0") + " muestras).");
            sb.AppendLine();
            sb.AppendLine("| Medida | Mínimo | Promedio | Máximo |"); sb.AppendLine("|---|---:|---:|---:|");
            sb.AppendLine("| RAM libre (MB) | " + data.Min(x => x.RamFreeMb) + " | " + (int)data.Average(x => x.RamFreeMb) + " | " + data.Max(x => x.RamFreeMb) + " |");
            var pg = data.Where(x => x.PagesIn >= 0).ToList();
            if (pg.Count > 0) sb.AppendLine("| Lecturas de página/s | " + pg.Min(x => x.PagesIn) + " | " + (int)pg.Average(x => x.PagesIn) + " | " + pg.Max(x => x.PagesIn) + " |");
            sb.AppendLine("| CPU (%) | " + data.Min(x => x.Cpu) + " | " + (int)data.Average(x => x.Cpu) + " | " + data.Max(x => x.Cpu) + " |");
            var bar = data.Where(x => x.BarMs >= 0).ToList();
            if (bar.Count > 0) sb.AppendLine("| Barra de tareas (ms) | " + bar.Min(x => x.BarMs) + " | " + (int)bar.Average(x => x.BarMs) + " | " + bar.Max(x => x.BarMs) + " |");
            sb.AppendLine();
            var odd = data.Where(x => x.Odd).ToList();
            if (odd.Count > 0)
            {
                sb.AppendLine("Momentos raros (" + odd.Count + ", se muestran los últimos 25):");
                sb.AppendLine("| Hora | Qué pasó | Al frente | Más disco / CPU |"); sb.AppendLine("|---|---|---|---|");
                foreach (var x in odd.Skip(Math.Max(0, odd.Count - 25)))
                    sb.AppendLine("| " + x.T.ToString("HH:mm:ss") + " | " + string.Join("; ", x.Flags.ToArray()) + " | " + x.FgApp + " | " + string.Join(" · ", new[] { x.TopRead, x.TopCpu }.Where(t => t.Length > 0).ToArray()) + " |");
                sb.AppendLine();
            }
            else { sb.AppendLine("No hubo momentos raros en ese período."); sb.AppendLine(); }
            var v = HangMonitor.LastVerdict;
            if (v != null)
            {
                sb.AppendLine("Última traba marcada por el usuario (" + v.At.ToString("HH:mm:ss") + "): **" + v.Title + "**");
                foreach (var l in v.Lines) sb.AppendLine("- " + l);
                sb.AppendLine();
            }
        }

        static void Map(StringBuilder sb)
        {
            var ix = DiskIndex.Last;
            sb.AppendLine("## Mapa del disco");
            if (ix == null) { sb.AppendLine("No se analizó ninguna unidad en esta sesión (Espacio en disco > Mapa del disco)."); sb.AppendLine(); return; }
            sb.AppendLine("Unidad " + ix.Drive + ": " + Fmt.Bytes(ix.Total) + " en total, " + Fmt.Bytes(ix.Total - ix.Free) + " usados, " + Fmt.Bytes(ix.Free) + " libres. " + ix.ScannedFiles.ToString("N0") + " archivos en " + ix.ScannedDirs.ToString("N0") + " carpetas (método: " + ix.Method + ").");
            sb.AppendLine();
            sb.AppendLine("Carpetas más grandes (dos niveles):");
            foreach (var k in (ix.Root.Kids ?? new List<DNode>()).Take(10))
            {
                sb.AppendLine("- " + Fmt.Bytes(k.Size) + " · " + k.Name);
                foreach (var k2 in (k.Kids ?? new List<DNode>()).Take(4)) sb.AppendLine("  - " + Fmt.Bytes(k2.Size) + " · " + k2.Name);
            }
            if (ix.Root.DirectFiles > 0) sb.AppendLine("- " + Fmt.Bytes(ix.Root.FilesSize) + " · archivos sueltos en la raíz (" + ix.Root.DirectFiles + ")");
            sb.AppendLine();
            sb.AppendLine("Tipos de archivo que más ocupan:");
            sb.AppendLine("| Tipo | En disco | Archivos |"); sb.AppendLine("|---|---:|---:|");
            foreach (var e in ix.Ext.Values.OrderByDescending(x => x.Alloc).Take(12)) sb.AppendLine("| ." + (e.Ext.Length == 0 ? "(sin extensión)" : e.Ext) + " | " + Fmt.Bytes(e.Alloc) + " | " + e.Count.ToString("N0") + " |");
            sb.AppendLine();
            sb.AppendLine("Archivos más grandes:");
            foreach (var f in ix.TopFiles.Take(20)) sb.AppendLine("- " + Fmt.Bytes(f.Size) + " · " + f.Path);
            sb.AppendLine();
        }

        // Saca datos personales: nombre del equipo, usuario, cuentas de correo y la ruta del perfil
        public static string Anonymize(string t)
        {
            string user = Environment.UserName, pc = Environment.MachineName;
            t = Regex.Replace(t, Regex.Escape(Env.Profile), Env.SysDrive + "Users\\usuario", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"[\w.+-]+@[\w-]+(\.[\w-]+)+", "[correo]");
            if (pc.Length >= 3) t = Regex.Replace(t, Regex.Escape(pc), "EQUIPO", RegexOptions.IgnoreCase);
            if (user.Length >= 3) t = Regex.Replace(t, @"(?<![\p{L}\d])" + Regex.Escape(user) + @"(?![\p{L}\d])", "usuario", RegexOptions.IgnoreCase);
            return t;
        }
    }
}
