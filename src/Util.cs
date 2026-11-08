using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace PulsoPC
{
    static class Fmt
    {
        public static string Bytes(long b)
        {
            if (b < 0) b = 0;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            if (i == 0) return b + " B";
            return v.ToString(v >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + u[i];
        }
        public static string Dur(TimeSpan t)
        {
            if (t.TotalDays >= 1) return (int)t.TotalDays + " d " + t.Hours + " h";
            if (t.TotalHours >= 1) return t.Hours + " h " + t.Minutes + " min";
            return Math.Max(1, t.Minutes) + " min";
        }
        public static string Ago(DateTime d)
        {
            var t = DateTime.Now - d;
            if (t.TotalDays >= 365) return "hace " + (int)(t.TotalDays / 365) + (t.TotalDays >= 730 ? " años" : " año");
            if (t.TotalDays >= 60) return "hace " + (int)(t.TotalDays / 30) + " meses";
            if (t.TotalDays >= 1) return "hace " + (int)t.TotalDays + " días";
            return "hoy";
        }
    }

    static class Env
    {
        public static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        public static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        public static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        public static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        public static readonly string SysDrive = Path.GetPathRoot(Environment.SystemDirectory);
        public static string Downloads
        {
            get
            {
                string p = Native.KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"));
                return string.IsNullOrEmpty(p) ? Path.Combine(Profile, "Downloads") : p;
            }
        }
        public static string P(params string[] parts) { return Path.Combine(parts); }
    }

    static class Log
    {
        static readonly object L = new object();
        public static readonly string Dir = Path.Combine(Env.Local, "PulsoPC");
        public static string FilePath { get { return Path.Combine(Dir, "registro.txt"); } }
        public static void Write(string msg)
        {
            try
            {
                lock (L)
                {
                    Directory.CreateDirectory(Dir);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    // Guarda estados anteriores para poder revertir los cambios.
    static class Store
    {
        static Dictionary<string, string> d;
        static readonly object L = new object();
        static string FilePath { get { return Path.Combine(Log.Dir, "estado.ini"); } }
        static void Load()
        {
            if (d != null) return;
            d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch { }
        }
        public static string Get(string k, string def = null) { lock (L) { Load(); string v; return d.TryGetValue(k, out v) ? v : def; } }
        public static void Set(string k, string v)
        {
            lock (L)
            {
                Load();
                if (v == null) d.Remove(k); else d[k] = v.Replace("\r", " ").Replace("\n", " ");
                try { Directory.CreateDirectory(Log.Dir); File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value).ToArray()); } catch { }
            }
        }
    }

    static class Reg
    {
        public static object Get(RegistryKey root, string path, string name, object def = null)
        {
            try { using (var k = root.OpenSubKey(path)) { if (k == null) return def; return k.GetValue(name, def); } }
            catch { return def; }
        }
        public static int GetInt(RegistryKey root, string path, string name, int def)
        {
            object v = Get(root, path, name, null);
            if (v is int) return (int)v;
            int n; if (v != null && int.TryParse(v.ToString(), out n)) return n;
            return def;
        }
        public static void SetDword(RegistryKey root, string path, string name, int value)
        {
            using (var k = root.CreateSubKey(path)) k.SetValue(name, value, RegistryValueKind.DWord);
        }
        public static void SetString(RegistryKey root, string path, string name, string value)
        {
            using (var k = root.CreateSubKey(path)) k.SetValue(name, value, RegistryValueKind.String);
        }
        public static void Delete(RegistryKey root, string path, string name)
        {
            try { using (var k = root.OpenSubKey(path, true)) { if (k != null) k.DeleteValue(name, false); } } catch { }
        }
    }

    class RunResult { public int Exit; public string Output = ""; public bool TimedOut; }

    static class Sh
    {
        public static readonly Encoding Oem = GetOem();
        static Encoding GetOem()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }

        public static RunResult Run(string file, string args, int timeoutMs = 30000, Encoding enc = null)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = enc ?? Oem, StandardErrorEncoding = enc ?? Oem
            };
            var sb = new StringBuilder(); var r = new RunResult();
            try
            {
                using (var p = new Process { StartInfo = psi })
                {
                    DataReceivedEventHandler h = (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.OutputDataReceived += h; p.ErrorDataReceived += h;
                    p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { r.TimedOut = true; try { p.Kill(); } catch { } }
                    else p.WaitForExit();
                    r.Exit = r.TimedOut ? -1 : p.ExitCode;
                }
            }
            catch (Exception ex) { r.Exit = -2; sb.AppendLine(ex.Message); }
            lock (sb) r.Output = sb.ToString();
            return r;
        }
    }

    static class Q
    {
        // Entrecomilla un argumento para linea de comandos
        public static string Arg(string s) { char q = (char)34; return q + s + q; }
    }

    static class ShellOpen
    {
        // Archivo: abre su carpeta con el archivo marcado. Carpeta: la abre.
        public static void Location(string path)
        {
            path = FsWalk.NoPrefix(path);
            if (File.Exists(path)) Explorer("/select," + Quote(path));
            else if (Directory.Exists(path)) Explorer(Quote(path));
        }

        // Abre la carpeta que contiene el elemento, con el elemento marcado (sirve también para carpetas)
        public static void Reveal(string path)
        {
            path = FsWalk.NoPrefix(path);
            if (File.Exists(path) || Directory.Exists(path)) Explorer("/select," + Quote(path));
        }

        // La barra final de "C:\" escapaba la comilla de cierre y el Explorador no entendía la ruta
        internal static string Quote(string p)
        {
            p = p.TrimEnd('\\');
            if (p.Length == 2 && p[1] == ':') return p + "\\"; // raíz de una unidad: sin comillas
            return "\"" + p + "\"";
        }

        static void Explorer(string args)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("No se pudo abrir el Explorador (" + args + "): " + ex.Message); }
        }
    }

    static class ExplorerShell
    {
        // Cierra el Explorador, ejecuta una accion y deja que Windows lo reinicie (asi vuelve sin permisos elevados).
        public static void Restart(Action whileStopped)
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
                p.Dispose();
            }
            try { if (whileStopped != null) whileStopped(); } catch { }
            for (int i = 0; i < 24; i++)
            {
                Thread.Sleep(500);
                if (Process.GetProcessesByName("explorer").Length > 0) return;
            }
            try { Process.Start("explorer.exe"); } catch { }
        }
    }

    static class Safe
    {
        static readonly string Sep = Path.DirectorySeparatorChar.ToString();

        static string Trim(string s) { return s.TrimEnd(Path.DirectorySeparatorChar); }

        // Barrera extra: nunca borrar estas rutas ni nada dentro de las del sistema.
        public static bool CanDelete(string path)
        {
            try
            {
                string full = Trim(Path.GetFullPath(path));
                if (full.Length <= 3) return false;
                string[] blockedRoots = { Env.Win, Env.ProgramFiles, Env.ProgramFilesX86, Env.ProgramData };
                foreach (var b in blockedRoots)
                {
                    if (string.IsNullOrEmpty(b)) continue;
                    string bb = Trim(b);
                    if (full.Equals(bb, StringComparison.OrdinalIgnoreCase) || full.StartsWith(bb + Sep, StringComparison.OrdinalIgnoreCase)) return false;
                }
                string[] blockedExact = { Env.Profile, Env.Local, Env.Roaming, Path.Combine(Env.SysDrive, "Users"),
                    Path.Combine(Env.Profile, "Desktop"), Path.Combine(Env.Profile, "Documents"), Path.Combine(Env.Profile, "Pictures"),
                    Path.Combine(Env.Profile, "Videos"), Path.Combine(Env.Profile, "Music"), Env.Downloads };
                foreach (var b in blockedExact)
                    if (!string.IsNullOrEmpty(b) && full.Equals(Trim(b), StringComparison.OrdinalIgnoreCase)) return false;
                if (full.IndexOf("Recycle.Bin", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (full.IndexOf("System Volume Information", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return true;
            }
            catch { return false; }
        }

        // Sin contenido local: marcadores de OneDrive / Google Drive / archivos sin conexion
        public static bool IsCloudOnly(FileAttributes a)
        {
            return ((int)a & (0x400000 | 0x40000 | 0x1000)) != 0;
        }
    }
}

namespace PulsoPC
{
    static class Wmi
    {
        // Ruta de espacio de nombres WMI local, por ejemplo: \.\root\wmi
        public static string Scope(string ns) { return new string((char)92, 2) + "." + (char)92 + ns.Replace((char)47, (char)92); }
    }
}
