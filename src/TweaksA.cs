using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace PulsoPC
{
    static partial class Tweaks
    {
        const string Pers = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        static readonly uint[] AnimCodes = { 0x1003, 0x1005, 0x1007, 0x1013, 0x1015, 0x1017, 0x1019, 0x1043 };

        static void SetAnimations(bool on)
        {
            foreach (var c in AnimCodes) Native.SystemParametersInfo(c, 0, on ? (IntPtr)1 : IntPtr.Zero, 3);
            var ai = new Native.ANIMATIONINFO { cbSize = 8, iMinAnimate = on ? 1 : 0 };
            Native.SystemParametersInfo(0x49, 8, ref ai, 3);
        }

        static void AddSystem(List<Tweak> l)
        {
            l.Add(new Tweak
            {
                Id = "power", Title = "Plan de energía de alto rendimiento", Recommended = !Hw.HasBattery, NeedsAdmin = true, Risk = "Seguro",
                Desc = "Mantiene el procesador a máxima velocidad en lugar de reducirla para ahorrar energía.",
                Note = Hw.HasBattery ? "En portátiles gasta más batería." : "",
                IsApplied = () => { string g, n; return Hw.PowerPlan(out g, out n) == "alto"; },
                Apply = () =>
                {
                    string g, n; Hw.PowerPlan(out g, out n); Store.Set("tw.power.prev", g);
                    if (Sh.Run("powercfg", "/setactive " + HighPerf).Exit == 0) return;
                    string newGuid = null;
                    foreach (var src in new[] { HighPerf, Ultimate })
                    {
                        newGuid = FirstGuid(Sh.Run("powercfg", "-duplicatescheme " + src).Output);
                        if (newGuid != null) break;
                    }
                    if (newGuid == null || Sh.Run("powercfg", "/setactive " + newGuid).Exit != 0) throw new Exception("Este equipo no permite el plan de alto rendimiento.");
                },
                Revert = () => { Sh.Run("powercfg", "/setactive " + Store.Get("tw.power.prev", Balanced)); }
            });
            l.Add(new Tweak
            {
                Id = "transp", Title = "Desactivar la transparencia de Windows", Recommended = true, NeedsAdmin = false, Risk = "Seguro",
                Desc = "Quita el efecto de cristal de la barra de tareas y el menú Inicio. Libera gráficos y hace el escritorio más ágil.",
                IsApplied = () => Reg.GetInt(Registry.CurrentUser, Pers, "EnableTransparency", 1) == 0,
                Apply = () => { SetDwordRemember("transp", Registry.CurrentUser, Pers, "EnableTransparency", 0); Native.BroadcastSettingChange("ImmersiveColorSet"); },
                Revert = () => { RestoreDword("transp", Registry.CurrentUser, Pers, "EnableTransparency", 1); Native.BroadcastSettingChange("ImmersiveColorSet"); }
            });
            l.Add(new Tweak
            {
                Id = "anim", Title = "Animaciones y menús instantáneos", Recommended = true, NeedsAdmin = false, Risk = "Seguro",
                Desc = "Quita las animaciones de ventanas y menús y elimina la espera al abrir submenús.",
                IsApplied = () => { int v = 1; Native.SystemParametersInfo(0x1042, 0, ref v, 0); return v == 0; },
                Apply = () =>
                {
                    if (Store.Get("tw.anim.delay") == null) Store.Set("tw.anim.delay", Convert.ToString(Reg.Get(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "400")));
                    SetAnimations(false); Reg.SetString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0");
                },
                Revert = () => { SetAnimations(true); Reg.SetString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", Store.Get("tw.anim.delay", "400")); Store.Set("tw.anim.delay", null); }
            });
            l.Add(new Tweak
            {
                Id = "fast", Title = "Desactivar el Inicio rápido", Recommended = true, NeedsAdmin = true, Risk = "Seguro",
                Desc = "Hace que Apagar cierre Windows por completo. Evita la lentitud y los errores raros que aparecen tras días sin reiniciar de verdad.",
                Note = "El arranque puede tardar unos segundos más.",
                IsApplied = () => Reg.GetInt(Registry.LocalMachine, PowerKey, "HiberbootEnabled", 0) == 0,
                Apply = () => SetDwordRemember("fast", Registry.LocalMachine, PowerKey, "HiberbootEnabled", 0),
                Revert = () => RestoreDword("fast", Registry.LocalMachine, PowerKey, "HiberbootEnabled", 1)
            });
            l.Add(new Tweak
            {
                Id = "hiber", Title = "Desactivar la hibernación", Recommended = false, NeedsAdmin = true, Risk = "Moderado",
                Desc = "Elimina el archivo de hibernación (varios GB). Se pierde la opción Hibernar y el Inicio rápido.",
                IsApplied = () => CleanerService.HiberfilSize() == 0,
                Apply = () => { Sh.Run("powercfg", "/hibernate off"); Store.Set("hibernate.off", "1"); },
                Revert = () => { Sh.Run("powercfg", "/hibernate on"); Store.Set("hibernate.off", null); }
            });
            const string ss = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";
            l.Add(new Tweak
            {
                Id = "ss", Title = "Activar el Sensor de almacenamiento", Recommended = true, NeedsAdmin = false, Risk = "Seguro",
                Desc = "Windows limpia por su cuenta los temporales y la papelera (más de 30 días) cuando falta espacio.",
                IsApplied = () => Reg.GetInt(Registry.CurrentUser, ss, "01", 0) == 1,
                Apply = () => { SetDwordRemember("ss", Registry.CurrentUser, ss, "01", 1); SetDwordRemember("ss", Registry.CurrentUser, ss, "04", 1); SetDwordRemember("ss", Registry.CurrentUser, ss, "08", 1); SetDwordRemember("ss", Registry.CurrentUser, ss, "256", 30); },
                Revert = () => { RestoreDword("ss", Registry.CurrentUser, ss, "01", 0); RestoreDword("ss", Registry.CurrentUser, ss, "04", -1); RestoreDword("ss", Registry.CurrentUser, ss, "08", -1); RestoreDword("ss", Registry.CurrentUser, ss, "256", -1); }
            });
        }
    }
}
