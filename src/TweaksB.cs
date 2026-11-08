using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace PulsoPC
{
    static partial class Tweaks
    {
        static readonly string[] SuggestNames = { "SilentInstalledAppsEnabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled", "PreInstalledAppsEnabled", "OemPreInstalledAppsEnabled", "ContentDeliveryAllowed",
            "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-310093Enabled" };
        const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string GameStore = @"System\GameConfigStore";
        const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
        const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
        const string ExplorerPol = @"Software\Policies\Microsoft\Windows\Explorer";
        const string AppPriv = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";

        static void AddPrivacy(List<Tweak> l)
        {
            l.Add(new Tweak
            {
                Id = "dvr", Title = "Desactivar la grabación en segundo plano de Xbox Game Bar", Recommended = true, NeedsAdmin = false,
                Desc = "Detiene la captura de juegos que Windows deja preparada y que consume CPU y disco aunque no juegues.",
                IsApplied = () => Reg.GetInt(Registry.CurrentUser, GameStore, "GameDVR_Enabled", 1) == 0 && Reg.GetInt(Registry.CurrentUser, GameDvr, "AppCaptureEnabled", 1) == 0,
                Apply = () => { SetDwordRemember("dvr", Registry.CurrentUser, GameStore, "GameDVR_Enabled", 0); SetDwordRemember("dvr", Registry.CurrentUser, GameDvr, "AppCaptureEnabled", 0); },
                Revert = () => { RestoreDword("dvr", Registry.CurrentUser, GameStore, "GameDVR_Enabled", 1); RestoreDword("dvr", Registry.CurrentUser, GameDvr, "AppCaptureEnabled", 1); }
            });
            l.Add(new Tweak
            {
                Id = "web", Title = "Quitar la búsqueda web del menú Inicio", Recommended = true, NeedsAdmin = true,
                Desc = "El menú Inicio deja de consultar Internet al buscar y responde más rápido con tus archivos y programas.",
                IsApplied = () => Reg.GetInt(Registry.CurrentUser, ExplorerPol, "DisableSearchBoxSuggestions", 0) == 1,
                Apply = () => { SetDwordRemember("web", Registry.CurrentUser, SearchKey, "BingSearchEnabled", 0); SetDwordRemember("web", Registry.CurrentUser, SearchKey, "CortanaConsent", 0); SetDwordRemember("web", Registry.CurrentUser, ExplorerPol, "DisableSearchBoxSuggestions", 1); },
                Revert = () => { RestoreDword("web", Registry.CurrentUser, SearchKey, "BingSearchEnabled", 1); RestoreDword("web", Registry.CurrentUser, SearchKey, "CortanaConsent", -1); RestoreDword("web", Registry.CurrentUser, ExplorerPol, "DisableSearchBoxSuggestions", -1); }
            });
            l.Add(new Tweak
            {
                Id = "sugg", Title = "Quitar sugerencias y aplicaciones promocionadas", Recommended = true, NeedsAdmin = false,
                Desc = "Detiene los anuncios en Inicio y la instalación automática de aplicaciones promocionadas.",
                IsApplied = () => Reg.GetInt(Registry.CurrentUser, Cdm, "ContentDeliveryAllowed", 1) == 0 && Reg.GetInt(Registry.CurrentUser, Cdm, "SystemPaneSuggestionsEnabled", 1) == 0,
                Apply = () => { foreach (var n in SuggestNames) SetDwordRemember("sugg", Registry.CurrentUser, Cdm, n, 0); },
                Revert = () => { foreach (var n in SuggestNames) RestoreDword("sugg", Registry.CurrentUser, Cdm, n, 1); }
            });
            l.Add(new Tweak
            {
                Id = "bg", Title = "Impedir que las apps de la Tienda se ejecuten en segundo plano", Recommended = false, NeedsAdmin = true, Risk = "Moderado",
                Desc = "Ahorra memoria y CPU, pero algunas apps de la Tienda (Correo, Calendario) dejan de avisar si están cerradas.",
                IsApplied = () => Reg.GetInt(Registry.LocalMachine, AppPriv, "LetAppsRunInBackground", 0) == 2,
                Apply = () => SetDwordRemember("bg", Registry.LocalMachine, AppPriv, "LetAppsRunInBackground", 2),
                Revert = () => RestoreDword("bg", Registry.LocalMachine, AppPriv, "LetAppsRunInBackground", -1)
            });
        }
    }
}
