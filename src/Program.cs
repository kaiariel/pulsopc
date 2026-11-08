using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PulsoPC
{
    static class Program
    {
        public static bool IsAdmin;

        [STAThread]
        static int Main(string[] args)
        {
            try { IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => { Log.Write("Error: " + e.Exception); try { MessageBox.Show("Ocurrió un error inesperado:\n\n" + e.Exception.Message, "PulsoPC", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { } };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Error fatal: " + e.ExceptionObject);

            if (args.Length >= 2 && args[0] == "--informe") return Cli.RunReport(args[1]);
            if (args.Length >= 2 && args[0] == "--captura") return Cli.RunCapture(args[1], args.Length > 2 ? args[2] : "");
            if (args.Length >= 2 && args[0] == "--autotest") return SelfTest.Run(args[1]);
            if (args.Length >= 3 && args[0] == "--mapa") return Cli.RunMap(args[1], args[2]);
            if (args.Length >= 3 && args[0] == "--mapa-lento") { DiskIndex.ForceWalk = true; return Cli.RunMap(args[1], args[2]); }
            if (args.Length >= 2 && args[0] == "--sobrantes") return Cli.RunLeftovers(args[1]);
            if (args.Length >= 2 && args[0] == "--exportar-disco") return Cli.RunStorageExport(args[1], args.Length > 2 ? args[2] : null);
            if (args.Length >= 2 && args[0] == "--informe-ia") return Cli.RunAiReport(args[1], args.Length > 2 ? args[2] : null);

            Application.Run(new MainForm());
            return 0;
        }

        // Reabre la aplicacion con permisos de administrador (aviso UAC)
        public static bool RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }
    }
}
