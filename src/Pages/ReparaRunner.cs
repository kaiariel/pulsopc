using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PulsoPC
{
    // Ejecuta programas del sistema mostrando su salida en vivo
    class Runner
    {
        public CancellationToken Ct;
        public Action<string> OnLine = s => { };
        public Action<string> OnPartial = s => { };
        public string Drive = Env.SysDrive.Substring(0, 2);

        public void Write(string line) { OnLine(line); }

        public int Exec(string file, string args, Encoding enc = null, string stdin = null)
        {
            OnLine("> " + file + " " + args);
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = stdin != null, StandardOutputEncoding = enc ?? Sh.Oem, StandardErrorEncoding = enc ?? Sh.Oem
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (stdin != null) { p.StandardInput.WriteLine(stdin); p.StandardInput.Close(); }
                    var t1 = Task.Run(() => Pump(p.StandardOutput)); var t2 = Task.Run(() => Pump(p.StandardError));
                    while (!p.WaitForExit(250))
                    {
                        if (Ct.IsCancellationRequested) { Kill(p); OnLine("Cancelado por el usuario."); break; }
                    }
                    try { Task.WaitAll(new[] { t1, t2 }, 4000); } catch { }
                    OnPartial("");
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex) { OnLine("No se pudo ejecutar: " + ex.Message); return -2; }
        }

        static void Kill(Process p)
        {
            try { Sh.Run("taskkill", "/PID " + p.Id + " /T /F", 8000); } catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        // Separa lineas y trata el retorno de carro como progreso que se reescribe
        void Pump(StreamReader r)
        {
            var sb = new StringBuilder(); var buf = new char[512]; int n; bool cr = false;
            while ((n = r.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    char c = buf[i];
                    if (c == (char)0) continue;
                    if (c == (char)10) { if (sb.Length > 0) OnLine(sb.ToString()); sb.Clear(); cr = false; OnPartial(""); }
                    else if (c == (char)13) cr = true;
                    else { if (cr) { sb.Clear(); cr = false; } sb.Append(c); }
                }
                if (sb.Length > 0) OnPartial(sb.ToString());
            }
            if (sb.Length > 0) OnLine(sb.ToString());
        }
    }

    class RepairAction
    {
        public string Id, Title, Desc, Confirm, Tag = "";
        public bool NeedsAdmin = true;
        public Func<Runner, int> Run;
    }
}
