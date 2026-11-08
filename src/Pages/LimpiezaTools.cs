using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PulsoPC
{
    partial class LimpiezaPage
    {
        Label Row(DBTable t, int row, Button b)
        {
            var l = UI.Lbl("", UI.Body); l.AutoSize = false; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            t.Controls.Add(l, 0, row);
            if (b != null) { b.Anchor = AnchorStyles.Right; b.Margin = new Padding(UI.S(10), UI.S(3), 0, UI.S(3)); t.Controls.Add(b, 1, row); }
            return l;
        }

        Control BuildTools()
        {
            var card = new Card { Dock = DockStyle.Bottom, Height = UI.S(206), Padding = UI.P(16, 10, 16, 8) };
            var t = new DBTable { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, BackColor = Color.White };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, UI.S(30)));
            for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, UI.S(38)));
            var head = UI.Lbl("Archivos grandes de Windows que no se borran con la lista de arriba", UI.H2); head.Dock = DockStyle.Fill;
            t.Controls.Add(head, 0, 0); t.SetColumnSpan(head, 2);
            btnHiber = UI.Btn("Desactivar hibernación"); btnOld = UI.Btn("Liberador de disco de Windows"); btnDown = UI.Btn("Abrir Descargas");
            btnHiber.Click += (s, e) => DisableHibernate(); btnOld.Click += (s, e) => RunCleanmgr(); btnDown.Click += (s, e) => ShellOpen.Location(Env.Downloads);
            lblHiber = Row(t, 1, btnHiber); lblPage = Row(t, 2, null); lblOld = Row(t, 3, btnOld); lblDown = Row(t, 4, btnDown);
            lblHiber.Text = "Archivo de hibernación: calculando..."; lblPage.Text = "Archivo de paginación: calculando..."; lblOld.Text = "Instalación anterior de Windows: calculando..."; lblDown.Text = "Carpeta Descargas: calculando...";
            card.Controls.Add(t);
            return card;
        }

        async void RefreshTools()
        {
            long hib = 0, pag = 0, old = 0, down = 0;
            await Task.Run(() =>
            {
                hib = CleanerService.HiberfilSize(); pag = CleanerService.PagefileSize();
                string wo = Path.Combine(Env.SysDrive, "Windows.old");
                if (Directory.Exists(wo)) old = FsWalk.SizeOf(wo, CancellationToken.None);
                down = FsWalk.SizeOf(Env.Downloads, CancellationToken.None);
            });
            if (IsDisposed) return;
            lblHiber.Text = hib > 0 ? "Archivo de hibernación: " + Fmt.Bytes(hib) + "  (al desactivarla también se apaga el Inicio rápido)" : "Hibernación desactivada: no ocupa espacio.";
            btnHiber.Visible = hib > 0;
            lblPage.Text = "Archivos de paginación: " + Fmt.Bytes(pag) + "  (memoria virtual de Windows, no se recomienda tocarlos)";
            lblOld.Text = old > 0 ? "Instalación anterior de Windows (Windows.old): " + Fmt.Bytes(old) + "  (se quita con el Liberador)" : "Restos de actualizaciones: el Liberador de Windows los quita.";
            lblDown.Text = "Carpeta Descargas: " + Fmt.Bytes(down) + "  (revisa a mano lo que ya no necesites)";
        }

        void DisableHibernate()
        {
            if (!Program.IsAdmin) { UI.Toast(this, "Necesitas abrir PulsoPC como administrador."); return; }
            if (!UI.Confirm(this, "Desactivar la hibernación libera el archivo de hibernación, pero también desactiva el Inicio rápido y la opción Hibernar.\n\nSe puede volver a activar desde Optimizar.\n\n¿Continuar?", "Desactivar hibernación", false)) return;
            var r = Sh.Run("powercfg", "/hibernate off", 20000);
            Log.Write("Hibernación desactivada (código " + r.Exit + ")");
            Store.Set("hibernate.off", "1");
            RefreshTools();
        }

        void RunCleanmgr()
        {
            if (!Program.IsAdmin) { UI.Toast(this, "Necesitas abrir PulsoPC como administrador."); return; }
            if (!UI.Confirm(this, "Se abrirá el Liberador de disco de Windows con todas las categorías marcadas, incluidas la papelera, las instalaciones anteriores de Windows y los archivos de actualización.\n\nNo toca tu carpeta Descargas.\n\n¿Continuar?", "Liberador de disco", false)) return;
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
            try
            {
                using (var vc = Registry.LocalMachine.OpenSubKey(path, true))
                    foreach (var n in vc.GetSubKeyNames())
                    {
                        if (n.Equals("DownloadsFolder", StringComparison.OrdinalIgnoreCase) || n.IndexOf("ESD", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        using (var k = vc.OpenSubKey(n, true)) { if (k != null) k.SetValue("StateFlags0077", 2, RegistryValueKind.DWord); }
                    }
                var p = Process.Start("cleanmgr.exe", "/sagerun:77");
                Log.Write("Liberador de disco de Windows iniciado");
                Task.Run(() =>
                {
                    try { p.WaitForExit(); } catch { }
                    try
                    {
                        using (var vc = Registry.LocalMachine.OpenSubKey(path, true))
                            foreach (var n in vc.GetSubKeyNames()) using (var k = vc.OpenSubKey(n, true)) { if (k != null) k.DeleteValue("StateFlags0077", false); }
                    }
                    catch { }
                    Ui(() => RefreshTools());
                });
            }
            catch (Exception ex) { UI.Toast(this, "No se pudo abrir el Liberador: " + ex.Message); }
        }
    }
}
