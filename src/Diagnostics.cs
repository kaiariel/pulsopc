using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PulsoPC
{
    enum Lvl { Ok, Info, Warn, Bad }

    class Finding
    {
        public Lvl Level; public string Title = "", Detail = "", Page, ActionText;
        // Arreglo directo desde el Resumen: devuelve null si salió bien, "" si se canceló o un mensaje de error
        public Func<System.Windows.Forms.Control, string> Fix;
    }

    class DriveRow
    {
        public string Letter = "", Label = "", Format = ""; public long Total, Free; public bool IsSystem;
        public double FreePct { get { return Total <= 0 ? 0 : Free * 100.0 / Total; } }
    }

    class SysSnapshot
    {
        public string Pc = "", User = "", Os = "", Cpu = "", Gpu = "", PowerName = "";
        public int Threads, RamLoad, Score = 100, StartupEnabled, EventCount;
        public ulong RamTotal, RamAvail;
        public TimeSpan Uptime; public bool FastStartup, Laptop, Admin;
        public List<DriveRow> Drives = new List<DriveRow>();
        public List<PhysDisk> Disks = new List<PhysDisk>();
        public List<AvInfo> Av = new List<AvInfo>();
        public List<Finding> Findings = new List<Finding>();
        public List<StartupItem> Startup = new List<StartupItem>();
        public SampleResult Sample = new SampleResult();
        public List<BrokenService> Broken = new List<BrokenService>();
        public List<DcomIssue> Dcom = new List<DcomIssue>();
        public double PagesIn = -1, PagesInMax = -1;
        public int TempLogons, HiveUnloadFails, TempProfiles;
        public DateTime Taken = DateTime.Now;

        public void Add(Lvl l, string title, string detail, string page = null, string action = null)
        {
            Findings.Add(new Finding { Level = l, Title = title, Detail = detail, Page = page, ActionText = action });
        }

        public void Recompute()
        {
            int pen = 0;
            foreach (var f in Findings) { if (f.Level == Lvl.Bad) pen += 14; else if (f.Level == Lvl.Warn) pen += 6; }
            Score = Math.Max(12, 100 - Math.Min(pen, 85));
            Findings = Findings.OrderByDescending(f => (int)f.Level).ToList();
        }

        public string DiskSummary
        {
            get
            {
                var sys = Disks.FirstOrDefault(d => d.IsSystem) ?? Disks.FirstOrDefault();
                if (sys == null) return "No se pudo leer";
                return sys.Label + ", " + Fmt.Bytes(sys.Size);
            }
        }
    }

    static class Diag
    {
        static T Get<T>(Task<T> t, T def, int ms = 20000)
        {
            try { return t.Wait(ms) ? t.Result : def; } catch { return def; }
        }

        static bool? TrimEnabled()
        {
            var r = Sh.Run("fsutil", "behavior query DisableDeleteNotify", 8000);
            var m = Regex.Match(r.Output, @"DisableDeleteNotify\s*=\s*(\d)");
            if (!m.Success) return null;
            return m.Groups[1].Value == "0";
        }

        public static string Top(IEnumerable<ProcRow> rows, Func<ProcRow, double> key, int n, Func<ProcRow, string> fmt)
        {
            return string.Join(", ", rows.OrderByDescending(key).Take(n).Where(r => key(r) > 0).Select(r => r.Name + " (" + fmt(r) + ")").ToArray());
        }

        public static SysSnapshot Run(Action<string> status)
        {
            Action<string> st = m => { if (status != null) status(m); };
            var s = new SysSnapshot();
            st("Leyendo datos del equipo...");
            s.Pc = Environment.MachineName; s.User = Environment.UserName; s.Os = Hw.OsName(); s.Cpu = Hw.CpuName();
            s.Threads = Environment.ProcessorCount; s.Laptop = Hw.HasBattery; s.Admin = Program.IsAdmin;
            s.Uptime = Hw.Uptime; s.FastStartup = Hw.FastStartupOn();

            st("Midiendo CPU, memoria y disco...");
            var tPages = Task.Run(() => MemPressure.PagesIn(4, 600));
            s.Sample = Sampler.Measure(2500);
            var tSvc = Task.Run(() => ServiceHealth.Find(7));
            var tDcom = Task.Run(() => DcomHealth.Find(7));
            var tProf = Task.Run(() => ProfileHealth.Find(30));
            var tDisks = Task.Run(() => Hw.Disks());
            var tSmart = Task.Run(() => Hw.SmartFailing());
            var tAv = Task.Run(() => Hw.Antivirus());
            var tGpu = Task.Run(() => Hw.Gpu());
            var tEv = Task.Run(() => Events.Summary(7));
            var tStart = Task.Run(() => Startup.List());
            var tPower = Task.Run(() => { string g, n; string k = Hw.PowerPlan(out g, out n); return new[] { k, n }; });
            var tTrim = Task.Run(() => TrimEnabled());
            var tReboot = Task.Run(() => Hw.PendingReboot());

            var live = new LiveMetrics(); live.Update();
            s.RamTotal = live.RamTotal; s.RamAvail = live.RamAvail; s.RamLoad = live.RamLoad;
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    s.Drives.Add(new DriveRow { Letter = d.Name.Substring(0, 2), Label = d.VolumeLabel, Format = d.DriveFormat, Total = d.TotalSize, Free = d.AvailableFreeSpace, IsSystem = d.Name.Equals(Env.SysDrive, StringComparison.OrdinalIgnoreCase) });
                }
                catch { }
            }
            st("Revisando discos, seguridad y eventos...");
            s.Disks = Get(tDisks, new List<PhysDisk>());
            s.Av = Get(tAv, new List<AvInfo>());
            s.Gpu = Get(tGpu, "");
            s.Startup = Get(tStart, new List<StartupItem>());
            s.StartupEnabled = s.Startup.Count(x => x.Enabled);
            var power = Get(tPower, new[] { "otro", "" }); s.PowerName = power[1];
            var smart = Get(tSmart, (bool?)null); var trim = Get(tTrim, (bool?)null); bool reboot = Get(tReboot, false);
            var ev = Get(tEv, new EvtSummary(), 12000); s.EventCount = ev.Total;
            var pages = Get(tPages, new[] { -1.0, -1.0 }, 8000); s.PagesIn = pages[0]; s.PagesInMax = pages[1];
            s.Broken = Get(tSvc, new List<BrokenService>(), 15000);
            s.Dcom = Get(tDcom, new List<DcomIssue>(), 10000);
            var prof = Get(tProf, new int[3], 10000); s.TempLogons = prof[0]; s.HiveUnloadFails = prof[1]; s.TempProfiles = prof[2];
            st("Preparando resultados...");
            DiagRules.Build(s, power[0], smart, trim, reboot, ev);
            s.Recompute();
            return s;
        }
    }
}
