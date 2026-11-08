using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace PulsoPC
{
    // Una muestra del monitor de trabas (cada 2 segundos)
    class HangSample
    {
        public DateTime T;
        public int RamFreeMb, CommitPct, PagesIn, Cpu, DiskBusy, DiskQueue, FgMs, BarMs, DriveWarn, GapMs;
        public bool FgHung, BarHung, Mark;
        public string FgApp = "", Keys = "", Capture = "", TopRead = "", TopCpu = "";
        public List<string> Flags = new List<string>();
        public bool Odd { get { return Flags.Count > 0; } }
    }

    class HangVerdict
    {
        public Lvl Level = Lvl.Ok; public string Title = ""; public List<string> Lines = new List<string>();
        public DateTime At;
    }

    static class HangMonitor
    {
        public const int IntervalMs = 2000;
        const int Keep = 3600; // 2 horas
        static readonly object L = new object();
        static readonly List<HangSample> ring = new List<HangSample>();
        static Thread th; static volatile bool stop;
        public static bool Running { get { return th != null && th.IsAlive; } }
        public static DateTime StartedAt;
        public static int Count;
        public static HangVerdict LastVerdict;
        public static event Action<HangSample> Sampled;

        public static string CsvPath(DateTime d) { return Path.Combine(Log.Dir, "trabas-" + d.ToString("yyyy-MM-dd") + ".csv"); }

        public static void Start()
        {
            if (Running) return;
            stop = false; StartedAt = DateTime.Now; Count = 0;
            th = new Thread(Loop) { IsBackground = true, Name = "Monitor de trabas", Priority = ThreadPriority.AboveNormal };
            th.Start();
            Log.Write("Monitor de trabas iniciado");
        }

        public static void Stop()
        {
            if (!Running) return;
            stop = true; Log.Write("Monitor de trabas detenido (" + Count + " muestras)");
        }

        public static List<HangSample> Recent(TimeSpan span)
        {
            var from = DateTime.Now - span;
            lock (L) return ring.Where(s => s.T >= from).ToList();
        }

        // "Se trabó recién": marca el momento y analiza los 90 segundos anteriores
        public static HangVerdict Mark()
        {
            var now = DateTime.Now;
            List<HangSample> win;
            lock (L) { win = ring.Where(s => s.T >= now.AddSeconds(-90)).ToList(); if (ring.Count > 0) ring[ring.Count - 1].Mark = true; }
            var v = Analyze(win, now);
            LastVerdict = v;
            Log.Write("Traba marcada a las " + now.ToString("HH:mm:ss") + ": " + v.Title);
            try { File.AppendAllText(CsvPath(now), Csv(new HangSample { T = now, Mark = true, Flags = { "MARCA: " + v.Title } }) + Environment.NewLine, Encoding.UTF8); } catch { }
            return v;
        }

        // ---- muestreo ----

        static void Loop()
        {
            var live = new LiveMetrics(); live.InitDisk();
            PerformanceCounter pages = Counter("Memory", "Pages Input/sec", ""), queue = Counter("PhysicalDisk", "Current Disk Queue Length", "_Total");
            string driveProv = DriveProvider();
            Dictionary<int, ProcInfo> prev = Procs.Snapshot(); var swPrev = Stopwatch.StartNew();
            DateTime lastDrive = DateTime.Now, lastT = DateTime.MinValue;
            live.Update();
            while (!stop)
            {
                Thread.Sleep(IntervalMs);
                if (stop) break;
                var s = new HangSample { T = DateTime.Now };
                try
                {
                    if (lastT != DateTime.MinValue) s.GapMs = (int)(s.T - lastT).TotalMilliseconds; lastT = s.T;
                    live.Update();
                    s.Cpu = (int)Math.Round(live.Cpu); s.DiskBusy = live.Disk < 0 ? -1 : (int)Math.Round(live.Disk);
                    s.RamFreeMb = (int)(live.RamAvail / 1048576);
                    s.CommitPct = live.CommitTotal == 0 ? 0 : (int)((live.CommitTotal - live.CommitAvail) * 100 / live.CommitTotal);
                    s.PagesIn = pages != null ? (int)Safe(pages) : -1;
                    s.DiskQueue = queue != null ? (int)Safe(queue) : -1;

                    var fg = W.GetForegroundWindow();
                    if (fg != IntPtr.Zero)
                    {
                        s.FgApp = ProcName(W.Pid(fg)); s.FgHung = W.IsHungAppWindow(fg); s.FgMs = W.Ping(fg, 2000);
                    }
                    var bar = W.FindWindow("Shell_TrayWnd", null);
                    if (bar != IntPtr.Zero) { s.BarHung = W.IsHungAppWindow(bar); s.BarMs = W.Ping(bar, 2000); } else s.BarMs = -1;
                    s.Keys = W.Pressed(); s.Capture = W.CaptureInfo(ProcName);

                    var cur = Procs.Snapshot(); double dt = swPrev.Elapsed.TotalSeconds; swPrev.Restart();
                    var rows = Procs.Rows(prev, cur, dt, true); prev = cur;
                    var rd = rows.OrderByDescending(r => r.IoBps).FirstOrDefault(); var cp = rows.OrderByDescending(r => r.Cpu).FirstOrDefault();
                    if (rd != null && rd.IoBps >= 1048576) s.TopRead = rd.Name + " " + (rd.IoBps / 1048576.0).ToString("0") + " MB/s";
                    if (cp != null && cp.Cpu >= 1) s.TopCpu = cp.Name + " " + Math.Round(cp.Cpu) + " %";

                    if (driveProv != null) { s.DriveWarn = DriveWarnings(driveProv, lastDrive); lastDrive = s.T; }
                    Classify(s);
                }
                catch (Exception ex) { s.Flags.Add("error del monitor: " + ex.Message); }

                lock (L) { ring.Add(s); if (ring.Count > Keep) ring.RemoveRange(0, ring.Count - Keep); }
                Count++;
                try { File.AppendAllText(CsvPath(s.T), (File.Exists(CsvPath(s.T)) ? "" : Header + Environment.NewLine) + Csv(s) + Environment.NewLine, Encoding.UTF8); } catch { }
                var h = Sampled; if (h != null) { try { h(s); } catch { } }
            }
        }

        static string lastKeys = ""; static int keyRun;

        static void Classify(HangSample s)
        {
            if (s.BarHung || s.BarMs >= 1000) s.Flags.Add("barra de tareas sin responder (" + (s.BarHung ? "colgada" : s.BarMs + " ms") + ")");
            if (s.FgHung || s.FgMs >= 2000) s.Flags.Add("la ventana de " + s.FgApp + " no responde");
            if (s.RamFreeMb >= 0 && s.RamFreeMb < 400) s.Flags.Add("RAM casi agotada (" + s.RamFreeMb + " MB libres)");
            if (s.PagesIn >= 20000) s.Flags.Add("disco usado como memoria (" + s.PagesIn.ToString("N0") + " páginas/s)");
            if (s.Cpu >= 95) s.Flags.Add("CPU al máximo" + (s.TopCpu.Length > 0 ? " (" + s.TopCpu + ")" : ""));
            if (s.DiskBusy >= 90 && s.DiskQueue >= 8) s.Flags.Add("disco saturado" + (s.TopRead.Length > 0 ? " (" + s.TopRead + ")" : ""));
            if (s.DriveWarn >= 5) s.Flags.Add("Google Drive no respondió " + s.DriveWarn + " pedidos");
            // Una tecla cuenta como trabada si sigue apretada 3 muestras seguidas (unos 6 s)
            keyRun = s.Keys.Length == 0 ? 0 : (s.Keys == lastKeys ? keyRun + 1 : 1); lastKeys = s.Keys;
            if (keyRun >= 3) s.Flags.Add("tecla trabada: " + s.Keys);
            if (s.GapMs >= IntervalMs * 3) s.Flags.Add("hasta el monitor se frenó " + (s.GapMs / 1000.0).ToString("0") + " s");
        }

        // ---- análisis de una traba marcada ----

        public static HangVerdict Analyze(List<HangSample> w, DateTime at)
        {
            var v = new HangVerdict { At = at };
            if (w.Count < 3)
            {
                v.Level = Lvl.Info; v.Title = "No hay datos suficientes";
                v.Lines.Add("El monitor tiene que estar andando antes de la traba. Dejalo encendido y marcá la próxima.");
                return v;
            }
            int barBad = w.Count(s => s.BarHung || s.BarMs >= 1000), fgBad = w.Count(s => s.FgHung || s.FgMs >= 2000);
            int minRam = w.Min(s => s.RamFreeMb), maxPages = w.Max(s => s.PagesIn), cpuHigh = w.Count(s => s.Cpu >= 95);
            int diskHigh = w.Count(s => s.DiskBusy >= 90), drive = w.Sum(s => s.DriveWarn), maxGap = w.Max(s => s.GapMs);
            bool memory = minRam < 500 || maxPages >= 20000;
            Func<IEnumerable<HangSample>, Func<HangSample, string>, string> mostly = (ss, f) =>
                ss.Select(f).Where(x => !string.IsNullOrEmpty(x)).GroupBy(x => x.Split(' ')[0]).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";

            if (memory)
            {
                var worst = w.OrderBy(s => s.RamFreeMb).First();
                v.Level = Lvl.Bad; v.Title = "Faltó memoria";
                v.Lines.Add("La RAM libre bajó a " + minRam + " MB y Windows tuvo que traer hasta " + Math.Max(0, maxPages).ToString("N0") + " páginas por segundo del disco (lo normal es menos de 100).");
                string reader = mostly(w.Where(s => s.PagesIn >= 5000 || s.RamFreeMb < 800), s => s.TopRead);
                v.Lines.Add("En ese momento estaba al frente: " + (worst.FgApp.Length > 0 ? worst.FgApp : "?") + (reader.Length > 0 ? ". El que más leía del disco: " + reader + "." : "."));
                v.Lines.Add("Qué hacer: cerrar las apps que más ocupan (navegadores con muchas pestañas, apps de IA de escritorio) antes de abrir otra grande.");
            }
            if (barBad > 0)
            {
                if (v.Level < Lvl.Bad) v.Level = Lvl.Bad;
                if (v.Title.Length == 0) v.Title = "Se colgó el Explorador (la barra de tareas)";
                v.Lines.Add("La barra de tareas no respondió en " + barBad + " de " + w.Count + " muestras (" + (barBad * IntervalMs / 1000) + " s). Mientras está colgada no se puede cambiar de ventana.");
                if (!memory && cpuHigh == 0 && diskHigh < 2)
                    v.Lines.Add("La memoria, la CPU y el disco estaban normales: no fue falta de recursos. Suelen ser extensiones del Explorador (Google Drive, Adobe, antivirus), los Widgets o miniaturas de carpetas en la nube.");
            }
            if (fgBad > 0 && barBad == 0)
            {
                string app = mostly(w.Where(s => s.FgHung || s.FgMs >= 2000), s => s.FgApp);
                if (v.Level < Lvl.Warn) v.Level = Lvl.Warn;
                if (v.Title.Length == 0) v.Title = "Se colgó " + (app.Length > 0 ? app : "la ventana activa");
                v.Lines.Add("La ventana de " + app + " no respondió durante " + (fgBad * IntervalMs / 1000) + " s. El resto del sistema respondía.");
            }
            if (cpuHigh >= 2)
            {
                if (v.Level < Lvl.Warn) v.Level = Lvl.Warn; if (v.Title.Length == 0) v.Title = "CPU al máximo";
                v.Lines.Add("La CPU estuvo al 95 % o más en " + cpuHigh + " muestras. Lo que más la usaba: " + mostly(w.Where(s => s.Cpu >= 95), s => s.TopCpu) + ".");
            }
            if (diskHigh >= 2 && !memory)
            {
                if (v.Level < Lvl.Warn) v.Level = Lvl.Warn; if (v.Title.Length == 0) v.Title = "Disco saturado";
                v.Lines.Add("El disco estuvo al 90 % o más en " + diskHigh + " muestras. Lo que más leía: " + mostly(w.Where(s => s.DiskBusy >= 90), s => s.TopRead) + ".");
            }
            if (drive > 0)
            {
                if (v.Level < Lvl.Warn) v.Level = Lvl.Warn; if (v.Title.Length == 0) v.Title = "Google Drive no respondió";
                v.Lines.Add("Google Drive dejó sin respuesta " + drive + " pedidos a la unidad G:. Cualquier ventana que estuviera leyendo de G: se queda esperando.");
            }
            if (maxGap >= IntervalMs * 3)
            {
                if (v.Level < Lvl.Warn) v.Level = Lvl.Warn; if (v.Title.Length == 0) v.Title = "Se frenó todo el sistema";
                v.Lines.Add("Hasta el monitor se frenó " + (maxGap / 1000.0).ToString("0") + " s: el equipo entero estuvo trabado, no una sola app.");
            }
            var keys = w.Where(s => s.Flags.Any(f => f.StartsWith("tecla trabada"))).Select(s => s.Keys).FirstOrDefault();
            if (keys != null) v.Lines.Add("Windows registró la tecla " + keys + " como apretada. Si no la estabas tocando, quedó trabada: Ctrl+Alt+Supr la suelta.");
            if (v.Title.Length == 0)
            {
                v.Level = Lvl.Info; v.Title = "No se ve nada raro en el último minuto y medio";
                v.Lines.Add("Memoria, CPU, disco, barra de tareas y ventana activa respondieron bien. Si la traba fue antes, marcá más cerca del momento.");
            }
            return v;
        }

        // ---- utilidades ----

        const string Header = "Hora;RAM_libre_MB;Commit_pct;Lecturas_pagina_s;CPU_pct;Disco_pct;Cola_disco;Ventana;Ventana_colgada;Ventana_ms;Barra_ms;Barra_colgada;Drive_avisos;Teclas;Captura;Mas_lectura;Mas_CPU;Hueco_ms;Marca;Que_paso";

        static string Csv(HangSample s)
        {
            Func<string, string> c = x => (x ?? "").Replace(";", ",");
            return string.Join(";", new[] { s.T.ToString("HH:mm:ss"), s.RamFreeMb.ToString(), s.CommitPct.ToString(), s.PagesIn.ToString(), s.Cpu.ToString(), s.DiskBusy.ToString(), s.DiskQueue.ToString(),
                c(s.FgApp), s.FgHung ? "1" : "0", s.FgMs.ToString(), s.BarMs.ToString(), s.BarHung ? "1" : "0", s.DriveWarn.ToString(), c(s.Keys), c(s.Capture), c(s.TopRead), c(s.TopCpu), s.GapMs.ToString(), s.Mark ? "1" : "0", c(string.Join(" | ", s.Flags.ToArray())) });
        }

        static PerformanceCounter Counter(string cat, string name, string inst)
        {
            try { var p = new PerformanceCounter(cat, name, inst, true); p.NextValue(); return p; } catch { return null; }
        }
        static float Safe(PerformanceCounter p) { try { return p.NextValue(); } catch { return -1; } }

        static readonly Dictionary<int, string> names = new Dictionary<int, string>();
        static string ProcName(int pid)
        {
            string n;
            if (names.TryGetValue(pid, out n)) return n;
            try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName; } catch { n = "?"; }
            if (names.Count > 2000) names.Clear();
            names[pid] = n; return n;
        }

        // El controlador de Google Drive se registra con un número de versión (googledrivefs31931)
        static string DriveProvider()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EventLog\System"))
                    return k == null ? null : k.GetSubKeyNames().FirstOrDefault(n => n.StartsWith("googledrivefs", StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        static int DriveWarnings(string prov, DateTime since)
        {
            try
            {
                long ms = Math.Max(1000, (long)(DateTime.Now - since).TotalMilliseconds);
                string q = "*[System[Provider[@Name='" + prov + "'] and Level=3 and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
                int n = 0;
                using (var r = new EventLogReader(new EventLogQuery("System", PathType.LogName, q)))
                {
                    EventRecord ev; while (n < 500 && (ev = r.ReadEvent()) != null) using (ev) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // Funciones de ventanas y teclado de Windows
        static class W
        {
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
            [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
            [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
            [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
            [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
            [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
            [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

            public static int Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return (int)p; }

            // Milisegundos que tarda la ventana en procesar un mensaje vacío (tope: timeout)
            public static int Ping(IntPtr h, int timeout)
            {
                IntPtr r; var sw = Stopwatch.StartNew();
                var ok = SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 0x2, (uint)timeout, out r);
                return ok == IntPtr.Zero ? timeout : (int)sw.ElapsedMilliseconds;
            }

            public static string Pressed()
            {
                var l = new List<string>();
                int[] vk = { 0x11, 0x12, 0x10, 0x5B, 0x5C }; string[] nm = { "Ctrl", "Alt", "Shift", "Win", "Win" };
                for (int i = 0; i < vk.Length; i++) if ((GetAsyncKeyState(vk[i]) & 0x8000) != 0 && !l.Contains(nm[i])) l.Add(nm[i]);
                return string.Join("+", l.ToArray());
            }

            public static string CaptureInfo(Func<int, string> name)
            {
                var g = new GUITHREADINFO(); g.cbSize = Marshal.SizeOf(g);
                if (!GetGUIThreadInfo(0, ref g)) return "";
                var parts = new List<string>();
                if (g.hwndCapture != IntPtr.Zero) parts.Add("mouse capturado por " + name(Pid(g.hwndCapture)));
                if ((g.flags & 0x4) != 0) parts.Add("menú abierto");
                if ((g.flags & 0x2) != 0) parts.Add("arrastrando ventana");
                return string.Join(", ", parts.ToArray());
            }
        }
    }
}
