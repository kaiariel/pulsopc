using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace PulsoPC
{
    // Autoprueba sin interfaz: ejerce el motor de limpieza, el ejecutor y los ajustes con datos temporales
    static partial class SelfTest
    {
        static StringBuilder sb; static int fails;
        static void Check(string name, bool ok, string extra = "")
        {
            sb.AppendLine((ok ? "PASS  " : "FAIL  ") + name + (extra.Length > 0 ? "   -> " + extra : ""));
            if (!ok) fails++;
        }
        static void Info(string text) { sb.AppendLine("INFO  " + text); }

        public static int Run(string outFile)
        {
            sb = new StringBuilder(); fails = 0;
            string root = Path.Combine(Path.GetTempPath(), "pulsopc_selftest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            string[] names = { "Wipe", "Safe", "Runner", "NodeModules", "Startup", "Tweak", "Info", "Health", "Monitor", "DiskIndex", "AiReport", "Mft", "Known", "MftAttrList", "StorageExport" };
            Action[] tests = { () => TestWipe(root), TestSafe, TestRunner, () => TestNodeModules(root), TestStartup, TestTweak, TestInfo, TestHealth, TestMonitor, () => TestDiskIndex(root), TestAiReport, TestMft, TestKnown, TestMftAttrList, () => TestStorageExport(root) };
            for (int i = 0; i < tests.Length; i++)
            {
                try { tests[i](); } catch (Exception ex) { Check("excepción en " + names[i], false, ex.ToString().Replace("\r\n", " | ")); }
            }
            try { Directory.Delete(root, true); } catch { }
            sb.AppendLine(); sb.AppendLine(fails == 0 ? "TODAS LAS PRUEBAS PASARON" : fails + " PRUEBA(S) FALLARON");
            File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
            return fails;
        }

        static void WriteFile(string path, int size, DateTime when)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[size]); File.SetLastWriteTime(path, when);
        }

        static void TestWipe(string root)
        {
            string w = Path.Combine(root, "w"), outside = Path.Combine(root, "outside");
            DateTime old = DateTime.Now.AddHours(-48);
            WriteFile(Path.Combine(w, "old.txt"), 100, old);
            WriteFile(Path.Combine(w, "new.txt"), 111, DateTime.Now);
            WriteFile(Path.Combine(w, "ro.txt"), 200, old); File.SetAttributes(Path.Combine(w, "ro.txt"), FileAttributes.ReadOnly);
            WriteFile(Path.Combine(w, "sub", "old2.txt"), 300, old);
            WriteFile(Path.Combine(w, "sub", "new2.txt"), 122, DateTime.Now);
            Directory.CreateDirectory(Path.Combine(w, "emptyold")); Directory.SetLastWriteTime(Path.Combine(w, "emptyold"), old);
            Directory.CreateDirectory(Path.Combine(w, "emptynew"));
            WriteFile(Path.Combine(outside, "precious.txt"), 50, old);
            var mk = Sh.Run("cmd.exe", "/c mklink /J " + Q.Arg(Path.Combine(w, "junc")) + " " + Q.Arg(outside));
            Check("crear unión de carpetas de prueba", mk.Exit == 0, mk.Output.Trim());
            var t = new Target { Dir = w, Recursive = true, MinAgeHours = 24, Filter = fi => (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalHours >= 24 };
            long freed = CleanerService.Wipe(t, CancellationToken.None);
            Check("borra archivo antiguo", !File.Exists(Path.Combine(w, "old.txt")));
            Check("borra archivo antiguo de solo lectura", !File.Exists(Path.Combine(w, "ro.txt")));
            Check("borra antiguo en subcarpeta", !File.Exists(Path.Combine(w, "sub", "old2.txt")));
            Check("conserva archivo reciente", File.Exists(Path.Combine(w, "new.txt")));
            Check("conserva reciente en subcarpeta", File.Exists(Path.Combine(w, "sub", "new2.txt")));
            Check("no atraviesa la unión de carpetas", File.Exists(Path.Combine(outside, "precious.txt")));
            Check("quita carpeta vacía antigua", !Directory.Exists(Path.Combine(w, "emptyold")));
            Check("conserva carpeta vacía reciente", Directory.Exists(Path.Combine(w, "emptynew")));
            Check("bytes liberados correctos", freed == 600, "liberados=" + freed + " esperado=600");
            Sh.Run("cmd.exe", "/c rmdir " + Q.Arg(Path.Combine(w, "junc")));
        }

        static void TestSafe()
        {
            Check("protege la raíz de la unidad", !Safe.CanDelete(Env.SysDrive));
            Check("protege Windows", !Safe.CanDelete(Env.Win));
            Check("protege dentro de Windows", !Safe.CanDelete(Path.Combine(Env.Win, "Temp")));
            Check("protege Program Files", !Safe.CanDelete(Env.ProgramFiles));
            Check("protege el perfil de usuario", !Safe.CanDelete(Env.Profile));
            Check("protege Documentos", !Safe.CanDelete(Path.Combine(Env.Profile, "Documents")));
            Check("protege la papelera", !Safe.CanDelete(Path.Combine(Env.SysDrive, "$Recycle.Bin", "x")));
            Check("permite una carpeta temporal", Safe.CanDelete(Path.Combine(Path.GetTempPath(), "pulsopc_x")));
            Check("detecta archivo solo en la nube", Safe.IsCloudOnly((FileAttributes)0x400000) && !Safe.IsCloudOnly(FileAttributes.Normal));
        }
    }
}
