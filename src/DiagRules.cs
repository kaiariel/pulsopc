using System;
using System.Collections.Generic;
using System.Linq;

namespace PulsoPC
{
    // Reglas que convierten los datos medidos en hallazgos para el usuario
    static partial class DiagRules
    {
        static string G(double b) { return (b / 1073741824.0).ToString("0.#"); }

        public static void Build(SysSnapshot s, string plan, bool? smart, bool? trim, bool reboot, EvtSummary ev)
        {
            Memory(s);
            Storage(s, smart, trim);
            Load(s);
            Boot(s, plan, reboot);
            Events(s, ev);
            Paging(s);
            Services(s);
            Dcom(s);
            Profiles(s);
        }

        static void Memory(SysSnapshot s)
        {
            var rows = s.Sample.Rows;
            double ramGb = s.RamTotal / 1073741824.0;
            if (ramGb <= 4.5) s.Add(Lvl.Bad, "Memoria RAM justa (" + G(s.RamTotal) + " GB)", "Windows y el navegador ya ocupan buena parte. Ampliar la RAM es la mejora más efectiva si el equipo lo permite.");
            else if (ramGb <= 8.5) s.Add(Lvl.Warn, "Memoria RAM ajustada (" + G(s.RamTotal) + " GB)", "Con varios programas abiertos puede quedarse corta. Cierra lo que no uses o amplía la RAM.");
            else s.Add(Lvl.Ok, "Memoria RAM suficiente (" + G(s.RamTotal) + " GB)", "");
            string topRam = string.Join(", ", rows.Where(r => !Procs.Critical.Contains(r.Name)).OrderByDescending(r => r.Ws).Take(3).Select(AppMem).ToArray());
            if (s.RamLoad >= 90) s.Add(Lvl.Bad, "Memoria casi llena (" + s.RamLoad + " %)", "Los que más consumen: " + topRam + ". Cierra programas o revisa los procesos.", "procesos", "Ver procesos");
            else if (s.RamLoad >= 80) s.Add(Lvl.Warn, "Memoria muy usada (" + s.RamLoad + " %)", "Los que más consumen: " + topRam + ".", "procesos", "Ver procesos");
        }

        static void Storage(SysSnapshot s, bool? smart, bool? trim)
        {
            var sys = s.Disks.FirstOrDefault(d => d.IsSystem);
            if (sys != null)
            {
                if (sys.Media == "HDD") s.Add(Lvl.Bad, "El disco de Windows es mecánico (HDD)", "Es la causa de lentitud más común. Cambiarlo por un SSD hace que el equipo arranque y abra programas varias veces más rápido.");
                else if (sys.Media == "SSD") s.Add(Lvl.Ok, "Disco de Windows: " + sys.Label, "");
            }
            foreach (var d in s.Disks.Where(x => !x.Removable))
            {
                if (d.HealthCode == 2) s.Add(Lvl.Bad, "El disco " + d.Name + " tiene fallos", "Windows lo marca como no saludable. Haz una copia de seguridad cuanto antes.", "reparar", "Revisar disco");
                else if (d.HealthCode == 1) s.Add(Lvl.Warn, "El disco " + d.Name + " tiene una advertencia", "Windows detectó un problema de salud. Revisa el disco y respalda tus archivos.", "reparar", "Revisar disco");
            }
            if (smart == true) s.Add(Lvl.Bad, "El disco anuncia una falla inminente (SMART)", "Respalda tus archivos de inmediato: el disco puede dejar de funcionar en cualquier momento.");
            else if (sys != null && sys.HealthCode == 0 && smart == false) s.Add(Lvl.Ok, "Salud del disco correcta", "");
            if (sys != null && sys.Media == "SSD" && trim == false) s.Add(Lvl.Warn, "TRIM desactivado en el SSD", "Sin TRIM el SSD se vuelve más lento con el tiempo. Se corrige en Reparar > Optimizar unidades.", "reparar", "Optimizar");

            foreach (var d in s.Drives)
            {
                double freeGb = d.Free / 1073741824.0, pct = d.FreePct;
                if (d.IsSystem)
                {
                    if (freeGb < 8 || pct < 5) s.Add(Lvl.Bad, "Casi no queda espacio en " + d.Letter + " (" + Fmt.Bytes(d.Free) + " libres)", "Con tan poco espacio Windows se vuelve lento y no puede actualizarse. Libera espacio ya.", "limpieza", "Limpiar");
                    else if (pct < 10 || freeGb < 20) s.Add(Lvl.Warn, "Poco espacio libre en " + d.Letter + " (" + Fmt.Bytes(d.Free) + " libres)", "Conviene dejar al menos un 10 % libre para que Windows trabaje con holgura.", "limpieza", "Limpiar");
                    else s.Add(Lvl.Ok, "Espacio libre suficiente en " + d.Letter + " (" + Fmt.Bytes(d.Free) + ")", "");
                }
                else if (pct < 5 && d.Total > 0) s.Add(Lvl.Warn, "La unidad " + d.Letter + " está casi llena", "Solo queda " + Fmt.Bytes(d.Free) + " libre.", "espacio", "Ver espacio");
            }
        }
    }
}
