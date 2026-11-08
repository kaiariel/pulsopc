using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PulsoPC
{
    class Tweak
    {
        public string Id = "", Title = "", Desc = "", Risk = "Seguro", Note = "";
        public bool Recommended, NeedsAdmin = true;
        public Func<bool> IsApplied; public Action Apply, Revert;
        public bool Applied;
    }

    static partial class Tweaks
    {
        public const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        public static readonly List<Tweak> All = Build();

        static List<Tweak> Build()
        {
            var l = new List<Tweak>();
            AddSystem(l); AddPrivacy(l); AddServices(l);
            return l;
        }

        // ---- ayudas de registro con memoria del valor anterior ----
        public static void SetDwordRemember(string id, RegistryKey root, string path, string name, int val)
        {
            string key = "tw." + id + "." + name;
            if (Store.Get(key) == null)
            {
                object old = Reg.Get(root, path, name, null);
                Store.Set(key, old is int ? ((int)old).ToString() : "-");
            }
            Reg.SetDword(root, path, name, val);
        }

        // Restaura el valor guardado; si no existia, usa el valor por defecto (o borra si defaultIfMissing es negativo)
        public static void RestoreDword(string id, RegistryKey root, string path, string name, int defaultIfMissing)
        {
            string key = "tw." + id + "." + name; string old = Store.Get(key);
            int n;
            if (old == "-") Reg.Delete(root, path, name);
            else if (old != null && int.TryParse(old, out n)) Reg.SetDword(root, path, name, n);
            else if (defaultIfMissing < 0) Reg.Delete(root, path, name);
            else Reg.SetDword(root, path, name, defaultIfMissing);
            Store.Set(key, null);
        }

        // ---- servicios ----
        const string SvcPath = @"SYSTEM\CurrentControlSet\Services\";
        public static int SvcStart(string name) { return Reg.GetInt(Registry.LocalMachine, SvcPath + name, "Start", -1); }
        public static void SetSvcStart(string name, int start)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(SvcPath + name, true)) { if (k != null) k.SetValue("Start", start, RegistryValueKind.DWord); }
        }
        public static void StopSvc(string name)
        {
            try { var sc = new ServiceController(name); if (sc.Status == ServiceControllerStatus.Running) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); } } catch { }
        }
        public static void StartSvc(string name)
        {
            try { var sc = new ServiceController(name); if (sc.Status == ServiceControllerStatus.Stopped) sc.Start(); } catch { }
        }

        // Cambia el tipo de inicio de un servicio guardando el anterior
        public static void DisableService(string id, string svc, int newStart, bool stop)
        {
            int cur = SvcStart(svc); if (cur < 0) return;
            string key = "tw." + id + "." + svc;
            if (Store.Get(key) == null) Store.Set(key, cur.ToString());
            SetSvcStart(svc, newStart);
            if (stop) StopSvc(svc);
        }
        public static void RestoreService(string id, string svc, int defaultStart)
        {
            if (SvcStart(svc) < 0) return;
            string key = "tw." + id + "." + svc; int n;
            if (!int.TryParse(Store.Get(key, ""), out n)) n = defaultStart;
            SetSvcStart(svc, n); Store.Set(key, null);
            if (n == 2) StartSvc(svc);
        }

        public static string FirstGuid(string text)
        {
            var m = Regex.Match(text ?? "", "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value : null;
        }

        public static string CreateRestorePoint(string description)
        {
            try
            {
                var mc = new System.Management.ManagementClass(Wmi.Scope("root/default") + ":SystemRestore");
                var p = mc.GetMethodParameters("CreateRestorePoint");
                p["Description"] = description; p["RestorePointType"] = 12; p["EventType"] = 100;
                var r = mc.InvokeMethod("CreateRestorePoint", p, null);
                int rc = Convert.ToInt32(r["ReturnValue"]);
                return rc == 0 ? null : "Windows respondió con el código " + rc + " (puede que la protección del sistema esté desactivada).";
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
