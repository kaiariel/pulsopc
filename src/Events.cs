using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;

namespace PulsoPC
{
    class EvtRow { public DateTime Time; public string Provider = "", Level = "", Message = ""; public int Id; }

    class EvtSummary
    {
        public int Total, Disk, Whea, KernelPower; public bool Capped;
        public List<KeyValuePair<string, int>> Top = new List<KeyValuePair<string, int>>();
    }

    static class Events
    {
        const int Cap = 600;

        public static List<EvtRow> Read(int days, int max, bool messages, string extra = "")
        {
            var res = new List<EvtRow>();
            try
            {
                long ms = days * 86400000L;
                string q = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= " + ms + "]" + extra + "]]";
                var query = new EventLogQuery("System", PathType.LogName, q) { ReverseDirection = true };
                using (var r = new EventLogReader(query))
                {
                    EventRecord ev;
                    while (res.Count < max && (ev = r.ReadEvent()) != null)
                    {
                        using (ev)
                        {
                            var row = new EvtRow { Time = ev.TimeCreated ?? DateTime.MinValue, Provider = ev.ProviderName ?? "", Id = ev.Id, Level = ev.Level == 1 ? "Crítico" : "Error" };
                            if (messages)
                            {
                                string m = null;
                                try { m = ev.FormatDescription(); } catch { }
                                if (string.IsNullOrWhiteSpace(m)) m = "(sin descripción disponible)";
                                m = m.Replace("\r", " ").Replace("\n", " ").Trim();
                                row.Message = m.Length > 400 ? m.Substring(0, 400) + "..." : m;
                            }
                            res.Add(row);
                        }
                    }
                }
            }
            catch { }
            return res;
        }

        // Resumen rapido: consultas dirigidas para disco, hardware y apagados, mas un muestreo general
        public static EvtSummary Summary(int days)
        {
            var s = new EvtSummary();
            var general = Read(days, Cap, false);
            s.Total = general.Count; s.Capped = general.Count >= Cap;
            s.Top = general.GroupBy(e => e.Provider).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).Where(k => k.Value >= 5).OrderByDescending(k => k.Value).Take(3).ToList();
            string diskX = " and (Provider[@Name='disk'] or Provider[@Name='Ntfs'] or Provider[@Name='stornvme'] or Provider[@Name='storahci'] or Provider[@Name='volmgr'] or Provider[@Name='partmgr'] or Provider[@Name='iaStorAC'] or Provider[@Name='iaStorA'])";
            s.Disk = Read(days, 200, false, diskX).Count;
            s.Whea = Read(days, 200, false, " and Provider[@Name='Microsoft-Windows-WHEA-Logger']").Count;
            s.KernelPower = Read(days, 200, false, " and Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41").Count;
            return s;
        }
    }
}
