using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace PulsoPC
{
    static partial class SelfTest
    {
        static string LP(string p) { return new string((char)92, 2) + "?" + (char)92 + p; }

        static void TestRunner()
        {
            var lines = new List<string>();
            var r = new Runner { Ct = CancellationToken.None, OnLine = l => { lock (lines) lines.Add(l); } };
            int code = r.Exec("cmd.exe", "/c echo hola& echo mundo");
            Check("ejecutor: código de salida 0", code == 0, "código " + code);
            Check("ejecutor: captura la salida línea a línea", lines.Any(l => l.Trim() == "hola") && lines.Any(l => l.Trim() == "mundo"), string.Join(" | ", lines.ToArray()));
            lines.Clear();
            r.Exec("findstr.exe", ".", null, "Y");
            Check("ejecutor: envía respuesta por la entrada", lines.Any(l => l.Trim() == "Y"), string.Join(" | ", lines.ToArray()));
            var cts = new CancellationTokenSource(); var r2 = new Runner { Ct = cts.Token };
            var timer = new System.Threading.Timer(_ => cts.Cancel(), null, 1500, Timeout.Infinite);
            var sw = Stopwatch.StartNew(); r2.Exec("ping.exe", "-n 30 127.0.0.1"); sw.Stop(); timer.Dispose();
            Check("ejecutor: la cancelación detiene el proceso", sw.Elapsed.TotalSeconds < 8, sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
        }

        static void TestNodeModules(string root)
        {
            string proj = Path.Combine(root, "proj1"); Directory.CreateDirectory(proj); File.WriteAllText(Path.Combine(proj, "package.json"), "{}");
            string nm = Path.Combine(proj, "node_modules"), deep = nm;
            for (int i = 0; i < 12; i++) deep = Path.Combine(deep, new string('a', 30));
            Directory.CreateDirectory(LP(deep)); File.WriteAllText(LP(Path.Combine(deep, "f.txt")), "x");
            Check("crea ruta de más de 260 caracteres", deep.Length > 260 && Directory.Exists(LP(deep)), "largo " + deep.Length);
            Directory.CreateDirectory(Path.Combine(root, "proj2", "node_modules"));
            string hid = Path.Combine(root, ".hid"); Directory.CreateDirectory(Path.Combine(hid, "node_modules")); File.WriteAllText(Path.Combine(hid, "package.json"), "{}");
            var found = new List<string>();
            FsWalk.Files(root, true, null, null, CancellationToken.None, di =>
            {
                if (di.Name == "node_modules") { if (File.Exists(Path.Combine(di.Parent.FullName, "package.json"))) found.Add(di.FullName); return true; }
                return di.Name.StartsWith(".");
            });
            Check("busca solo node_modules de proyectos reales", found.Count == 1 && found[0] == nm, string.Join(" | ", found.ToArray()));
            Sh.Run("cmd.exe", "/c rmdir /s /q " + Q.Arg(LP(nm)), 120000);
            Check("elimina node_modules con ruta larga", !Directory.Exists(LP(nm)));
            Check("conserva el package.json del proyecto", File.Exists(Path.Combine(proj, "package.json")));
        }

        static void TestStartup()
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string apprKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            const string name = "PulsoPC_Autotest";
            Reg.SetString(Registry.CurrentUser, runKey, name, Path.Combine(Env.Win, "notepad.exe"));
            try
            {
                var it = Startup.List().FirstOrDefault(x => x.Name == name);
                Check("inicio: lista el programa de prueba como activo", it != null && it.Enabled);
                if (it == null) return;
                Check("inicio: deshabilitar sin errores", Startup.SetEnabled(it, false) == null);
                it = Startup.List().First(x => x.Name == name);
                Check("inicio: el estado deshabilitado se conserva", !it.Enabled);
                Startup.SetEnabled(it, true);
                it = Startup.List().First(x => x.Name == name);
                Check("inicio: volver a habilitar", it.Enabled);
            }
            finally { Reg.Delete(Registry.CurrentUser, runKey, name); Reg.Delete(Registry.CurrentUser, apprKey, name); }
        }
    }
}
