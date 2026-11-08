using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PulsoPC
{
    // Modos de linea de comandos usados para verificar la aplicacion sin intervencion manual
    static class Cli
    {
        public static int RunReport(string path)
        {
            try
            {
                var s = Diag.Run(null);
                File.WriteAllText(path, PulsoPC.Report.Build(s), Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { try { File.WriteAllText(path, "ERROR: " + ex); } catch { } return 1; }
        }

        // Informe para IA sin ventana (anonimizado), opcionalmente con el mapa de una unidad
        public static int RunAiReport(string path, string drive)
        {
            try
            {
                if (!string.IsNullOrEmpty(drive)) DiskIndex.Last = DiskIndex.Scan(drive, System.Threading.CancellationToken.None);
                var s = Diag.Run(null);
                File.WriteAllText(path, AiReport.Build(s, true, true, DiskIndex.Last != null), new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex) { try { File.WriteAllText(path, "ERROR: " + ex); } catch { } return 1; }
        }

        // Informe de almacenamiento sin ventana: escanea, busca sobrantes y exporta .md, .json y .tsv
        public static int RunStorageExport(string drive, string folder)
        {
            try
            {
                var ix = DiskIndex.Scan(drive, System.Threading.CancellationToken.None);
                var g = Leftovers.Find(ix);
                var files = StorageExport.Write(ix, g, folder);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(files[0]), "ultimo.txt"), string.Join(Environment.NewLine, files), new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex) { Log.Write("Exportar almacenamiento falló: " + ex); return 1; }
        }

        // Sobrantes de la unidad de Windows sin ventana
        public static int RunLeftovers(string path)
        {
            try
            {
                var ix = DiskIndex.Scan(Env.SysDrive, System.Threading.CancellationToken.None);
                var sw = Stopwatch.StartNew(); var g = Leftovers.Find(ix);
                var sb = new StringBuilder();
                sb.AppendLine("Sobrantes en " + Env.SysDrive + " (" + ix.Method + ", mapa " + ix.Elapsed.TotalSeconds.ToString("0.0") + " s, búsqueda " + sw.ElapsedMilliseconds + " ms): " + g.Count + " grupos");
                foreach (var x in g)
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + x.Info.VerdictText + "] " + x.Info.Title + " · " + x.Count + " · " + Fmt.Bytes(x.Size) + (x.AllRecyclable ? " · papelera" : "") + (x.Info.Page != null ? " · ir a " + x.Info.Page : "") + (x.Info.Command != null ? " · " + x.Info.Command : ""));
                    foreach (var p in x.Paths.Take(4)) sb.AppendLine("    " + p);
                    if (x.Paths.Count > 4) sb.AppendLine("    … y " + (x.Paths.Count - 4) + " más");
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { try { File.WriteAllText(path, "ERROR: " + ex); } catch { } return 1; }
        }

        // Mapa del disco sin ventana: escanea una unidad y guarda carpetas, tipos y archivos más grandes
        public static int RunMap(string drive, string path)
        {
            try
            {
                var ix = DiskIndex.Scan(drive, System.Threading.CancellationToken.None);
                var sb = new StringBuilder();
                sb.AppendLine("Mapa de " + drive + " en " + ix.Elapsed.TotalSeconds.ToString("0.0") + " s (" + ix.Method + ")  ·  " + ix.ScannedFiles.ToString("N0") + " archivos, " + ix.ScannedDirs.ToString("N0") + " carpetas, " + ix.DeniedDirs + " sin acceso, " + ix.HardLinks.ToString("N0") + " enlaces duros");
                sb.AppendLine("Tamaño " + Fmt.Bytes(ix.Root.Size) + "  ·  en disco " + Fmt.Bytes(ix.Root.Alloc) + "  ·  usado según Windows " + Fmt.Bytes(ix.Total - ix.Free));
                sb.AppendLine(); sb.AppendLine("CARPETAS (2 niveles)");
                foreach (var k in ix.Root.Kids.Take(15))
                {
                    sb.AppendLine("  " + Fmt.Bytes(k.Size).PadLeft(9) + "  " + k.Name + "  (" + k.Files.ToString("N0") + " archivos)" + (k.Denied ? " SIN ACCESO" : ""));
                    if (k.Kids != null) foreach (var k2 in k.Kids.Take(4)) sb.AppendLine("      " + Fmt.Bytes(k2.Size).PadLeft(9) + "  " + k2.Name);
                }
                sb.AppendLine("  " + Fmt.Bytes(ix.Root.FilesSize).PadLeft(9) + "  [" + ix.Root.DirectFiles + " archivos en la raíz]");
                foreach (var f in DiskIndex.FilesOf(ix.Root, 5)) sb.AppendLine("      " + Fmt.Bytes(f.Size).PadLeft(9) + "  " + f.Name);
                sb.AppendLine(); sb.AppendLine("TIPOS DE ARCHIVO");
                foreach (var e in ix.Ext.Values.OrderByDescending(x => x.Alloc).Take(15)) sb.AppendLine("  " + Fmt.Bytes(e.Alloc).PadLeft(9) + "  ." + (e.Ext.Length == 0 ? "(sin extensión)" : e.Ext) + "  " + e.Count.ToString("N0") + " archivos");
                sb.AppendLine(); sb.AppendLine("ARCHIVOS MÁS GRANDES");
                foreach (var f in ix.TopFiles.Take(15)) sb.AppendLine("  " + Fmt.Bytes(f.Size).PadLeft(9) + "  " + f.Path);
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { try { File.WriteAllText(path, "ERROR: " + ex); } catch { } return 1; }
        }

        static void Pump(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(30); }
        }

        static void Shot(Form f, string path)
        {
            using (var bmp = new Bitmap(f.Width, f.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(f.Location, Point.Empty, f.Size);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        public static int RunCapture(string dir, string pages)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var f = new MainForm();
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(10, 10); f.TopMost = true;
                f.Show(); f.WindowState = FormWindowState.Normal; f.Location = new Point(10, 10); f.Activate(); f.BringToFront();
                string[] keys = string.IsNullOrEmpty(pages) ? new[] { "resumen", "limpieza", "espacio", "inicio", "procesos", "trabas", "optimizar", "reparar" } : pages.Split(',');
                foreach (var k0 in keys)
                {
                    // "mapa=E:" abre Espacio en disco y escanea esa unidad antes de capturar
                    string k = k0; int wait = k == "resumen" ? 16000 : k == "limpieza" ? 30000 : 8000;
                    if (k == "sobrantes") { EspacioPage.StartTab = 1; SobrantesView.AutoScan = true; k = "espacio"; wait = 70000; }
                    if (k.StartsWith("mapa=")) { MapaView.AutoScan = k.Substring(5).TrimEnd('\\') + "\\"; k = "espacio"; wait = 25000; }
                    if (k == "trabas" && !HangMonitor.Running) { HangMonitor.Start(); Pump(30000); }
                    f.Go(k);
                    Pump(wait);
                    Shot(f, Path.Combine(dir, k0.Replace("=", "-").Replace(":", "") + ".png"));
                }
                HangMonitor.Stop();
                f.Close();
                return 0;
            }
            catch (Exception ex) { Log.Write("Captura fallida: " + ex); return 1; }
        }
    }
}
