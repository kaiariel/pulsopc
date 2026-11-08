using System;
using System.Collections.Generic;
using System.Linq;

namespace PulsoPC
{
    static partial class DiagRules
    {
        static void Load(SysSnapshot s)
        {
            var rows = s.Sample.Rows;
            if (s.Sample.Cpu >= 85)
                s.Add(s.Sample.Cpu >= 95 ? Lvl.Bad : Lvl.Warn, "Procesador muy ocupado (" + Math.Round(s.Sample.Cpu) + " %)", "Lo que más usa la CPU: " + Diag.Top(rows, r => r.Cpu, 3, r => Math.Round(r.Cpu) + " %") + ".", "procesos", "Ver procesos");
            if (s.Sample.Disk >= 70)
                s.Add(s.Sample.Disk >= 90 ? Lvl.Bad : Lvl.Warn, "Disco saturado (" + Math.Round(s.Sample.Disk) + " % de actividad)", "Lo que más lo usa: " + Diag.Top(rows, r => r.IoBps, 3, r => (r.IoBps / 1048576.0).ToString("0.#") + " MB/s") + ". Con un disco mecánico esto vuelve todo lento.", "procesos", "Ver procesos");
            var heavy = rows.Where(r => r.IoBps >= 10 * 1048576.0 && !Procs.Critical.Contains(r.Name)).OrderByDescending(r => r.IoBps).FirstOrDefault();
            if (heavy != null && s.Sample.Disk < 70)
            {
                string note = Procs.Note(heavy.Name);
                s.Add(Lvl.Info, heavy.Name + " está usando mucho disco (" + (heavy.IoBps / 1048576.0).ToString("0") + " MB/s)",
                    (note.Length > 0 ? note + ". " : "") + "Si el equipo va lento, pausa o cierra este programa un rato y comprueba si mejora.", "procesos", "Ver procesos");
            }
        }

        static void Boot(SysSnapshot s, string plan, bool reboot)
        {
            if (s.StartupEnabled >= 16) s.Add(Lvl.Bad, s.StartupEnabled + " programas se inician con Windows", "Cada uno alarga el arranque y consume memoria. Deshabilita los que no necesites.", "inicio", "Revisar");
            else if (s.StartupEnabled >= 9) s.Add(Lvl.Warn, s.StartupEnabled + " programas se inician con Windows", "Deshabilitar los que no uses acelera el arranque.", "inicio", "Revisar");
            else s.Add(Lvl.Ok, "Pocos programas al inicio (" + s.StartupEnabled + ")", "");

            double days = s.Uptime.TotalDays;
            if (days >= 5 && s.FastStartup) s.Add(days >= 14 ? Lvl.Bad : Lvl.Warn, "Llevas " + (int)days + " días sin reiniciar de verdad", "Con el Inicio rápido activado, apagar no cierra Windows por completo. Usa Reiniciar o desactiva el Inicio rápido.", "optimizar", "Optimizar");
            else if (days >= 14) s.Add(Lvl.Warn, "Llevas " + (int)days + " días encendido sin reiniciar", "Reiniciar libera memoria y aplica las actualizaciones pendientes.");

            if (plan == "ahorro") s.Add(Lvl.Warn, "Plan de energía en modo ahorro", "Limita la velocidad del procesador. Equilibrado o Alto rendimiento mejoran la respuesta.", "optimizar", "Optimizar");
            if (reboot) s.Add(Lvl.Warn, "Hay un reinicio pendiente", "Windows terminó de instalar algo y espera un reinicio. Mientras tanto puede ir más lento.");
            var avOn = s.Av.Where(a => a.Enabled).ToList();
            if (avOn.Count >= 2) s.Add(Lvl.Bad, "Hay varios antivirus activos a la vez", string.Join(", ", avOn.Select(a => a.Name).ToArray()) + ". Compiten por el disco y la CPU: deja solo uno.");
            else if (avOn.Count == 1) s.Add(Lvl.Ok, "Antivirus activo: " + avOn[0].Name, "");
        }

        static void Events(SysSnapshot s, EvtSummary ev)
        {
            if (ev.Disk >= 3) s.Add(Lvl.Bad, "Errores de disco en los últimos 7 días (" + ev.Disk + ")", "Windows registró fallos de lectura o escritura. Respalda tus archivos y revisa el disco.", "reparar", "Revisar disco");
            if (ev.Whea > 0) s.Add(Lvl.Bad, "Errores de hardware registrados (" + ev.Whea + ")", "El procesador, la memoria o la placa reportaron errores. Puede ser calor o hardware defectuoso.", "eventos", "Ver eventos");
            if (ev.KernelPower >= 2) s.Add(Lvl.Warn, "Apagados inesperados (" + ev.KernelPower + " en 7 días)", "El equipo se apagó o reinició sin cerrar Windows. Suele ser calor, fuente de poder o batería.", "eventos", "Ver eventos");
            else if (ev.KernelPower == 1) s.Add(Lvl.Info, "Un apagado inesperado en los últimos 7 días", "El equipo se apagó o reinició sin cerrar Windows una vez. Si se repite, revisa la temperatura y la fuente de poder.", "eventos", "Ver eventos");
            int other = ev.Total - ev.Disk - ev.Whea - ev.KernelPower;
            if (other >= 25)
            {
                string top = string.Join(", ", ev.Top.Select(k => k.Key + " (" + k.Value + ")").ToArray());
                s.Add(Lvl.Info, (ev.Capped ? "Más de " + ev.Total : ev.Total.ToString()) + " errores en el visor de eventos esta semana", (top.Length > 0 ? "Los que más se repiten: " + top + "." : "Casi siempre son avisos de servicios y controladores."), "eventos", "Ver eventos");
            }
        }
    }
}
