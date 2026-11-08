using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PulsoPC
{
    static partial class SelfTest
    {
        // Informe de almacenamiento: dos exportaciones de un disco ficticio y la comparación entre ellas
        static void TestStorageExport(string root)
        {
            const long MB = 1048576;
            Func<long, long, long, long, DiskIndex> disk = (sub, a, b, c) =>
            {
                var ix = new DiskIndex { Drive = "X:\\", Root = new DNode { Name = "X:\\" }, Total = 100000 * MB, Free = 50000 * MB, Method = "prueba" };
                Func<DNode, string, long, DNode> d = (p, n, al) => { var k = new DNode { Name = n, Parent = p, Alloc = al, Size = al, Files = 10 }; if (p.Kids == null) p.Kids = new List<DNode>(); p.Kids.Add(k); return k; };
                var A = d(ix.Root, "Proyectos", a); d(A, "videos", sub);
                if (b > 0) d(ix.Root, "Viejo", b);
                if (c > 0) d(ix.Root, "Nuevo", c);
                ix.Root.Alloc = ix.Root.Kids.Sum(k => k.Alloc); ix.Root.Size = ix.Root.Alloc;
                return ix;
            };
            string dir = Path.Combine(root, "informes");
            var f1 = StorageExport.Write(disk(450 * MB, 500 * MB, 600 * MB, 0), new List<LeftoverGroup>(), dir, "20261101-1000");
            Check("informe: escribe los tres archivos", f1.All(File.Exists), string.Join(", ", f1.Select(Path.GetFileName).ToArray()));
            string md1 = File.ReadAllText(f1[0]);
            Check("informe: el primero avisa que no hay con qué comparar", md1.Contains("Es el primer informe"));
            Check("informe: trae las instrucciones para la IA", md1.Contains("Para la IA") && md1.Contains("Borrar ya") && md1.Contains("Mover a otro disco"));
            Check("informe: la foto tiene las carpetas de 100 MB o más", File.ReadAllLines(f1[2]).Any(l => l.StartsWith("X:\\Proyectos\\videos\t")));

            var f2 = StorageExport.Write(disk(1500 * MB, 1550 * MB, 0, 400 * MB), new List<LeftoverGroup>(), dir, "20261106-1200");
            string md2 = File.ReadAllText(f2[0]), js2 = File.ReadAllText(f2[1]);
            Check("informe: compara con el anterior", md2.Contains("Comparado con el 01/11/2026 10:00"));
            Check("informe: muestra la subcarpeta que creció (no la de arriba)", md2.Contains("X:\\Proyectos\\videos") && !md2.Contains("| X:\\Proyectos |"), "");
            Check("informe: marca lo nuevo y lo que desapareció", md2.Contains("X:\\Nuevo") && md2.Contains("X:\\Viejo"));
            Check("informe: el JSON trae los cambios", js2.Contains("\"cambios\"") && js2.Contains("\"ruta\": \"X:\\\\Proyectos\\\\videos\"") && js2.Contains("\"comparado_con\": \"2026-11-01T10:00:00\""));
            Check("informe: el JSON está bien cerrado", js2.TrimEnd().EndsWith("}") && js2.Count(ch => ch == '{') == js2.Count(ch => ch == '}') && js2.Count(ch => ch == '[') == js2.Count(ch => ch == ']'));
        }
    }
}
