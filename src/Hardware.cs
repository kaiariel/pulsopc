using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PulsoPC
{
    class PhysDisk
    {
        public string Name = "", Media = "?", Bus = "", Health = "Desconocido", DeviceId = "";
        public int HealthCode = -1; public long Size; public bool IsSystem, Removable;
        public string Label { get { return (Media == "?" ? "Disco" : Media) + (Bus == "NVMe" ? " NVMe" : ""); } }
    }
    class AvInfo { public string Name; public bool Enabled; }

    static class Hw
    {
        static string Str(object o) { return o == null ? "" : o.ToString(); }
        static string CleanName(string n)
        {
            n = (n ?? "").Trim();
            while (n.Length > 5 && n.StartsWith("NVMe", StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(n[4])) n = n.Substring(4).TrimStart();
            return n;
        }
        // Sin permisos de administrador: relaciona C: con su disco fisico mediante Win32
        static string SystemDiskIndexFallback()
        {
            try
            {
                string dev = Env.SysDrive.Substring(0, 2);
                using (var s = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + dev + "'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                    foreach (ManagementObject p in s.Get())
                        using (var s2 = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + Str(p["DeviceID"]) + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                            foreach (ManagementObject d in s2.Get()) return Str(d["Index"]);
            }
            catch { }
            return null;
        }
        static long Lng(object o) { try { return o == null ? -1 : Convert.ToInt64(o); } catch { return -1; } }

        static string BusName(int b)
        {
            switch (b)
            {
                case 3: return "ATA"; case 7: return "USB"; case 8: return "RAID"; case 10: return "SAS"; case 11: return "SATA";
                case 12: return "SD"; case 13: return "MMC"; case 14: return "Virtual"; case 16: return "Espacios"; case 17: return "NVMe";
                default: return "";
            }
        }

        static int SystemDiskNumber()
        {
            try
            {
                char letter = char.ToUpperInvariant(Env.SysDrive[0]);
                var scope = new ManagementScope(Wmi.Scope("root/Microsoft/Windows/Storage")); scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, DiskNumber FROM MSFT_Partition")))
                    foreach (ManagementObject m in s.Get())
                    {
                        object o = m["DriveLetter"]; char c = '\0';
                        if (o is char) c = (char)o; else if (o != null) c = (char)Convert.ToUInt16(o);
                        if (char.ToUpperInvariant(c) == letter) return Convert.ToInt32(m["DiskNumber"]);
                    }
            }
            catch { }
            return -1;
        }

        public static List<PhysDisk> Disks()
        {
            var res = new List<PhysDisk>();
            try
            {
                int sys = SystemDiskNumber();
                var scope = new ManagementScope(Wmi.Scope("root/Microsoft/Windows/Storage")); scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk")))
                    foreach (ManagementObject m in s.Get())
                    {
                        var d = new PhysDisk();
                        d.Name = CleanName(Str(m["FriendlyName"])); d.DeviceId = Str(m["DeviceId"]); d.Size = Lng(m["Size"]);
                        int media = (int)Lng(m["MediaType"]), bus = (int)Lng(m["BusType"]); long spin = Lng(m["SpindleSpeed"]);
                        d.Bus = BusName(bus);
                        d.Media = media == 3 ? "HDD" : media == 4 ? "SSD" : bus == 17 ? "SSD" : (spin > 0 && spin < 4294967295L) ? "HDD" : "?";
                        d.HealthCode = (int)Lng(m["HealthStatus"]);
                        d.Health = d.HealthCode == 0 ? "Correcto" : d.HealthCode == 1 ? "Advertencia" : d.HealthCode == 2 ? "Con fallos" : "Desconocido";
                        d.Removable = bus == 7 || bus == 12 || bus == 13;
                        d.IsSystem = sys >= 0 && d.DeviceId == sys.ToString();
                        res.Add(d);
                    }
                if (!res.Any(x => x.IsSystem))
                {
                    var internals = res.Where(x => !x.Removable).ToList();
                    if (internals.Count == 1) internals[0].IsSystem = true;
                }
            }
            catch { }
            if (res.Count == 0)
            {
                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT Model, Size, Status, Index, InterfaceType, MediaType FROM Win32_DiskDrive"))
                        foreach (ManagementObject m in s.Get())
                        {
                            var d = new PhysDisk(); string raw = Str(m["Model"]); d.Name = CleanName(raw); d.Size = Lng(m["Size"]); d.DeviceId = Str(m["Index"]);
                            string model = raw.ToLowerInvariant();
                            if (model.Contains("nvme")) { d.Bus = "NVMe"; d.Media = "SSD"; }
                            else if (model.Contains("ssd") || model.Contains("solid")) d.Media = "SSD";
                            d.Removable = Str(m["InterfaceType"]).ToUpperInvariant() == "USB" || Str(m["MediaType"]).ToLowerInvariant().Contains("removable");
                            d.Health = Str(m["Status"]) == "OK" ? "Correcto" : "Desconocido"; d.HealthCode = d.Health == "Correcto" ? 0 : -1;
                            res.Add(d);
                        }
                    string sysIdx = SystemDiskIndexFallback();
                    foreach (var x in res) if (x.DeviceId == sysIdx) x.IsSystem = true;
                    var internals = res.Where(x => !x.Removable && !res.Any(y => y.IsSystem)).ToList();
                    if (internals.Count == 1) internals[0].IsSystem = true;
                }
                catch { }
            }
            return res;
        }

        // true = el disco anuncia fallo inminente (SMART); null = no se pudo leer
        public static bool? SmartFailing()
        {
            try
            {
                var scope = new ManagementScope(Wmi.Scope("root/wmi")); scope.Connect();
                bool any = false, failing = false;
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus")))
                    foreach (ManagementObject m in s.Get()) { any = true; if (Convert.ToBoolean(m["PredictFailure"])) failing = true; }
                return any ? (bool?)failing : null;
            }
            catch { return null; }
        }

        public static List<AvInfo> Antivirus()
        {
            var res = new List<AvInfo>();
            try
            {
                var scope = new ManagementScope(Wmi.Scope("root/SecurityCenter2")); scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT displayName, productState FROM AntiVirusProduct")))
                    foreach (ManagementObject m in s.Get())
                    {
                        long st = Lng(m["productState"]);
                        res.Add(new AvInfo { Name = Str(m["displayName"]), Enabled = st >= 0 && ((st >> 12) & 0xF) == 1 });
                    }
            }
            catch { }
            return res;
        }

        public static string Gpu()
        {
            try
            {
                var names = new List<string>();
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject m in s.Get()) { string n = Str(m["Name"]); if (n.Length > 0) names.Add(n); }
                return string.Join(" + ", names.ToArray());
            }
            catch { return ""; }
        }

        public static string CpuName()
        {
            string n = Reg.Get(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "") as string;
            return Regex.Replace((n ?? "").Trim(), @"\s+", " ");
        }

        public static string OsName()
        {
            const string k = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string prod = Reg.Get(Registry.LocalMachine, k, "ProductName", "Windows") as string ?? "Windows";
            string dv = Reg.Get(Registry.LocalMachine, k, "DisplayVersion", null) as string ?? Reg.Get(Registry.LocalMachine, k, "ReleaseId", "") as string ?? "";
            string build = Reg.Get(Registry.LocalMachine, k, "CurrentBuild", "") as string ?? "";
            int b; if (int.TryParse(build, out b) && b >= 22000) prod = prod.Replace("Windows 10", "Windows 11");
            return (prod + " " + dv).Trim() + " (compilación " + build + ")";
        }

        public static bool IsWin11 { get { int b; return int.TryParse(Reg.Get(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "0") as string, out b) && b >= 22000; } }

        // tipo: "ahorro", "equilibrado", "alto" o "otro"
        public static string PowerPlan(out string guid, out string name)
        {
            guid = ""; name = "";
            var r = Sh.Run("powercfg", "/getactivescheme", 8000);
            var m = Regex.Match(r.Output, "([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
            if (m.Success) guid = m.Groups[1].Value.ToLowerInvariant();
            var n = Regex.Match(r.Output, @"\(([^)]+)\)");
            if (n.Success) name = n.Groups[1].Value;
            if (guid == "a1841308-3541-4fab-bc81-f71556f20b4a") return "ahorro";
            if (guid == "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" || guid == "e9a42b02-d5df-448d-aa00-03f14749eb61") return "alto";
            if (guid == "381b4222-f694-41f0-9685-ff5bb260df2e") return "equilibrado";
            string l = name.ToLowerInvariant();
            if (Regex.IsMatch(l, "alto rendimiento|high performance|rendimiento m.ximo|ultimate")) return "alto";
            if (Regex.IsMatch(l, "ahorro|saver")) return "ahorro";
            if (Regex.IsMatch(l, "equilibrado|balanced")) return "equilibrado";
            return "otro";
        }

        static bool KeyExists(RegistryKey r, string p)
        {
            try { using (var k = r.OpenSubKey(p)) return k != null; } catch { return false; }
        }

        public static bool PendingReboot()
        {
            return KeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")
                || KeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        }

        public static bool FastStartupOn()
        {
            return Reg.GetInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0) == 1;
        }

        public static bool HasBattery
        {
            get { try { return (SystemInformation.PowerStatus.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.Unknown; } catch { return false; } }
        }

        public static TimeSpan Uptime { get { return TimeSpan.FromMilliseconds((double)Native.GetTickCount64()); } }
    }

    // Lecturas en vivo de CPU, memoria y disco
    class LiveMetrics
    {
        ulong pi, pk, pu; bool has; PerformanceCounter disk;
        public double Cpu; public double Disk = -1;
        public int RamLoad; public ulong RamTotal, RamAvail, CommitTotal, CommitAvail;

        public void InitDisk()
        {
            try { disk = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); disk.NextValue(); }
            catch { disk = null; }
        }

        public void Update()
        {
            Native.FILETIME i, k, u;
            if (Native.GetSystemTimes(out i, out k, out u))
            {
                ulong ii = i.Value, kk = k.Value, uu = u.Value;
                if (has)
                {
                    ulong tot = (kk - pk) + (uu - pu), idle = ii - pi;
                    Cpu = tot == 0 ? 0 : Math.Max(0, Math.Min(100, (tot - idle) * 100.0 / tot));
                }
                pi = ii; pk = kk; pu = uu; has = true;
            }
            var m = new Native.MEMORYSTATUSEX();
            if (Native.GlobalMemoryStatusEx(m))
            {
                RamLoad = (int)m.dwMemoryLoad; RamTotal = m.ullTotalPhys; RamAvail = m.ullAvailPhys;
                CommitTotal = m.ullTotalPageFile; CommitAvail = m.ullAvailPageFile;
            }
            if (disk != null) { try { Disk = Math.Max(0, Math.Min(100, 100 - disk.NextValue())); } catch { } }
        }
    }

    class ProcInfo { public int Pid; public string Name; public ulong Cpu; public ulong Io; public long Ws; }
    class ProcRow
    {
        public string Name; public int Pid; public double Cpu; public long Ws; public double IoBps; public int Count = 1;
        public List<int> Pids = new List<int>();
    }

    static class Procs
    {
        public static Dictionary<int, ProcInfo> Snapshot()
        {
            var d = new Dictionary<int, ProcInfo>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0) continue;
                    var pi = new ProcInfo { Pid = p.Id, Name = p.ProcessName };
                    try { pi.Ws = p.WorkingSet64; } catch { }
                    IntPtr h = Native.OpenProcess(0x1000, false, p.Id);
                    if (h != IntPtr.Zero)
                    {
                        try
                        {
                            Native.FILETIME c, e, k, u; Native.IO_COUNTERS io;
                            if (Native.GetProcessTimes(h, out c, out e, out k, out u)) pi.Cpu = k.Value + u.Value;
                            if (Native.GetProcessIoCounters(h, out io)) pi.Io = io.ReadTransferCount + io.WriteTransferCount;
                        }
                        finally { Native.CloseHandle(h); }
                    }
                    d[pi.Pid] = pi;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return d;
        }

        public static List<ProcRow> Rows(Dictionary<int, ProcInfo> prev, Dictionary<int, ProcInfo> cur, double dt, bool group)
        {
            int cores = Math.Max(1, Environment.ProcessorCount);
            if (dt <= 0.05) dt = 0.05;
            var list = new List<ProcRow>();
            foreach (var kv in cur)
            {
                var c = kv.Value; ProcInfo p; double cpu = 0, io = 0;
                if (prev != null && prev.TryGetValue(kv.Key, out p) && p.Name == c.Name)
                {
                    if (c.Cpu >= p.Cpu) cpu = (c.Cpu - p.Cpu) / 1e7 / dt / cores * 100.0;
                    if (c.Io >= p.Io) io = (c.Io - p.Io) / dt;
                }
                var row = new ProcRow { Name = c.Name, Pid = c.Pid, Cpu = Math.Min(100, cpu), Ws = c.Ws, IoBps = io };
                row.Pids.Add(c.Pid);
                list.Add(row);
            }
            if (!group) return list;
            return list.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => new ProcRow
            {
                Name = g.Key, Pid = g.First().Pid, Cpu = g.Sum(x => x.Cpu), Ws = g.Sum(x => x.Ws), IoBps = g.Sum(x => x.IoBps),
                Count = g.Count(), Pids = g.SelectMany(x => x.Pids).ToList()
            }).ToList();
        }

        static readonly Dictionary<string, string> Notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "System", "Núcleo de Windows" }, { "svchost", "Servicios de Windows (varios)" }, { "MsMpEng", "Antivirus de Windows analizando" },
            { "SearchIndexer", "Indexador de búsqueda de Windows" }, { "SearchHost", "Búsqueda del menú Inicio" }, { "TiWorker", "Windows Update instalando" },
            { "TrustedInstaller", "Windows Update instalando" }, { "CompatTelRunner", "Telemetría de compatibilidad (se puede detener)" },
            { "GoogleDriveFS", "Google Drive sincronizando archivos" }, { "OneDrive", "OneDrive sincronizando archivos" }, { "Dropbox", "Dropbox sincronizando" },
            { "chrome", "Navegador Chrome" }, { "msedge", "Navegador Edge" }, { "firefox", "Navegador Firefox" }, { "brave", "Navegador Brave" },
            { "explorer", "Explorador y barra de tareas" }, { "dwm", "Compositor de ventanas" }, { "vmmem", "Máquina virtual / WSL / Docker usando RAM" },
            { "vmmemWSL", "WSL usando RAM" }, { "Docker Desktop", "Docker" }, { "node", "Node.js (servidor de desarrollo o herramienta)" },
            { "MicrosoftEdgeUpdate", "Actualizador de Edge" }, { "GoogleUpdate", "Actualizador de Google" }, { "wsappx", "Tienda de Microsoft" },
            { "Memory Compression", "Compresión de memoria de Windows" }, { "Registry", "Registro de Windows" }, { "audiodg", "Audio de Windows" },
            { "WmiPrvSE", "Consultas del sistema (WMI)" }, { "SgrmBroker", "Protección del sistema" }, { "OfficeClickToRun", "Actualizaciones de Office" },
            { "Teams", "Microsoft Teams" }, { "ms-teams", "Microsoft Teams" }, { "Discord", "Discord" }, { "Spotify", "Spotify" }, { "claude", "Claude" },
            { "code", "Visual Studio Code" }, { "SecurityHealthService", "Seguridad de Windows" }, { "PulsoPC", "Esta aplicación" }
        };
        public static string Note(string name) { string n; return Notes.TryGetValue(name, out n) ? n : ""; }

        public static readonly HashSet<string> Critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "Registry", "Memory Compression", "dwm", "fontdrvhost", "lsaiso", "SecurityHealthService"
        };
    }

    class SampleResult { public double Cpu; public double Disk = -1; public List<ProcRow> Rows = new List<ProcRow>(); }

    static class Sampler
    {
        // Mide CPU, disco y procesos durante unos milisegundos
        public static SampleResult Measure(int ms)
        {
            var live = new LiveMetrics(); live.InitDisk();
            var a = Procs.Snapshot(); live.Update();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int slices = Math.Max(1, ms / 500); double cpu = 0, disk = 0; int n = 0, nd = 0;
            for (int i = 0; i < slices; i++)
            {
                System.Threading.Thread.Sleep(ms / slices); live.Update();
                cpu += live.Cpu; n++; if (live.Disk >= 0) { disk += live.Disk; nd++; }
            }
            var b = Procs.Snapshot();
            return new SampleResult { Cpu = n > 0 ? cpu / n : 0, Disk = nd > 0 ? disk / nd : -1, Rows = Procs.Rows(a, b, sw.Elapsed.TotalSeconds, true) };
        }
    }
}
