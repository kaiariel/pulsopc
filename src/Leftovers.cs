using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PulsoPC
{
    // Un grupo de cosas que sobran (o que conviene revisar) con su explicación
    class LeftoverGroup
    {
        public KnownInfo Info; public string Key = "";
        public List<string> Paths = new List<string>(); public long Size; public int Count;
        public bool AllRecyclable = true;
    }

    static class Leftovers
    {
        const long MinItem = 50L * 1048576; // por debajo de 50 MB no vale la pena listarlo (salvo grupos como los perfiles temporales)
        static readonly HashSet<string> Grouped = new HashSet<string> { "Perfil temporal de usuario", "Dependencias de un proyecto (node_modules)", "Volcado de memoria", "Archivo temporal" };

        public static List<LeftoverGroup> Find(DiskIndex ix)
        {
            var groups = new Dictionary<string, LeftoverGroup>();
            Action<KnownInfo, string, long, bool> add = (k, path, size, dir) =>
            {
                LeftoverGroup g;
                if (!groups.TryGetValue(k.Title, out g)) groups[k.Title] = g = new LeftoverGroup { Info = k, Key = k.Title };
                g.Paths.Add(path); g.Size += size; g.Count++;
                if (!k.CanRecycle || !Safe.CanDelete(path)) g.AllRecyclable = false;
            };

            // Carpetas: si una coincide con algo conocido que se puede borrar o revisar, se agrega y no se baja más
            var matched = new HashSet<DNode>();
            var st = new Stack<DNode>(); st.Push(ix.Root);
            while (st.Count > 0)
            {
                var n = st.Pop();
                if (n != ix.Root)
                {
                    var k = Known.Explain(n.FullPath, true);
                    if (k != null && k.V != Verdict.No && k.V != Verdict.Revisar)
                    {
                        if (n.Alloc >= MinItem || Grouped.Contains(k.Title)) add(k, n.FullPath, n.Alloc, true);
                        matched.Add(n);
                        continue;
                    }
                }
                if (n.Kids != null) foreach (var c in n.Kids) if (!c.Reparse) st.Push(c);
            }

            // Archivos grandes conocidos (volcados, discos virtuales, modelos, instaladores)
            foreach (var f in ix.TopFiles)
            {
                if (f.Size < MinItem) break;
                bool inside = false;
                for (var d = f.Dir; d != null && !inside; d = d.Parent) inside = matched.Contains(d);
                if (inside) continue;
                string path = f.Path;
                var k = Known.Explain(path, false);
                if (k != null && k.V != Verdict.No && (k.V != Verdict.Revisar || f.Size >= 200L * 1048576)) add(k, path, f.Size, false);
            }

            // Posibles duplicados: mismo nombre y mismo tamaño, de 200 MB o más, fuera de Windows, programas instalados y node_modules
            // (ahí suelen ser enlaces duros o partes de programas: no hay nada que hacer)
            string[] sysRoots = { Env.Win, Env.ProgramFiles, Env.ProgramFilesX86, Env.ProgramData };
            Func<string, bool> userFile = path => !sysRoots.Any(r => !string.IsNullOrEmpty(r) && path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                && path.IndexOf("\\node_modules\\", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("\\Packages\\", StringComparison.OrdinalIgnoreCase) < 0;
            foreach (var d in ix.TopFiles.Where(f => f.Size >= 200L * 1048576 && userFile(f.Path)).GroupBy(f => f.Name.ToLowerInvariant() + "|" + f.Size).Where(g => g.Count() > 1))
            {
                var first = d.First();
                var k = new KnownInfo
                {
                    Title = "Posible duplicado: " + first.Name, V = Verdict.Revisar,
                    What = "Hay " + d.Count() + " archivos con el mismo nombre y el mismo tamaño (" + Fmt.Bytes(first.Size) + " cada uno). Suelen ser el mismo modelo o instalador bajado por dos programas.",
                    How = "Revisá cuál usa cada programa antes de borrar. Si uno lo baja solo de nuevo, borrar ese no sirve: hay que desactivar la función que lo descarga.",
                    CanRecycle = true
                };
                var g = new LeftoverGroup { Info = k, Key = k.Title, Count = d.Count(), Size = first.Size * (d.Count() - 1) };
                g.Paths.AddRange(d.Select(x => x.Path)); g.AllRecyclable = false;
                groups[k.Title] = g;
            }

            // Instaladores y comprimidos viejos en Descargas (más de 60 días)
            var dl = FindNode(ix, Env.Downloads);
            if (dl != null)
            {
                long limit = DateTime.Now.AddDays(-60).ToFileTime();
                var old = DiskIndex.FilesOf(dl, 5000).Where(f => f.Size >= 20L * 1048576 && f.Modified < limit && new[] { "exe", "msi", "iso", "zip", "rar", "7z", "dmg" }.Contains(DiskIndex.ExtOf(f.Name))).ToList();
                if (old.Count > 0)
                {
                    var k = new KnownInfo { Title = "Instaladores y comprimidos viejos en Descargas", V = Verdict.Revisar, CanRecycle = true,
                        What = old.Count + " archivos de más de 20 MB que bajaste hace más de 60 días (.exe, .msi, .iso, .zip...).",
                        How = "Si ya instalaste o descomprimiste lo que eran, se pueden mandar a la papelera." };
                    var g = new LeftoverGroup { Info = k, Key = k.Title, Count = old.Count, Size = old.Sum(x => x.Size) };
                    g.Paths.AddRange(old.Select(x => x.Path)); g.AllRecyclable = old.All(x => Safe.CanDelete(x.Path));
                    groups[k.Title] = g;
                }
            }

            foreach (var g in groups.Values) g.Paths = g.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return groups.Values.OrderBy(g => g.Info.V == Verdict.Si ? 0 : g.Info.V == Verdict.Cuidado ? 1 : 2).ThenByDescending(g => g.Size).ToList();
        }

        public static DNode FindNode(DiskIndex ix, string path)
        {
            if (ix == null || string.IsNullOrEmpty(path)) return null;
            string root = ix.Root.Name.TrimEnd('\\');
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            var n = ix.Root;
            foreach (var part in path.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (n.Kids == null) return null;
                n = n.Kids.FirstOrDefault(k => k.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (n == null) return null;
            }
            return n;
        }
    }
}
