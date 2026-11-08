using System;
using System.Linq;
using System.Text;

namespace PulsoPC
{
    static class Report
    {
        static string Tag(Lvl l)
        {
            switch (l) { case Lvl.Bad: return "[CRITICO]"; case Lvl.Warn: return "[AVISO]  "; case Lvl.Info: return "[INFO]   "; default: return "[OK]     "; }
        }

        public static string Build(SysSnapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PulsoPC - Informe del equipo");
            sb.AppendLine("Generado: " + s.Taken.ToString("yyyy-MM-dd HH:mm") + (s.Admin ? "  (como administrador)" : "  (sin permisos de administrador)"));
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("Salud estimada: " + s.Score + " / 100");
            sb.AppendLine();
            sb.AppendLine("EQUIPO");
            sb.AppendLine("  Nombre:     " + s.Pc + " (" + s.User + ")");
            sb.AppendLine("  Windows:    " + s.Os);
            sb.AppendLine("  Procesador: " + s.Cpu + " (" + s.Threads + " hilos)");
            sb.AppendLine("  Memoria:    " + Fmt.Bytes((long)s.RamTotal) + " (en uso: " + s.RamLoad + " %)");
            if (!string.IsNullOrEmpty(s.Gpu)) sb.AppendLine("  Gráfica:    " + s.Gpu);
            sb.AppendLine("  Encendido:  " + Fmt.Dur(s.Uptime) + (s.FastStartup ? " (Inicio rápido activado)" : ""));
            sb.AppendLine("  Energía:    " + s.PowerName);
            sb.AppendLine();
            sb.AppendLine("ALMACENAMIENTO");
            foreach (var d in s.Disks) sb.AppendLine("  " + d.Label + " " + d.Name + ", " + Fmt.Bytes(d.Size) + ", salud: " + d.Health + (d.IsSystem ? "  <- disco de Windows" : ""));
            foreach (var d in s.Drives) sb.AppendLine("  " + d.Letter + " libre " + Fmt.Bytes(d.Free) + " de " + Fmt.Bytes(d.Total) + " (" + Math.Round(d.FreePct) + " %)");
            sb.AppendLine();
            sb.AppendLine("HALLAZGOS");
            foreach (var f in s.Findings)
            {
                sb.AppendLine("  " + Tag(f.Level) + " " + f.Title);
                if (!string.IsNullOrEmpty(f.Detail)) sb.AppendLine("             " + f.Detail);
            }
            sb.AppendLine();
            sb.AppendLine("LO QUE MÁS CONSUME AHORA (CPU " + Math.Round(s.Sample.Cpu) + " %, disco " + (s.Sample.Disk < 0 ? "?" : Math.Round(s.Sample.Disk) + " %") + ")");
            foreach (var r in s.Sample.Rows.OrderByDescending(x => x.Cpu).Take(6))
                sb.AppendLine("  CPU " + Math.Round(r.Cpu, 1) + " %  " + r.Name + (r.Count > 1 ? " x" + r.Count : ""));
            foreach (var r in s.Sample.Rows.OrderByDescending(x => x.Ws).Take(6))
                sb.AppendLine("  RAM " + Fmt.Bytes(r.Ws) + "  " + r.Name + (r.Count > 1 ? " x" + r.Count : ""));
            if (s.PagesIn >= 0) sb.AppendLine("  Lecturas de página: " + s.PagesIn.ToString("N0") + "/s de promedio, " + s.PagesInMax.ToString("N0") + "/s de máximo (lo normal es menos de 100)");
            sb.AppendLine();
            if (s.Broken.Count > 0)
            {
                sb.AppendLine("SERVICIOS ROTOS POR DEPENDENCIAS");
                foreach (var b in s.Broken) sb.AppendLine("  " + b.SvcName + " (" + b.Svc + ") depende de " + b.DepName + " (" + b.Dep + "), que está deshabilitado." + (b.Failures > 0 ? " Fallos en 7 días: " + b.Failures + "." : ""));
                sb.AppendLine();
            }
            if (s.Dcom.Count > 0)
            {
                sb.AppendLine("APPS QUE NO ARRANCARON A TIEMPO (DCOM 10010, 7 días)");
                foreach (var d in s.Dcom) sb.AppendLine("  " + d.Count + " x  " + d.Friendly + "  [" + d.Clsid + (d.Raw.Length > 0 && d.Raw != d.Friendly ? ", " + d.Raw : "") + "]  último: " + d.Last.ToString("dd/MM HH:mm"));
                sb.AppendLine();
            }
            sb.AppendLine("PROGRAMAS QUE ARRANCAN CON WINDOWS (" + s.StartupEnabled + ")");
            foreach (var i in s.Startup.Where(x => x.Enabled)) sb.AppendLine("  " + i.Name + (string.IsNullOrEmpty(i.Publisher) ? "" : "  [" + i.Publisher + "]"));
            if (s.Av.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ANTIVIRUS");
                foreach (var a in s.Av) sb.AppendLine("  " + a.Name + (a.Enabled ? " (activo)" : " (inactivo)"));
            }
            return sb.ToString();
        }
    }
}
