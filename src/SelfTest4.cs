using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PulsoPC
{
    static partial class SelfTest
    {
        // Detectores de servicios rotos, DCOM y memoria (solo lectura, no cambian nada)
        static void TestHealth()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var broken = ServiceHealth.Find(7);
            Check("servicios rotos: la búsqueda termina", broken != null, broken.Count + " encontrados en " + sw.ElapsedMilliseconds + " ms");
            foreach (var b in broken.Take(5)) Info("servicio roto: " + b.SvcName + " <- " + b.DepName + " (" + b.Failures + " fallos)");
            var raw = ServiceHealth.RawPairs(14, 400);
            foreach (var p in raw.Distinct().Take(3)) Info("evento 7001 leído: " + p + " (" + raw.Count(x => x == p) + " veces)");

            Check("DCOM: resuelve el generador de miniaturas", DcomHealth.Resolve("{AB8902B4-09CA-4BB6-B78D-A8F59079A8D5}").IndexOf("Thumbnail", StringComparison.OrdinalIgnoreCase) >= 0);
            var d = new DcomIssue { Clsid = "{AB8902B4-09CA-4BB6-B78D-A8F59079A8D5}" }; DcomHealth.Describe(d);
            Check("DCOM: traduce el nombre", d.Friendly.StartsWith("Generador de miniaturas"), d.Friendly);
            Check("DCOM: CLSID inexistente no falla", DcomHealth.Resolve("{00000000-1111-2222-3333-444444444444}") == "");
            var dc = DcomHealth.Find(7);
            foreach (var x in dc.Take(5)) Info("DCOM 10010: " + x.Count + " x " + x.Friendly);

            var pg = MemPressure.PagesIn(2, 300);
            Check("memoria: lee las lecturas de página", pg[0] >= 0, "promedio " + pg[0].ToString("N0") + "/s, máximo " + pg[1].ToString("N0") + "/s");

            var row = new ProcRow { Name = "comet", Count = 26, Ws = 3L * 1073741824 };
            Check("memoria: formato por app", DiagRules.AppMem(row).Contains("26 procesos"), DiagRules.AppMem(row));
        }

        static void TestAiReport()
        {
            string raw = "Equipo " + Environment.MachineName + ", usuario " + Environment.UserName + ", ruta " + Env.Profile + "\\Documentos, correo alguien@ejemplo.com, otra palabra";
            string an = AiReport.Anonymize(raw);
            Check("informe IA: saca el nombre del equipo", an.IndexOf(Environment.MachineName, StringComparison.OrdinalIgnoreCase) < 0, an);
            Check("informe IA: saca el usuario y la ruta del perfil", an.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) < 0 && an.Contains("Users\\usuario\\Documentos"));
            Check("informe IA: saca los correos", !an.Contains("@") && an.Contains("[correo]"));
            Check("informe IA: deja el resto igual", an.EndsWith("otra palabra"));
        }

        // Monitor de trabas: muestreo real unos segundos y análisis con casos armados
        static void TestMonitor()
        {
            HangMonitor.Start();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (HangMonitor.Count < 3 && sw.ElapsedMilliseconds < 15000) Thread.Sleep(200);
            var got = HangMonitor.Recent(TimeSpan.FromMinutes(1));
            HangMonitor.Stop();
            Check("monitor: toma muestras", got.Count >= 3, got.Count + " muestras en " + sw.ElapsedMilliseconds + " ms");
            if (got.Count > 0)
            {
                var x = got[got.Count - 1];
                Check("monitor: lee memoria y CPU", x.RamFreeMb > 0 && x.Cpu >= 0, x.RamFreeMb + " MB libres, CPU " + x.Cpu + " %");
                Check("monitor: lee lecturas de página", x.PagesIn >= 0, x.PagesIn + "/s");
                Check("monitor: mide la barra de tareas", x.BarMs >= 0, x.BarMs + " ms" + (x.BarHung ? " (colgada)" : ""));
                Info("monitor: ventana al frente " + x.FgApp + " (" + x.FgMs + " ms), disco " + x.DiskBusy + " %, cola " + x.DiskQueue + ", más lectura «" + x.TopRead + "», más CPU «" + x.TopCpu + "»");
            }
            Check("monitor: escribe el CSV", System.IO.File.Exists(HangMonitor.CsvPath(DateTime.Now)));

            Func<int, int, int, bool, List<HangSample>> mk = (ram, pages, bar, hung) =>
                Enumerable.Range(0, 20).Select(i => new HangSample { T = DateTime.Now.AddSeconds(-40 + i * 2), RamFreeMb = i == 10 ? ram : 4000, PagesIn = i == 10 ? pages : 50, BarMs = i == 12 ? bar : 0, BarHung = i == 12 && hung, Cpu = 10, DiskBusy = 5, FgApp = i == 10 ? "comet" : "claude", TopRead = i == 10 ? "comet 300 MB/s" : "" }).ToList();
            var vm = HangMonitor.Analyze(mk(39, 138780, 0, false), DateTime.Now);
            Check("análisis: detecta falta de memoria", vm.Title == "Faltó memoria" && vm.Lines.Any(l => l.Contains("comet")), vm.Title + " | " + string.Join(" / ", vm.Lines.ToArray()));
            var vb = HangMonitor.Analyze(mk(4000, 50, 2000, true), DateTime.Now);
            Check("análisis: detecta Explorador colgado sin falta de recursos", vb.Title.StartsWith("Se colgó el Explorador") && vb.Lines.Any(l => l.Contains("no fue falta de recursos")), vb.Title);
            var vn = HangMonitor.Analyze(mk(4000, 50, 0, false), DateTime.Now);
            Check("análisis: sin anomalías no inventa una causa", vn.Level == Lvl.Info, vn.Title);
        }
    }
}
