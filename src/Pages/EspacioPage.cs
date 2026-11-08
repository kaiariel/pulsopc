using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace PulsoPC
{
    class EspacioPage : PageBase
    {
        readonly List<Control> views = new List<Control>();
        readonly List<Button> tabs = new List<Button>();

        public EspacioPage() : base("Espacio en disco", "Encuentra qué ocupa más sitio y decide qué sobra. Lo que borres desde aquí va a la papelera.")
        {
            var bar = new DBPanel { Dock = DockStyle.Top, Height = UI.S(48), BackColor = UI.Bg };
            string[] names = { "Mapa del disco", "Sobrantes", "Archivos grandes", "Explorador de carpetas", "Proyectos (node_modules)" };
            Control[] vs = { new MapaView(), new SobrantesView(), new LargeFilesView(), new FolderView(), new NodeModulesView() };
            int x = 0;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = UI.Btn(names[i], BtnKind.Secondary, UI.S(140)); b.Location = new Point(x, UI.S(4)); x += b.Width + UI.S(8);
                b.Click += (s, e) => Select(idx);
                bar.Controls.Add(b); tabs.Add(b);
                vs[i].Dock = DockStyle.Fill; vs[i].Visible = false; views.Add(vs[i]);
            }
            foreach (var v in views) Body.Controls.Add(v);
            Body.Controls.Add(bar);
            Select(StartTab); StartTab = 0;
        }

        public static int StartTab; // solo para las capturas automáticas

        void Select(int i)
        {
            for (int k = 0; k < views.Count; k++) { views[k].Visible = k == i; UI.Style(tabs[k], k == i ? BtnKind.Primary : BtnKind.Secondary); }
        }

        // Mover a la papelera (archivos o carpetas)
        public static string Recycle(string path)
        {
            try
            {
                if (!Safe.CanDelete(path)) return "Esta ruta está protegida y no se puede borrar desde aquí.";
                if (Directory.Exists(path)) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                Log.Write("Enviado a la papelera: " + path);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static List<string> FixedDrives()
        {
            var l = new List<string>();
            foreach (var d in DriveInfo.GetDrives()) { try { if (d.DriveType == DriveType.Fixed && d.IsReady) l.Add(d.Name); } catch { } }
            return l;
        }
    }
}
