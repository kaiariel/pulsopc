using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PulsoPC
{
    static partial class CleanCatalog
    {
        static readonly string[] ChromiumSub = { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "GrShaderCache", "ShaderCache" };

        // Cachés de un navegador Chromium con varios perfiles (Chrome, Edge, Brave, Vivaldi)
        static List<Target> Chromium(string userData)
        {
            var l = new List<Target>();
            if (!Directory.Exists(userData)) return l;
            try
            {
                foreach (var prof in Directory.GetDirectories(userData))
                {
                    string n = Path.GetFileName(prof);
                    if (n == "Default" || n.StartsWith("Profile ") || n == "Guest Profile")
                        foreach (var s in ChromiumSub) l.Add(T(Path.Combine(prof, s)));
                }
            }
            catch { }
            foreach (var s in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" }) l.Add(T(Path.Combine(userData, s)));
            return l;
        }

        static List<Target> ChromiumDirect(string profileDir)
        {
            var l = new List<Target>();
            foreach (var s in ChromiumSub) l.Add(T(Path.Combine(profileDir, s)));
            return l;
        }

        static List<Target> Firefox()
        {
            var l = new List<Target>();
            string root = Loc("Mozilla", "Firefox", "Profiles");
            try
            {
                if (Directory.Exists(root))
                    foreach (var p in Directory.GetDirectories(root)) { l.Add(T(Path.Combine(p, "cache2"))); l.Add(T(Path.Combine(p, "startupCache"))); }
            }
            catch { }
            return l;
        }

        static void AddBrowsers(List<CleanCat> l)
        {
            const string G = "Navegadores";
            const string note = "Cierra el navegador para liberar todo";
            l.Add(C("chrome", "Google Chrome", "Caché de páginas, código y gráficos. Las sesiones y contraseñas no se tocan.", G, () => Chromium(Loc("Google", "Chrome", "User Data")), true, false, true, note));
            l.Add(C("edge", "Microsoft Edge", "Caché de páginas, código y gráficos.", G, () => Chromium(Loc("Microsoft", "Edge", "User Data")), true, false, true, note));
            l.Add(C("brave", "Brave", "Caché de páginas, código y gráficos.", G, () => Chromium(Loc("BraveSoftware", "Brave-Browser", "User Data")), true, false, true, note));
            l.Add(C("vivaldi", "Vivaldi", "Caché de páginas, código y gráficos.", G, () => Chromium(Loc("Vivaldi", "User Data")), true, false, true, note));
            l.Add(C("opera", "Opera", "Caché de páginas, código y gráficos.", G,
                () => ChromiumDirect(Roam("Opera Software", "Opera Stable")).Concat(ChromiumDirect(Roam("Opera Software", "Opera GX Stable"))).ToList(), true, false, true, note));
            l.Add(C("firefox", "Mozilla Firefox", "Caché de páginas. Las sesiones y contraseñas no se tocan.", G, () => Firefox(), true, false, true, note));
        }

        static void AddApps(List<CleanCat> l)
        {
            const string G = "Aplicaciones";
            l.Add(C("teams", "Microsoft Teams", "Caché de la aplicación. Puede tardar un poco más en abrir la primera vez.", G,
                () => new List<Target> {
                    T(Roam("Microsoft", "Teams", "Cache")), T(Roam("Microsoft", "Teams", "Code Cache")), T(Roam("Microsoft", "Teams", "GPUCache")),
                    T(Loc("Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "EBWebView", "Default", "Cache")),
                    T(Loc("Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "EBWebView", "Default", "Code Cache")) }));
            l.Add(C("discord", "Discord", "Caché de imágenes y código.", G,
                () => new List<Target> { T(Roam("discord", "Cache")), T(Roam("discord", "Code Cache")), T(Roam("discord", "GPUCache")) }));
            l.Add(C("slack", "Slack", "Caché de la aplicación.", G,
                () => new List<Target> { T(Roam("Slack", "Cache")), T(Roam("Slack", "Code Cache")), T(Roam("Slack", "GPUCache")) }));
            l.Add(C("vscode", "Editores de código (VS Code, Cursor)", "Caché de datos y extensiones descargadas.", G,
                () => new List<Target> {
                    T(Roam("Code", "Cache")), T(Roam("Code", "CachedData")), T(Roam("Code", "CachedExtensionVSIXs")), T(Roam("Code", "Code Cache")), T(Roam("Code", "GPUCache")),
                    T(Roam("Cursor", "Cache")), T(Roam("Cursor", "CachedData")), T(Roam("Cursor", "Code Cache")), T(Roam("Cursor", "GPUCache")) }));
            l.Add(C("adobe", "Caché de medios de Adobe", "Premiere y After Effects la regeneran al abrir los proyectos (puede tardar).", G,
                () => new List<Target> {
                    T(Roam("Adobe", "Common", "Media Cache Files")), T(Roam("Adobe", "Common", "Media Cache")), T(Roam("Adobe", "Common", "Peak Files")) }, false, false, true, "Se regenera al abrir proyectos"));
            l.Add(C("shaders", "Caché de shaders de la gráfica", "NVIDIA, AMD y DirectX. Los juegos las recrean y pueden ir a tirones la primera vez.", G,
                () => new List<Target> {
                    T(Loc("NVIDIA", "DXCache")), T(Loc("NVIDIA", "GLCache")), T(Loc("AMD", "DxCache")), T(Loc("AMD", "GLCache")),
                    T(Loc("D3DSCache")), T(Loc("Microsoft", "DirectX Shader Cache")) }, false, false, true, "Tirones al abrir juegos"));
        }

        static void AddDev(List<CleanCat> l)
        {
            const string G = "Desarrollo";
            l.Add(C("npm", "Caché de npm", "Paquetes descargados. Se vuelven a bajar al instalar.", G,
                () => new List<Target> { T(Loc("npm-cache")), T(Roam("npm-cache")) }));
            l.Add(C("yarn", "Caché de Yarn", "Paquetes descargados.", G, () => new List<Target> { T(Loc("Yarn", "Cache")) }));
            l.Add(C("pip", "Caché de pip (Python)", "Paquetes descargados.", G, () => new List<Target> { T(Loc("pip", "Cache")) }));
            l.Add(C("nuget", "Caché de NuGet (.NET)", "Cachés web y de plugins.", G,
                () => new List<Target> { T(Loc("NuGet", "v3-cache")), T(Loc("NuGet", "plugins-cache")) }));
            l.Add(C("pnpm", "Almacén de pnpm", "Todos los paquetes compartidos. Se vuelven a bajar al instalar.", G,
                () => new List<Target> { T(Loc("pnpm", "store")), T(Loc("pnpm-cache")) }, false, false, true, "Vuelve a descargar"));
            l.Add(C("gradle", "Caché de Gradle", "Dependencias de Java y Android.", G,
                () => new List<Target> { T(Path.Combine(Env.Profile, ".gradle", "caches")) }, false, false, true, "Vuelve a descargar"));
            l.Add(C("playwright", "Navegadores de Playwright y Puppeteer", "Se descargan de nuevo la próxima vez que se usen.", G,
                () => new List<Target> { T(Loc("ms-playwright")), T(Path.Combine(Env.Profile, ".cache", "puppeteer")) }, false, false, true, "Vuelve a descargar"));
        }
    }
}
