using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace PulsoPC
{
    class StartupItem
    {
        public string Name = "", Command = "", Source = "", Publisher = "", Advice = "", Target = "";
        public bool Enabled = true, Machine;
        public RegistryKey Hive; public string ApprovedPath = "";
    }

    static class Startup
    {
        const string Run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string Appr = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
        static readonly char DQ = (char)34;

        public static List<StartupItem> List()
        {
            var res = new List<StartupItem>();
            AddRun(res, Registry.CurrentUser, Run, Appr + @"\Run", false, "Registro (usuario)");
            AddRun(res, Registry.LocalMachine, Run, Appr + @"\Run", true, "Registro (equipo)");
            AddRun(res, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", Appr + @"\Run32", true, "Registro (equipo, 32 bits)");
            AddFolder(res, Path.Combine(Env.Roaming, @"Microsoft\Windows\Start Menu\Programs\Startup"), Registry.CurrentUser, false, "Carpeta Inicio (usuario)");
            AddFolder(res, Path.Combine(Env.ProgramData, @"Microsoft\Windows\Start Menu\Programs\StartUp"), Registry.LocalMachine, true, "Carpeta Inicio (equipo)");
            foreach (var it in res) Describe(it);
            return res.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static bool IsDisabled(RegistryKey hive, string apprPath, string name)
        {
            try
            {
                using (var k = hive.OpenSubKey(apprPath))
                {
                    if (k == null) return false;
                    byte[] b = k.GetValue(name) as byte[];
                    return b != null && b.Length > 0 && (b[0] & 1) == 1;
                }
            }
            catch { return false; }
        }

        static void AddRun(List<StartupItem> res, RegistryKey hive, string path, string appr, bool machine, string source)
        {
            try
            {
                using (var k = hive.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (var n in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        var it = new StartupItem { Name = n, Command = Convert.ToString(k.GetValue(n)), Source = source, Machine = machine, Hive = hive, ApprovedPath = appr };
                        it.Enabled = !IsDisabled(hive, appr, n);
                        res.Add(it);
                    }
                }
            }
            catch { }
        }

        static void AddFolder(List<StartupItem> res, string dir, RegistryKey hive, bool machine, string source)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var it = new StartupItem { Name = fn, Command = f, Source = source, Machine = machine, Hive = hive, ApprovedPath = Appr + @"\StartupFolder" };
                    it.Enabled = !IsDisabled(hive, it.ApprovedPath, fn);
                    if (fn.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) it.Target = ResolveLnk(f);
                    res.Add(it);
                }
            }
            catch { }
        }

        static string ResolveLnk(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                return (string)sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
            }
            catch { return ""; }
        }

        static string ExtractExe(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return "";
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd[0] == DQ) { int e = cmd.IndexOf(DQ, 1); return e > 1 ? cmd.Substring(1, e - 1) : cmd.Trim(DQ); }
            int idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return cmd.Substring(0, idx + 4);
            return cmd.Split(' ')[0];
        }

        static void Describe(StartupItem it)
        {
            string exe = string.IsNullOrEmpty(it.Target) ? ExtractExe(it.Command) : it.Target;
            if (string.IsNullOrEmpty(it.Target)) it.Target = exe;
            try { if (File.Exists(exe)) it.Publisher = (FileVersionInfo.GetVersionInfo(exe).CompanyName ?? "").Trim(); } catch { }
            string s = (it.Name + " " + it.Command + " " + it.Publisher).ToLowerInvariant();
            string[] keep = { "securityhealth", "windows defender", "realtek", "rtkaud", "nvidia", "igfx", "synaptics", "touchpad", "radeon", "bluetooth" };
            string[] sync = { "onedrive", "google drive", "googledrivefs", "dropbox", "icloud", "megasync" };
            string[] upd = { "update", "updater", "helper", "autoupdate" };
            if (keep.Any(k => s.Contains(k))) it.Advice = "Sistema o controlador: mejor dejarlo";
            else if (sync.Any(k => s.Contains(k))) it.Advice = "Sincronizador: usa disco al iniciar";
            else if (upd.Any(k => s.Contains(k))) it.Advice = "Actualizador: se puede quitar";
            else it.Advice = "Opcional: seguro de quitar";
        }

        public static string SetEnabled(StartupItem it, bool enable)
        {
            try
            {
                using (var k = it.Hive.CreateSubKey(it.ApprovedPath))
                {
                    byte[] b = new byte[12];
                    if (enable) b[0] = 2; else { b[0] = 3; BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(b, 4); }
                    k.SetValue(it.Name, b, RegistryValueKind.Binary);
                }
                it.Enabled = enable;
                Log.Write((enable ? "Inicio habilitado: " : "Inicio deshabilitado: ") + it.Name);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
