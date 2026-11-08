using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PulsoPC
{
    enum Verdict { Si, Cuidado, No, Revisar }

    // Qué es un archivo o carpeta, si se puede borrar y cuál es la forma segura de liberar ese espacio
    class KnownInfo
    {
        public string Title = "", What = "", How = "";
        public Verdict V = Verdict.Revisar;
        public string Page;     // sección de PulsoPC que lo resuelve (limpieza, optimizar, reparar, espacio)
        public string Command;  // comando para copiar, si la solución es de consola
        public bool CanRecycle; // se puede mandar a la papelera desde PulsoPC

        public string VerdictText { get { return V == Verdict.Si ? "Se puede borrar" : V == Verdict.Cuidado ? "Con cuidado" : V == Verdict.No ? "No borrar" : "Revisalo vos"; } }
    }

    static class Known
    {
        static string N(string p) { return (p ?? "").TrimEnd('\\').ToLowerInvariant(); }
        static readonly string Win = N(Env.Win), Local = N(Env.Local), Roaming = N(Env.Roaming), Prof = N(Env.Profile), Sys = N(Env.SysDrive);

        static KnownInfo K(string title, Verdict v, string what, string how, string page = null, string cmd = null, bool recycle = false)
        {
            return new KnownInfo { Title = title, V = v, What = what, How = how, Page = page, Command = cmd, CanRecycle = recycle };
        }

        // Devuelve null si no hay nada específico que decir
        public static KnownInfo Explain(string fullPath, bool isDir)
        {
            string p = N(fullPath), name = Path.GetFileName(p), parent = N(Path.GetDirectoryName(p) ?? "");
            Func<string, bool> under = root => p == root || p.StartsWith(root + "\\");

            // --- Raíz de la unidad y archivos del sistema de archivos ---
            if (name == "pagefile.sys") return K("Archivo de paginación (memoria virtual)", Verdict.No,
                "Windows guarda acá lo que no entra en la RAM. Sin él, cuando la memoria se llena, los programas se cierran solos.",
                "No se borra. Si la memoria suele llenarse conviene dejarlo administrado por Windows; achicarlo solo tiene sentido si sobra RAM.");
            if (name == "hiberfil.sys") return K("Archivo de hibernación", Verdict.Cuidado,
                "Guarda la memoria al hibernar y también lo usa el Inicio rápido.",
                "Se libera desactivando la hibernación (Optimizar). Ojo: eso también apaga el Inicio rápido, que muchas veces conviene apagar igual.", "optimizar");
            if (name == "swapfile.sys") return K("Archivo de intercambio de apps de la Tienda", Verdict.No, "Lo usa Windows para suspender apps modernas.", "No se borra; ocupa poco.");
            if (name.StartsWith("$") && parent == Sys && !isDir && name != "$recycle.bin") return K("Archivo interno de NTFS", Verdict.No,
                "Es parte del sistema de archivos ($MFT es la tabla con todos los archivos del disco).", "Nunca se toca. Crece con la cantidad de archivos del disco.");
            if (name == "$extend" && parent == Sys) return K("Metadatos de NTFS", Verdict.No, "Registro de cambios del disco y cuotas.", "No se toca.");
            if (name == "system volume information") return K("Puntos de restauración e instantáneas", Verdict.Cuidado,
                "Copias de seguridad automáticas de Windows (Restaurar sistema) e índices internos.",
                "No se borra a mano. Para limitar cuánto ocupa: Protección del sistema > Configurar > Uso máximo. Para borrar los viejos dejando el último: Limpieza de disco > Más opciones.", null, "SystemPropertiesProtection.exe");
            if (name == "$recycle.bin") return K("Papelera de reciclaje", Verdict.Si, "Lo que mandaste a la papelera, de todos los usuarios.", "Vaciala desde Limpieza.", "limpieza");
            if (name == "windows.old" || name == "$windows.~bt" || name == "$windows.~ws" || name == "$winreagent") return K("Restos de una actualización de Windows", Verdict.Si,
                "Copia de la versión anterior de Windows o archivos de instalación. Sirve solo para volver atrás en los primeros 10 días.",
                "Se borra con Limpieza de disco > Limpiar archivos del sistema > «Instalaciones anteriores de Windows».", null, "cleanmgr.exe /d " + Sys.TrimEnd(':') + ":");
            if (name == "$sysreset" && parent == Sys) return K("Registros de un restablecimiento de Windows", Verdict.Si, "Quedan después de «Restablecer este PC».", "Se pueden borrar (necesita administrador).", null, null, true);
            if (parent == Sys + "\\users" && name.StartsWith("temp")) return K("Perfil temporal de usuario", Verdict.Si,
                "Windows lo crea cuando no puede abrir tu perfil al iniciar sesión (suele ser porque un servicio dejó tu registro cargado al apagar, sobre todo con el Inicio rápido).",
                "Se puede borrar si no estás usando ese perfil. Para que no se repita, probá desactivar el Inicio rápido (Optimizar).", "optimizar", null, true);

            // --- Windows ---
            if (under(Win + "\\winsxs")) return K("Almacén de componentes de Windows (WinSxS)", Verdict.No,
                "Versiones de los archivos del sistema. Muchos son enlaces duros: parece más grande de lo que ocupa.", "Nunca se borra a mano. Se reduce con «Limpiar componentes» en Reparar (DISM).", "reparar");
            if (under(Win + "\\installer")) return K("Caché de instaladores de programas", Verdict.No,
                "Windows guarda acá los instaladores para poder reparar o desinstalar programas.", "No se borra a mano: los programas dejan de poder desinstalarse o actualizarse.");
            if (under(Win + "\\softwaredistribution\\download")) return K("Descargas de Windows Update", Verdict.Si, "Actualizaciones ya descargadas.", "Se limpia desde Limpieza.", "limpieza");
            if (under(Win + "\\temp")) return K("Temporales de Windows", Verdict.Si, "Archivos temporales de instaladores y del sistema.", "Se limpia desde Limpieza (no toca lo usado en las últimas 24 horas).", "limpieza");
            if (under(Win + "\\minidump") || name == "memory.dmp") return K("Volcados de errores (pantallazos azules)", Verdict.Si, "Quedan cuando Windows se cuelga y se reinicia.", "Se borran desde Limpieza.", "limpieza");
            if (under(Win + "\\system32") || under(Win + "\\syswow64")) return K("Sistema de Windows", Verdict.No, "Archivos del sistema operativo.", "Nunca se tocan.");
            if (under(Win)) return K("Carpeta de Windows", Verdict.No, "Archivos del sistema operativo.", "No se borra nada a mano; para liberar espacio usá Limpieza o «Limpiar componentes» en Reparar.");
            if (under(N(Env.ProgramFiles) + "\\windowsapps")) return K("Apps de la Microsoft Store", Verdict.No, "Apps instaladas desde la Tienda (y partes de Windows).", "Desinstalá las que no uses desde Configuración > Aplicaciones.");
            if (under(N(Env.ProgramFiles)) || under(N(Env.ProgramFilesX86))) return K("Programas instalados", Verdict.No, "Archivos de programas.", "Para liberar espacio, desinstalá el programa desde Configuración > Aplicaciones; no borres sus carpetas.");
            if (under(N(Env.ProgramData) + "\\package cache")) return K("Caché de instaladores (Visual Studio y otros)", Verdict.No, "Instaladores que se usan para reparar o desinstalar.", "No se borra a mano.");

            // --- Perfil del usuario: cachés que se regeneran ---
            if (under(Local + "\\temp")) return K("Tus archivos temporales", Verdict.Si, "Temporales de programas.", "Se limpian desde Limpieza.", "limpieza");
            if (under(Local + "\\crashdumps")) return K("Volcados de programas que se cerraron solos", Verdict.Si, "Sirven solo para enviar el error al fabricante.", "Se borran desde Limpieza.", "limpieza", null, true);
            if (under(Local + "\\google\\drivefs")) return K("Caché y base de datos de Google Drive", Verdict.No,
                "Copias locales de tus archivos de Drive y su índice.", "No se borra a mano. Se mueve o se limita en Google Drive > Preferencias > Configuración > Directorio de caché.");
            if (p.Contains("\\optguideondevicemodel")) return K("Modelo de IA del navegador (Gemini Nano)", Verdict.Cuidado,
                "Chrome y otros navegadores basados en Chrome lo bajan para funciones de IA locales. Si lo borrás, lo vuelve a bajar.",
                "Para que no lo baje: chrome://flags > «optimization-guide-on-device-model» > Disabled, y después borrá la carpeta.", null, null, true);
            if (under(Prof + "\\.cache\\huggingface")) return K("Modelos de IA descargados (Hugging Face)", Verdict.Cuidado,
                "Modelos que bajaron herramientas de Python (Whisper, etc.). Se vuelven a bajar si hacen falta.", "Borrá los que no uses con el comando de abajo (te deja elegir cuáles).", null, "huggingface-cli delete-cache");
            if (under(Prof + "\\.foundry\\cache")) return K("Modelos de Foundry Local", Verdict.Cuidado, "Modelos de IA locales de Microsoft Foundry.", "Borralos con el comando de abajo.", null, "foundry cache remove");
            if (under(Prof + "\\.codex")) return K("Datos de Codex", Verdict.Cuidado, "Historial de conversaciones y configuración de Codex.", "Borrá conversaciones viejas desde Codex; no toques estos archivos con Codex abierto.");
            if (under(Roaming + "\\claude\\vm_bundles")) return K("Máquina virtual de Claude (Cowork)", Verdict.Si, "Solo la usa el modo Cowork. Si no lo usás, se puede borrar; si vuelve a hacer falta, Claude la descarga.", "Cerrá Claude y borrala.", null, null, true);
            if (name == "node_modules") return K("Dependencias de un proyecto (node_modules)", Verdict.Si, "Se regeneran con «npm install».", "Si el proyecto no lo usás hace tiempo, borralo desde Espacio > Proyectos.", "espacio");
            if (under(Local + "\\npm-cache") || under(Roaming + "\\npm-cache")) return K("Caché de npm", Verdict.Si, "Paquetes ya descargados.", "Se limpia desde Limpieza o con el comando.", "limpieza", "npm cache clean --force");
            if (p.Contains("\\.pnpm-store") || under(Local + "\\pnpm")) return K("Almacén de pnpm", Verdict.Cuidado, "Paquetes compartidos entre proyectos (con enlaces duros: ocupa menos de lo que parece).", "Limpiá lo que ningún proyecto usa con el comando.", null, "pnpm store prune");
            if (under(Local + "\\uv\\cache")) return K("Caché de uv (Python)", Verdict.Si, "Paquetes de Python descargados.", "Se limpia con el comando (mejor que borrar la carpeta: son muchos GB). Los entornos ya creados siguen funcionando.", null, "uv cache clean");
            if (under(Local + "\\pip\\cache")) return K("Caché de pip", Verdict.Si, "Paquetes de Python descargados.", "Se limpia desde Limpieza o con el comando.", "limpieza", "pip cache purge");
            if (under(Prof + "\\.gradle\\caches")) return K("Caché de Gradle", Verdict.Si, "Dependencias de proyectos Java/Android.", "Se limpia desde Limpieza.", "limpieza");
            if (under(Prof + "\\.nuget\\packages")) return K("Paquetes de NuGet", Verdict.Cuidado, "Dependencias de proyectos .NET.", "Se limpia con el comando (se vuelven a bajar al compilar).", null, "dotnet nuget locals all --clear");
            if (under(Local + "\\ms-playwright")) return K("Navegadores de Playwright", Verdict.Si, "Navegadores para pruebas automáticas.", "Se limpia desde Limpieza.", "limpieza");
            if (under(Local + "\\docker") && name.EndsWith(".vhdx")) return K("Disco de Docker", Verdict.Cuidado,
                "Todas tus imágenes y contenedores de Docker viven acá. Borrarlo a mano borra todo Docker.", "Liberá con el comando de abajo y después compactá desde Docker Desktop (Troubleshoot > Clean / Purge data).", null, "docker system prune -a");
            if (name == "ext4.vhdx") return K("Disco de una distribución Linux (WSL)", Verdict.Cuidado, "Todo lo que tenés dentro de Linux.", "No se borra a mano. Para que devuelva el espacio libre, activá el modo disperso con el comando (reemplazá <distro>).", null, "wsl --manage <distro> --set-sparse true");
            if (under(Local + "\\microsoft\\windows\\inetcache") || p.Contains("\\cache\\cache_data") || p.EndsWith("\\code cache") || p.EndsWith("\\gpucache")) return K("Caché de navegador o app", Verdict.Si, "Archivos temporales de páginas web y apps hechas con tecnología de navegador. Se regeneran solos.", "Cerrá la app y mandala a la papelera, o usá Limpieza para los navegadores conocidos.", "limpieza", null, true);
            if (under(N(Env.Downloads))) return K("Archivo grande en Descargas", Verdict.Revisar, "Algo grande que bajaste de Internet.", "Revisá instaladores (.exe, .msi, .iso) y comprimidos viejos que ya no necesites.");

            // --- Por extensión ---
            string ext = DiskIndex.ExtOf(name);
            if (!isDir && ext == "dmp") return K("Volcado de memoria", Verdict.Si, "Se genera cuando un programa se cuelga.", "Sirve solo para diagnóstico. Se puede borrar.", null, null, true);
            if (!isDir && (ext == "tmp" || ext == "temp")) return K("Archivo temporal", Verdict.Si, "Lo dejó un programa al trabajar.", "Si es viejo, se puede borrar.", null, null, true);
            if (!isDir && (ext == "log" || ext == "etl")) return K("Registro (log)", Verdict.Cuidado, "Texto que escriben los programas.", "Uno muy grande puede borrarse si el programa está cerrado.", null, null, true);
            if (!isDir && (ext == "bak" || ext == "old")) return K("Copia de seguridad vieja", Verdict.Revisar, "Copia anterior de un archivo.", "Revisá si todavía la necesitás.", null, null, true);
            if (!isDir && (ext == "iso" || ext == "msi")) return K("Imagen o instalador", Verdict.Revisar, "Instalador o imagen de disco.", "Si ya instalaste lo que era, se puede borrar.", null, null, true);
            return null;
        }
    }
}
