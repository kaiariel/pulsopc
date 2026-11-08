using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace PulsoPC
{
    static partial class SelfTest
    {
        // Mapa del disco: escaneo de un árbol armado con tamaños conocidos y comparación con el recorrido de .NET
        static void TestDiskIndex(string root)
        {
            string r = Path.Combine(root, "mapa");
            WriteFile(Path.Combine(r, "a", "uno.bin"), 5000, DateTime.Now);
            WriteFile(Path.Combine(r, "a", "dos.txt"), 300, DateTime.Now);
            WriteFile(Path.Combine(r, "a", "b", "tres.bin"), 70000, DateTime.Now);
            WriteFile(Path.Combine(r, "c", "sinext"), 10, DateTime.Now);
            WriteFile(Path.Combine(r, "c", "grande.mp4"), 2 * 1048576, DateTime.Now);
            WriteFile(Path.Combine(r, "raiz.txt"), 1, DateTime.Now);
            Directory.CreateDirectory(Path.Combine(r, "vacia"));

            var ix = DiskIndex.Scan(r, CancellationToken.None);
            long expect = 5000 + 300 + 70000 + 10 + 2 * 1048576 + 1;
            Check("mapa: suma el tamaño total", ix.Root.Size == expect, ix.Root.Size + " de " + expect);
            Check("mapa: cuenta archivos y carpetas", ix.Root.Files == 6 && ix.Root.Dirs == 4, ix.Root.Files + " archivos, " + ix.Root.Dirs + " carpetas");
            Check("mapa: ordena las carpetas por tamaño", ix.Root.Kids[0].Name == "c", string.Join(",", ix.Root.Kids.Select(k => k.Name).ToArray()));
            var a = ix.Root.Kids.First(k => k.Name == "a");
            Check("mapa: totales por carpeta", a.Size == 75300 && a.Files == 3 && a.DirectFiles == 2, a.Size + " / " + a.Files);
            Check("mapa: en disco redondea al clúster", ix.Root.Alloc >= expect && ix.Root.Alloc % ix.Cluster == 0, ix.Root.Alloc + " (clúster " + ix.Cluster + ")");
            Check("mapa: estadística por extensión", ix.Ext["bin"].Count == 2 && ix.Ext["bin"].Size == 75000 && ix.Ext[""].Count == 1, "bin=" + ix.Ext["bin"].Size);
            Check("mapa: archivos grandes por tipo", ix.TopByExt.ContainsKey("mp4") && ix.TopByExt["mp4"][0].Name == "grande.mp4");
            Check("mapa: ruta completa", a.FullPath.TrimEnd('\\').Equals(Path.Combine(r, "a"), StringComparison.OrdinalIgnoreCase), a.FullPath);
            var files = DiskIndex.FilesOf(a, 10);
            Check("mapa: lista los archivos de una carpeta", files.Count == 2 && files[0].Name == "uno.bin");
            ix.RemoveFile(a, 5000);
            Check("mapa: descuenta un archivo borrado", a.Size == 70300 && ix.Root.Size == expect - 5000 && ix.Root.Files == 5);
            var c = ix.Root.Kids.First(k => k.Name == "c"); long cs = c.Size;
            ix.RemoveDir(c);
            Check("mapa: descuenta una carpeta borrada", ix.Root.Size == expect - 5000 - cs && !ix.Root.Kids.Contains(c) && ix.Root.Dirs == 3);

            // Contra el recorrido estándar en una carpeta real del perfil
            string real = Path.Combine(Env.Local, "Temp");
            if (Directory.Exists(real))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rx = DiskIndex.Scan(real, CancellationToken.None); long t1 = sw.ElapsedMilliseconds;
                long net = 0; int nf = 0;
                FsWalk.Files(real, true, fi => { net += fi.Length; nf++; }, null, CancellationToken.None, null);
                Info("mapa: Temp del perfil = " + rx.Root.Files + " archivos / " + Fmt.Bytes(rx.Root.Size) + " en " + t1 + " ms; recorrido .NET = " + nf + " / " + Fmt.Bytes(net) + " en " + (sw.ElapsedMilliseconds - t1) + " ms");
                Check("mapa: coincide con el recorrido estándar (±2 %)", Math.Abs(rx.Root.Size - net) <= Math.Max(1048576, net / 50), rx.Root.Size + " vs " + net);
            }
        }
    }
}
