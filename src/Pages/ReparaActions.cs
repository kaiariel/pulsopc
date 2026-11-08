using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PulsoPC
{
    static class RepairActions
    {
        static RepairAction A(string id, string title, string desc, Func<Runner, int> run, bool admin = true, string confirm = null, string tag = "")
        {
            return new RepairAction { Id = id, Title = title, Desc = desc, Run = run, NeedsAdmin = admin, Confirm = confirm, Tag = tag };
        }

        static void Rename(Runner r, string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                string old = dir + ".old";
                if (Directory.Exists(old)) Directory.Delete(old, true);
                Directory.Move(dir, old); r.Write("Renombrada: " + dir + "  ->  " + old);
            }
            catch (Exception ex) { r.Write("No se pudo renombrar " + dir + ": " + ex.Message); }
        }

        static int ResetUpdate(Runner r)
        {
            foreach (var s in new[] { "wuauserv", "cryptSvc", "bits", "msiserver" }) r.Exec("net.exe", "stop " + s);
            Rename(r, Path.Combine(Env.Win, "SoftwareDistribution")); Rename(r, Path.Combine(Env.Win, "System32", "catroot2"));
            foreach (var s in new[] { "cryptSvc", "bits", "wuauserv", "msiserver" }) r.Exec("net.exe", "start " + s);
            r.Write("Listo. Reinicia el equipo y vuelve a buscar actualizaciones.");
            return 0;
        }

        static int IconCache(Runner r)
        {
            r.Write("Cerrando el Explorador para liberar los archivos...");
            ExplorerShell.Restart(() =>
            {
                int n = 0; string ex = Path.Combine(Env.Local, "Microsoft", "Windows", "Explorer");
                try { foreach (var f in Directory.GetFiles(Env.Local, "IconCache.db")) { try { File.Delete(f); n++; } catch { } } } catch { }
                foreach (var pat in new[] { "iconcache_*.db", "thumbcache_*.db" })
                    try { foreach (var f in Directory.GetFiles(ex, pat)) { try { File.Delete(f); n++; } catch { } } } catch { }
                r.Write("Archivos de caché eliminados: " + n);
            });
            r.Write("Explorador reiniciado. Los iconos se reconstruyen solos en unos segundos.");
            return 0;
        }

        static int SearchIndex(Runner r)
        {
            Tweaks.StopSvc("WSearch");
            string edb = Path.Combine(Env.ProgramData, "Microsoft", "Search", "Data", "Applications", "Windows", "Windows.edb");
            try { if (File.Exists(edb)) { File.Delete(edb); r.Write("Índice eliminado."); } else r.Write("No había índice que borrar."); }
            catch (Exception ex) { r.Write("No se pudo borrar el índice: " + ex.Message); }
            Tweaks.StartSvc("WSearch");
            r.Write("Windows reconstruirá el índice en segundo plano; con muchos archivos puede tardar horas.");
            return 0;
        }

        static int Network(Runner r)
        {
            r.Exec("ipconfig.exe", "/flushdns"); r.Exec("netsh.exe", "winsock reset"); r.Exec("netsh.exe", "int ip reset");
            r.Write("Reinicia el equipo para terminar de aplicar el restablecimiento.");
            return 0;
        }

        static int Dism(Runner r, params string[] steps)
        {
            int code = 0;
            foreach (var s in steps) { code = r.Exec("Dism.exe", "/Online /Cleanup-Image /" + s); if (code != 0 || r.Ct.IsCancellationRequested) break; }
            return code;
        }

        public static List<RepairAction> Build()
        {
            var l = new List<RepairAction>();
            l.Add(A("sfc", "Verificar los archivos de Windows (SFC)", "Busca archivos del sistema dañados y los repara. Es lo primero que conviene probar si Windows falla o va raro.", r => r.Exec("sfc.exe", "/scannow", Encoding.Unicode), true, null, "5 a 15 min"));
            l.Add(A("dismscan", "Revisar la imagen de Windows (DISM)", "Comprueba si el almacén de componentes de Windows tiene daños. No modifica nada.", r => Dism(r, "CheckHealth", "ScanHealth"), true, null, "3 a 10 min"));
            l.Add(A("dismfix", "Reparar la imagen de Windows (DISM)", "Repara el almacén de componentes descargando lo que falte desde Windows Update. Úsalo si SFC no pudo arreglar todo.", r => Dism(r, "RestoreHealth"), true, "Necesita conexión a Internet y puede tardar bastante. ¿Continuar?", "10 a 30 min"));
            l.Add(A("cleanup", "Limpiar componentes de Windows", "Quita versiones antiguas de componentes y actualizaciones reemplazadas. Libera varios GB en equipos con años de uso.", r => Dism(r, "StartComponentCleanup"), true, null, "5 a 20 min"));
            l.Add(A("chkscan", "Revisar el disco (sin reiniciar)", "Busca errores del sistema de archivos en la unidad elegida arriba. No cambia nada.", r => r.Exec("chkdsk.exe", r.Drive + " /scan"), true, null, "2 a 10 min"));
            l.Add(A("chkfix", "Reparar el disco al reiniciar", "Programa una reparación completa (chkdsk /f). Se ejecuta durante el próximo arranque y puede tardar mucho.", r => r.Exec("chkdsk.exe", r.Drive + " /f", null, "Y"), true, "Se programará la reparación del disco para el próximo reinicio y el arranque tardará más de lo normal. ¿Continuar?", "Al reiniciar"));
            l.Add(A("defrag", "Optimizar la unidad (TRIM o desfragmentar)", "En SSD ejecuta TRIM; en discos mecánicos desfragmenta. Windows elige lo correcto para cada unidad.", r => r.Exec("defrag.exe", r.Drive + " /O /U /V"), true, null, "1 a 20 min"));
            l.Add(A("network", "Restablecer la red", "Vacía la caché DNS y restablece Winsock y TCP/IP. Arregla muchos problemas de conexión y de páginas que no cargan.", Network, true, "Puede borrar una configuración de IP fija (si usas una) y requiere reiniciar. ¿Continuar?", "Requiere reinicio"));
            l.Add(A("wu", "Restablecer Windows Update", "Detiene los servicios, renombra las carpetas de descarga y los vuelve a iniciar. Útil si las actualizaciones se atascan o fallan.", ResetUpdate, true, null, "1 min"));
            l.Add(A("store", "Restablecer la Tienda de Microsoft", "Limpia la caché de la Tienda. Se abrirá la Tienda al terminar.", r => r.Exec("wsreset.exe", ""), false, null, "1 min"));
            l.Add(A("icons", "Reconstruir la caché de iconos", "Arregla iconos en blanco, repetidos o que no se actualizan. El Explorador se reinicia unos segundos.", IconCache, false, null, "10 s"));
            l.Add(A("search", "Reconstruir el índice de búsqueda", "Si la búsqueda de Windows no encuentra archivos o consume mucho disco, borra el índice para que se cree de nuevo.", SearchIndex, true, "El índice se borrará y Windows lo reconstruirá en segundo plano. ¿Continuar?", "1 min"));
            l.Add(A("explorer", "Reiniciar el Explorador de Windows", "Cierra y vuelve a abrir la barra de tareas y el escritorio. Soluciona bloqueos de la interfaz sin reiniciar el equipo.", r => { ExplorerShell.Restart(null); r.Write("Explorador reiniciado."); return 0; }, false, null, "5 s"));
            l.Add(A("mem", "Probar la memoria RAM", "Abre el Diagnóstico de memoria de Windows. Para ejecutarlo hay que reiniciar el equipo.", r => { try { Process.Start("mdsched.exe"); r.Write("Se abrió el Diagnóstico de memoria de Windows."); return 0; } catch (Exception ex) { r.Write(ex.Message); return 1; } }, true, null, "Requiere reinicio"));
            return l;
        }
    }
}
