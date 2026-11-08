using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PulsoPC
{
    // Informe completo de almacenamiento para decidir con ayuda de una IA (Claude Code lo lee directo del disco):
    //   almacenamiento-C-AAAAMMDD-HHmm.md    resumen legible con instrucciones para la IA
    //   almacenamiento-C-AAAAMMDD-HHmm.json  todos los datos (carpetas, tipos, archivos, sobrantes, cambios)
    //   instantanea-C-AAAAMMDD-HHmm.tsv      foto de las carpetas para comparar con el próximo informe
    static class StorageExport
    {
        public static string Dir { get { return Path.Combine(Log.Dir, "informes"); } }
        const long FolderMin = 100L * 1048576; // carpetas de 100 MB o más

        class Change { public string Path; public long Before, After; public bool New; public long Delta { get { return After - Before; } } }

        public static string[] Write(DiskIndex ix, List<LeftoverGroup> groups, string folder = null, string stampOverride = null)
        {
            folder = folder ?? Dir; Directory.CreateDirectory(folder);
            string letter = ix.Drive.TrimEnd('\\', ':'), stamp = stampOverride ?? DateTime.Now.ToString("yyyyMMdd-HHmm");
            string md = Path.Combine(folder, "almacenamiento-" + letter + "-" + stamp + ".md");
            string js = Path.ChangeExtension(md, ".json");
            string tsv = Path.Combine(folder, "instantanea-" + letter + "-" + stamp + ".tsv");

            var folders = Folders(ix);
            DateTime prevWhen; var prev = LoadPrevious(folder, letter, tsv, out prevWhen);
            var changes = prev == null ? new List<Change>() : Diff(prev, folders);

            var sbT = new StringBuilder("ruta\ten_disco\tarchivos\n");
            foreach (var f in folders) sbT.Append(f.FullPath).Append('\t').Append(f.Alloc).Append('\t').Append(f.Files).Append('\n');
            File.WriteAllText(tsv, sbT.ToString(), new UTF8Encoding(false));
            File.WriteAllText(md, Markdown(ix, groups, changes, prev != null ? prevWhen : (DateTime?)null, js), new UTF8Encoding(false));
            File.WriteAllText(js, Json(ix, groups, folders, changes, prev != null ? prevWhen : (DateTime?)null), new UTF8Encoding(false));
            Log.Write("Informe de almacenamiento de " + ix.Drive + " exportado: " + md);
            return new[] { md, js, tsv };
        }

        // Todas las carpetas de 100 MB o más (las más grandes primero)
        static List<DNode> Folders(DiskIndex ix)
        {
            var l = new List<DNode>(); var st = new Stack<DNode>(); st.Push(ix.Root);
            while (st.Count > 0)
            {
                var n = st.Pop(); if (n.Alloc < FolderMin) continue;
                l.Add(n); if (n.Kids != null) foreach (var k in n.Kids) if (k.Alloc >= FolderMin) st.Push(k);
            }
            return l.OrderByDescending(x => x.Alloc).Take(6000).ToList();
        }

        static Dictionary<string, long> LoadPrevious(string folder, string letter, string exclude, out DateTime when)
        {
            when = DateTime.MinValue;
            var last = Directory.GetFiles(folder, "instantanea-" + letter + "-*.tsv").Where(f => !f.Equals(exclude, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).LastOrDefault();
            if (last == null) return null;
            DateTime.TryParseExact(Path.GetFileNameWithoutExtension(last).Substring(("instantanea-" + letter + "-").Length), "yyyyMMdd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
            var d = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(last).Skip(1))
            {
                var p = line.Split('\t'); long v;
                if (p.Length >= 2 && long.TryParse(p[1], out v)) d[p[0]] = v;
            }
            return d;
        }

        // Qué creció o bajó; si casi todo el cambio de una carpeta viene de una sola subcarpeta, se muestra la subcarpeta
        static List<Change> Diff(Dictionary<string, long> prev, List<DNode> now)
        {
            var map = new Dictionary<string, Change>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in now) { long b; bool had = prev.TryGetValue(n.FullPath, out b); map[n.FullPath] = new Change { Path = n.FullPath, Before = had ? b : 0, After = n.Alloc, New = !had }; }
            foreach (var kv in prev) if (!map.ContainsKey(kv.Key)) map[kv.Key] = new Change { Path = kv.Key, Before = kv.Value, After = 0 };
            var changes = map.Values.Where(c => Math.Abs(c.Delta) >= 300L * 1048576).ToList();
            return changes.Where(c => !changes.Any(ch => ch != c && ch.Path.StartsWith(c.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                    && Math.Sign(ch.Delta) == Math.Sign(c.Delta) && Math.Abs(ch.Delta) >= Math.Abs(c.Delta) * 0.8))
                .OrderByDescending(c => c.Delta).ToList();
        }

        static string Verdict(string path, bool dir)
        {
            var k = Known.Explain(path, dir);
            return k == null ? "" : k.VerdictText + ": " + k.Title;
        }

        static string Markdown(DiskIndex ix, List<LeftoverGroup> groups, List<Change> changes, DateTime? prevWhen, string jsonPath)
        {
            var sb = new StringBuilder();
            long used = ix.Total - ix.Free;
            sb.AppendLine("# Informe de almacenamiento de " + ix.Drive.TrimEnd('\\') + " · PulsoPC");
            sb.AppendLine();
            sb.AppendLine("> **Para la IA:** sos un técnico de Windows. Con estos datos ayudame a liberar espacio en " + ix.Drive.TrimEnd('\\') + " sin romper nada. Clasificá lo que encuentres en:");
            sb.AppendLine("> 1. **Borrar ya**: seguro, se regenera solo o es basura (decí cómo: comando, sección de PulsoPC o papelera).");
            sb.AppendLine("> 2. **Mover a otro disco**: cosas grandes que sirven pero no necesitan estar acá (mirá el espacio libre de las otras unidades abajo) y cómo moverlas sin romper el programa.");
            sb.AppendLine("> 3. **Revisar conmigo**: depende de si lo uso.");
            sb.AppendLine("> 4. **No tocar**.");
            sb.AppendLine("> Ordená por GB liberados, aclará el riesgo de cada paso y no inventes datos. Si algo creció mucho desde el informe anterior, explicá qué lo causó. Los datos completos están en `" + jsonPath + "`.");
            sb.AppendLine();
            sb.AppendLine("Generado: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · método: " + ix.Method + " (" + ix.Elapsed.TotalSeconds.ToString("0") + " s)" + (Program.IsAdmin ? "" : " · sin administrador: algunas carpetas del sistema no se pudieron leer"));
            sb.AppendLine();
            sb.AppendLine("## Unidades");
            sb.AppendLine("| Unidad | Total | Usado | Libre |"); sb.AppendLine("|---|---:|---:|---:|");
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady) sb.AppendLine("| " + d.Name.TrimEnd('\\') + " " + d.VolumeLabel + " (" + d.DriveFormat + ") | " + Fmt.Bytes(d.TotalSize) + " | " + Fmt.Bytes(d.TotalSize - d.TotalFreeSpace) + " | " + Fmt.Bytes(d.TotalFreeSpace) + " |"); } catch { }
            }
            sb.AppendLine();
            sb.AppendLine(ix.Drive.TrimEnd('\\') + ": " + ix.ScannedFiles.ToString("N0") + " archivos en " + ix.ScannedDirs.ToString("N0") + " carpetas · " + Fmt.Bytes(ix.Root.Alloc) + " en disco de " + Fmt.Bytes(used) + " usados"
                + (ix.HardLinks > 0 ? " · " + ix.HardLinks.ToString("N0") + " enlaces duros (su espacio cuenta una vez)" : "") + (ix.DeniedDirs > 0 ? " · " + ix.DeniedDirs.ToString("N0") + " carpetas sin acceso" : ""));
            sb.AppendLine();

            sb.AppendLine("## Qué cambió desde el informe anterior");
            if (prevWhen == null) sb.AppendLine("Es el primer informe de esta unidad: el próximo va a comparar contra este.");
            else if (changes.Count == 0) sb.AppendLine("Sin cambios de 300 MB o más desde el " + prevWhen.Value.ToString("dd/MM/yyyy HH:mm") + ".");
            else
            {
                sb.AppendLine("Comparado con el " + prevWhen.Value.ToString("dd/MM/yyyy HH:mm") + " (cambios de 300 MB o más, en la carpeta más específica):");
                sb.AppendLine("| Cambio | Antes | Ahora | Carpeta | Qué es |"); sb.AppendLine("|---:|---:|---:|---|---|");
                foreach (var c in changes.Where(x => x.Delta > 0).Take(30)) sb.AppendLine("| +" + Fmt.Bytes(c.Delta) + " | " + (c.New ? "(nueva o < 100 MB)" : Fmt.Bytes(c.Before)) + " | " + Fmt.Bytes(c.After) + " | " + c.Path + " | " + Verdict(c.Path, true) + " |");
                foreach (var c in changes.Where(x => x.Delta < 0).OrderBy(x => x.Delta).Take(10)) sb.AppendLine("| −" + Fmt.Bytes(-c.Delta) + " | " + Fmt.Bytes(c.Before) + " | " + (c.After == 0 ? "(ya no está o < 100 MB)" : Fmt.Bytes(c.After)) + " | " + c.Path + " | " + Verdict(c.Path, true) + " |");
            }
            sb.AppendLine();

            sb.AppendLine("## Dónde está el espacio (carpetas de 1 GB o más)");
            var lines = 0;
            Action<DNode, int> walk = null;
            walk = (n, depth) =>
            {
                if (lines >= 90 || n.Kids == null) return;
                foreach (var k in n.Kids.Where(x => x.Alloc >= 1073741824L))
                {
                    if (lines >= 90) return;
                    string v = Verdict(k.FullPath, true);
                    sb.AppendLine(new string(' ', depth * 2) + "- **" + Fmt.Bytes(k.Alloc) + "** " + k.Name + "  (" + k.Files.ToString("N0") + " archivos, cambiado " + SafeDate(k.Modified) + ")" + (v.Length > 0 ? " · " + v : ""));
                    lines++;
                    if (depth < 5 && v.Length == 0) walk(k, depth + 1); // si ya se sabe qué es, no hace falta bajar más
                }
            };
            walk(ix.Root, 0);
            if (ix.Root.DirectFiles > 0) sb.AppendLine("- **" + Fmt.Bytes(ix.Root.FilesAlloc) + "** archivos sueltos en la raíz (" + string.Join(", ", ix.TopFiles.Where(f => f.Dir == ix.Root).Take(4).Select(f => f.Name + " " + Fmt.Bytes(f.Size)).ToArray()) + ")");
            sb.AppendLine();

            sb.AppendLine("## Sobrantes que detectó PulsoPC");
            if (groups == null || groups.Count == 0) sb.AppendLine("Ninguno.");
            else
            {
                sb.AppendLine("| Veredicto | Qué es | Cuántos | Tamaño | Cómo liberar |"); sb.AppendLine("|---|---|---:|---:|---|");
                foreach (var g in groups)
                    sb.AppendLine("| " + g.Info.VerdictText + " | " + g.Info.Title + " | " + g.Count.ToString("N0") + " | " + Fmt.Bytes(g.Size) + " | " + Esc(g.Info.How) + (g.Info.Command != null ? " Comando: `" + g.Info.Command + "`" : "") + " |");
                sb.AppendLine();
                foreach (var g in groups.Where(x => x.Size >= 1073741824L || x.Count > 1).Take(15))
                {
                    sb.AppendLine("- " + g.Info.Title + ": " + string.Join("; ", g.Paths.Take(5).ToArray()) + (g.Paths.Count > 5 ? " … y " + (g.Paths.Count - 5) + " más" : ""));
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Tipos de archivo que más ocupan");
            sb.AppendLine("| Tipo | En disco | Archivos |"); sb.AppendLine("|---|---:|---:|");
            foreach (var e in ix.Ext.Values.OrderByDescending(x => x.Alloc).Take(25)) sb.AppendLine("| ." + (e.Ext.Length == 0 ? "(sin extensión)" : e.Ext) + " | " + Fmt.Bytes(e.Alloc) + " | " + e.Count.ToString("N0") + " |");
            sb.AppendLine();

            sb.AppendLine("## Archivos más grandes");
            sb.AppendLine("| Tamaño | Modificado | Archivo | Qué es |"); sb.AppendLine("|---:|---|---|---|");
            foreach (var f in ix.TopFiles.Take(60)) sb.AppendLine("| " + Fmt.Bytes(f.Size) + " | " + SafeDate(f.Modified) + " | " + f.Path + " | " + Verdict(f.Path, false) + " |");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("PulsoPC no borra nada por su cuenta. Lo que se decida se hace desde PulsoPC (Espacio en disco > Sobrantes o el Mapa, que mandan a la papelera) o con los comandos indicados.");
            return sb.ToString();
        }

        static string Json(DiskIndex ix, List<LeftoverGroup> groups, List<DNode> folders, List<Change> changes, DateTime? prevWhen)
        {
            var sb = new StringBuilder("{\n");
            sb.Append(" \"generado\": ").Append(J(DateTime.Now.ToString("s"))).Append(",\n");
            sb.Append(" \"unidad\": ").Append(J(ix.Drive)).Append(", \"metodo\": ").Append(J(ix.Method)).Append(", \"segundos\": ").Append(ix.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append(" \"total\": ").Append(ix.Total).Append(", \"libre\": ").Append(ix.Free).Append(", \"en_disco\": ").Append(ix.Root.Alloc).Append(", \"archivos\": ").Append(ix.ScannedFiles)
              .Append(", \"carpetas\": ").Append(ix.ScannedDirs).Append(", \"enlaces_duros\": ").Append(ix.HardLinks).Append(", \"sin_acceso\": ").Append(ix.DeniedDirs).Append(",\n");
            sb.Append(" \"unidades\": [");
            bool first = true;
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType != DriveType.Fixed || !d.IsReady) continue; sb.Append(first ? "" : ", ").Append("{\"unidad\": ").Append(J(d.Name)).Append(", \"total\": ").Append(d.TotalSize).Append(", \"libre\": ").Append(d.TotalFreeSpace).Append(", \"formato\": ").Append(J(d.DriveFormat)).Append("}"); first = false; } catch { }
            }
            sb.Append("],\n");
            sb.Append(" \"comparado_con\": ").Append(prevWhen == null ? "null" : J(prevWhen.Value.ToString("s"))).Append(",\n");
            sb.Append(" \"cambios\": [\n");
            sb.Append(string.Join(",\n", changes.Select(c => "  {\"ruta\": " + J(c.Path) + ", \"antes\": " + c.Before + ", \"ahora\": " + c.After + ", \"diferencia\": " + c.Delta + ", \"nueva\": " + (c.New ? "true" : "false") + "}").ToArray()));
            sb.Append("\n ],\n \"carpetas\": [\n");
            sb.Append(string.Join(",\n", folders.Select(f =>
            {
                var k = Known.Explain(f.FullPath, true);
                return "  {\"ruta\": " + J(f.FullPath) + ", \"en_disco\": " + f.Alloc + ", \"tamano\": " + f.Size + ", \"archivos\": " + f.Files + ", \"modificado\": " + J(SafeDate(f.Modified))
                    + (k != null ? ", \"que_es\": " + J(k.Title) + ", \"veredicto\": " + J(k.VerdictText) : "") + "}";
            }).ToArray()));
            sb.Append("\n ],\n \"tipos\": [\n");
            sb.Append(string.Join(",\n", ix.Ext.Values.OrderByDescending(x => x.Alloc).Take(80).Select(e => "  {\"extension\": " + J(e.Ext) + ", \"en_disco\": " + e.Alloc + ", \"tamano\": " + e.Size + ", \"archivos\": " + e.Count + "}").ToArray()));
            sb.Append("\n ],\n \"archivos_grandes\": [\n");
            sb.Append(string.Join(",\n", ix.TopFiles.Take(500).Select(f =>
            {
                var k = Known.Explain(f.Path, false);
                return "  {\"ruta\": " + J(f.Path) + ", \"tamano\": " + f.Size + ", \"modificado\": " + J(SafeDate(f.Modified)) + (k != null ? ", \"que_es\": " + J(k.Title) + ", \"veredicto\": " + J(k.VerdictText) : "") + "}";
            }).ToArray()));
            sb.Append("\n ],\n \"sobrantes\": [\n");
            sb.Append(string.Join(",\n", (groups ?? new List<LeftoverGroup>()).Select(g =>
                "  {\"que_es\": " + J(g.Info.Title) + ", \"veredicto\": " + J(g.Info.VerdictText) + ", \"tamano\": " + g.Size + ", \"cuantos\": " + g.Count
                + ", \"explicacion\": " + J(g.Info.What) + ", \"como_liberar\": " + J(g.Info.How) + ", \"comando\": " + J(g.Info.Command) + ", \"seccion\": " + J(g.Info.Page)
                + ", \"papelera\": " + (g.AllRecyclable ? "true" : "false") + ", \"rutas\": [" + string.Join(", ", g.Paths.Take(200).Select(J).ToArray()) + "]}").ToArray()));
            sb.Append("\n ]\n}\n");
            return sb.ToString();
        }

        static string SafeDate(long ft) { try { return ft > 0 ? DateTime.FromFileTime(ft).ToString("yyyy-MM-dd") : ""; } catch { return ""; } }
        static string Esc(string s) { return (s ?? "").Replace("|", "/").Replace("\n", " "); }

        static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
