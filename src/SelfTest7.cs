using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PulsoPC
{
    static partial class SelfTest
    {
        // «¿Qué es esto?» y sobrantes, con un árbol armado en memoria (no toca el disco)
        static void TestKnown()
        {
            string sys = Env.SysDrive.TrimEnd('\\');
            Func<string, bool, Verdict?> v = (p, d) => { var k = Known.Explain(p, d); return k == null ? (Verdict?)null : k.V; };
            Check("conoce: pagefile.sys no se borra", v(sys + "\\pagefile.sys", false) == Verdict.No);
            Check("conoce: hiberfil.sys con cuidado y lleva a Optimizar", v(sys + "\\hiberfil.sys", false) == Verdict.Cuidado && Known.Explain(sys + "\\hiberfil.sys", false).Page == "optimizar");
            Check("conoce: perfil temporal se puede borrar", v(sys + "\\Users\\TEMP.EQUIPO.120", true) == Verdict.Si && Known.Explain(sys + "\\Users\\TEMP.X", true).CanRecycle);
            Check("conoce: tu perfil NO es temporal", Known.Explain(Env.Profile, true) == null || Known.Explain(Env.Profile, true).Title != "Perfil temporal de usuario");
            Check("conoce: WinSxS no se borra", v(Path.Combine(Env.Win, "WinSxS", "algo"), true) == Verdict.No);
            Check("conoce: descargas de Windows Update se limpian", v(Path.Combine(Env.Win, "SoftwareDistribution", "Download"), true) == Verdict.Si);
            Check("conoce: System32 no se borra", v(Path.Combine(Env.Win, "System32", "drivers"), true) == Verdict.No);
            Check("conoce: $MFT es interno", v(sys + "\\$MFT", false) == Verdict.No);
            Check("conoce: node_modules se regenera", v(Path.Combine(Env.Profile, "proj", "node_modules"), true) == Verdict.Si);
            Check("conoce: disco de Docker con cuidado", v(Path.Combine(Env.Local, "Docker", "wsl", "disk", "docker_data.vhdx"), false) == Verdict.Cuidado);
            Check("conoce: caché de Drive no se borra a mano", v(Path.Combine(Env.Local, "Google", "DriveFS", "123"), true) == Verdict.No);
            Check("conoce: volcado .dmp", v(Path.Combine(Env.Profile, "x.dmp"), false) == Verdict.Si);
            Check("conoce: carpeta desconocida no inventa", Known.Explain(Path.Combine(Env.Profile, "MisFotos2026"), true) == null);

            // Árbol de prueba con rutas reales de este equipo pero tamaños inventados
            var ix = new DiskIndex { Drive = Env.SysDrive, Root = new DNode { Name = Env.SysDrive.TrimEnd('\\') + "\\" } };
            Func<DNode, string, long, DNode> dir = (parent, name, alloc) =>
            {
                var n = new DNode { Name = name, Parent = parent, Alloc = alloc, Size = alloc };
                if (parent.Kids == null) parent.Kids = new List<DNode>(); parent.Kids.Add(n);
                for (var p = parent; p != null; p = p.Parent) { p.Alloc += alloc; p.Size += alloc; }
                return n;
            };
            var users = dir(ix.Root, "Users", 0);
            for (int i = 0; i < 5; i++) dir(users, "TEMP.PRUEBA." + i.ToString("000"), 700000);
            var win = dir(ix.Root, Path.GetFileName(Env.Win), 0);
            var sd = dir(win, "SoftwareDistribution", 0); dir(sd, "Download", 90L * 1048576);
            var sxs = dir(win, "WinSxS", 9000L * 1048576);
            var me = users.Kids.FirstOrDefault(k => k.Name.Equals(Path.GetFileName(Env.Profile), StringComparison.OrdinalIgnoreCase)) ?? dir(users, Path.GetFileName(Env.Profile), 0);
            var proj = dir(me, "proyecto", 0); dir(proj, "node_modules", 5L * 1048576);
            var cache = dir(me, ".cache", 0); var hf = dir(cache, "huggingface", 3000L * 1048576);
            var a = dir(me, "a", 0); var b = dir(me, "b", 0);
            ix.TopFiles = new List<BigFile> {
                new BigFile { Dir = a, Name = "weights.bin", Size = 400L * 1048576 }, new BigFile { Dir = b, Name = "weights.bin", Size = 400L * 1048576 },
                new BigFile { Dir = me, Name = "crash.dmp", Size = 120L * 1048576 }, new BigFile { Dir = hf, Name = "model.bin", Size = 2900L * 1048576 } };

            var g = Leftovers.Find(ix);
            Func<string, LeftoverGroup> G = t => g.FirstOrDefault(x => x.Info.Title.StartsWith(t));
            var tp = G("Perfil temporal");
            Check("sobrantes: junta los perfiles temporales en un grupo", tp != null && tp.Count == 5 && tp.Size == 5 * 700000, tp == null ? "no" : tp.Count + " / " + tp.Size);
            Check("sobrantes: incluye las descargas de Windows Update", G("Descargas de Windows Update") != null);
            Check("sobrantes: no propone WinSxS", g.All(x => !x.Paths.Any(p => p.IndexOf("WinSxS", StringComparison.OrdinalIgnoreCase) >= 0)));
            Check("sobrantes: node_modules aunque sea chico (se agrupa)", G("Dependencias de un proyecto") != null);
            Check("sobrantes: modelos de Hugging Face sin repetir el archivo de adentro", G("Modelos de IA descargados") != null && g.All(x => !x.Paths.Any(p => p.EndsWith("model.bin"))));
            Check("sobrantes: detecta el posible duplicado", G("Posible duplicado: weights.bin") != null && G("Posible duplicado: weights.bin").Count == 2);
            Check("sobrantes: incluye el volcado", G("Volcado de memoria") != null);
            Check("sobrantes: lo borrable va primero", g.Count > 0 && g[0].Info.V == Verdict.Si);
            Check("sobrantes: encuentra carpetas por ruta", Leftovers.FindNode(ix, Path.Combine(Env.Profile, "proyecto", "node_modules")) != null);
            Info("sobrantes de prueba: " + string.Join(" | ", g.Select(x => x.Info.Title + " (" + x.Count + ", " + Fmt.Bytes(x.Size) + ")").ToArray()));

            Check("explorador: la raíz va sin comillas (antes la barra final rompía la ruta)", ShellOpen.Quote("C:\\") == "C:\\" && ShellOpen.Quote("E:") == "E:\\", ShellOpen.Quote("C:\\"));
            Check("explorador: ruta con espacios entre comillas y sin barra final", ShellOpen.Quote(@"C:\Users\Ana Perez\") == "\"" + @"C:\Users\Ana Perez" + "\"", ShellOpen.Quote(@"C:\Users\Ana Perez\"));
            var pr = ProfileHealth.Find(30);
            Info("perfiles: " + pr[0] + " sesiones temporales y " + pr[1] + " avisos de registro sin descargar en 30 días; " + pr[2] + " carpetas TEMP");
            Check("perfiles: la consulta responde", pr[0] >= 0 && pr[2] >= 0);
        }
    }
}
