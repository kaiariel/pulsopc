using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    // Sobrantes: carpetas temporales, restos de Windows, cachés, volcados, duplicados e instaladores viejos, con qué hacer en cada caso
    class SobrantesView : UserControl
    {
        readonly Button btnScan, btnRecycle, btnOpen, btnGo, btnCmd, btnRun;
        readonly Label lblStatus, dTitle, dVerdict, dWhat, dHow, dPaths;
        readonly SortableList lv = new SortableList();
        List<LeftoverGroup> groups = new List<LeftoverGroup>();
        bool busy;
        public static bool AutoScan; // solo para las capturas automáticas

        public SobrantesView()
        {
            BackColor = UI.Bg; DoubleBuffered = true;
            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.Bg };
            btnScan = UI.Btn("Buscar sobrantes en " + Env.SysDrive.TrimEnd('\\'), BtnKind.Primary, UI.S(200)); btnScan.Location = new Point(0, UI.S(6)); btnScan.Click += (s, e) => Scan();
            lblStatus = UI.Lbl("Busca carpetas temporales, restos de Windows, cachés que se regeneran, volcados, duplicados e instaladores viejos. No borra nada sin preguntarte.", UI.Body, UI.Mute);
            lblStatus.Location = new Point(btnScan.Right + UI.S(14), UI.S(14));
            top.Controls.Add(btnScan); top.Controls.Add(lblStatus);

            var right = new Card { Dock = DockStyle.Right, Width = UI.S(430), Padding = UI.P(16, 14, 16, 12) };
            dTitle = UI.Lbl("Elegí una fila para ver qué es", UI.H2); dTitle.AutoSize = false; dTitle.Dock = DockStyle.Top; dTitle.Height = UI.S(50);
            dVerdict = UI.Lbl("", UI.BodyBold, UI.Mute); dVerdict.Dock = DockStyle.Top; dVerdict.AutoSize = false; dVerdict.Height = UI.S(26);
            dWhat = UI.Lbl("", UI.Body, UI.Ink); dWhat.AutoSize = false; dWhat.Dock = DockStyle.Top; dWhat.Height = UI.S(88);
            dHow = UI.Lbl("", UI.Body, ColorTranslator.FromHtml("#1D4ED8")); dHow.AutoSize = false; dHow.Dock = DockStyle.Top; dHow.Height = UI.S(88);
            dPaths = UI.Lbl("", UI.Small, UI.Mute); dPaths.AutoSize = false; dPaths.Dock = DockStyle.Fill;
            var acts = new DBFlow { Dock = DockStyle.Bottom, Height = UI.S(84), BackColor = Color.White, WrapContents = true };
            btnRecycle = UI.Btn("Enviar a la papelera", BtnKind.Danger); btnRecycle.Click += (s, e) => RecycleSel();
            btnGo = UI.Btn("Abrir la sección"); btnGo.Click += (s, e) => { var g = Sel(); if (g != null && g.Info.Page != null) MainForm.Instance.Go(g.Info.Page); };
            btnCmd = UI.Btn("Copiar comando"); btnCmd.Click += (s, e) => { var g = Sel(); if (g != null && g.Info.Command != null) try { Clipboard.SetText(g.Info.Command); UI.Toast(this, "Copiado: " + g.Info.Command + "\n\nPegalo en PowerShell o en la Terminal."); } catch { } };
            btnRun = UI.Btn("Abrir herramienta de Windows"); btnRun.Click += (s, e) => RunTool();
            btnOpen = UI.Btn("Abrir ubicación"); btnOpen.Click += (s, e) => { var g = Sel(); if (g != null && g.Paths.Count > 0) ShellOpen.Location(g.Paths[0]); };
            foreach (var b in new[] { btnRecycle, btnGo, btnCmd, btnRun, btnOpen }) { b.Margin = new Padding(0, UI.S(4), UI.S(8), UI.S(4)); acts.Controls.Add(b); }
            right.Controls.Add(dPaths); right.Controls.Add(dHow); right.Controls.Add(dWhat); right.Controls.Add(dVerdict); right.Controls.Add(dTitle); right.Controls.Add(acts);

            var left = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            lv.Dock = DockStyle.Fill; lv.MultiSelect = false;
            lv.Columns.Add("Qué es", UI.S(300)); lv.Columns.Add("Cuántos", UI.S(70), HorizontalAlignment.Right); lv.Columns.Add("Tamaño", UI.S(85), HorizontalAlignment.Right); lv.Columns.Add("¿Borrar?", UI.S(120));
            lv.SelectedIndexChanged += (s, e) => ShowSel();
            var menu = new ContextMenuStrip { Font = UI.Body, ShowImageMargin = false };
            menu.Items.Add("Abrir en el Explorador", null, (s, e) => { var g = Sel(); if (g != null && g.Paths.Count > 0) ShellOpen.Location(g.Paths[0]); });
            menu.Items.Add("Mostrar en su carpeta", null, (s, e) => { var g = Sel(); if (g != null && g.Paths.Count > 0) ShellOpen.Reveal(g.Paths[0]); });
            menu.Items.Add("Copiar rutas", null, (s, e) => { var g = Sel(); if (g != null) try { Clipboard.SetText(string.Join(Environment.NewLine, g.Paths.ToArray())); } catch { } });
            menu.Opening += (s, e) => { if (Sel() == null) e.Cancel = true; };
            lv.ContextMenuStrip = menu;
            lv.DoubleClick += (s, e) => { var g = Sel(); if (g != null && g.Paths.Count > 0) ShellOpen.Location(g.Paths[0]); };
            left.Controls.Add(lv);

            Controls.Add(left); Controls.Add(new DBPanel { Dock = DockStyle.Right, Width = UI.S(12), BackColor = UI.Bg }); Controls.Add(right); Controls.Add(top);
            ShowSel();
            VisibleChanged += (s, e) => { if (Visible && AutoScan) { AutoScan = false; Scan(); } };
        }

        LeftoverGroup Sel() { return lv.SelectedItems.Count == 1 ? lv.SelectedItems[0].Tag as LeftoverGroup : null; }

        async void Scan()
        {
            if (busy) return; busy = true; btnScan.Enabled = false; lv.Items.Clear(); ShowSel();
            string drive = Env.SysDrive;
            var ix = DiskIndex.Last;
            bool reuse = ix != null && !ix.Cancelled && ix.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase);
            lblStatus.Text = reuse ? "Usando el último mapa de " + drive + "..." : "Analizando " + drive + " (con la MFT tarda unos segundos)...";
            try
            {
                if (!reuse) { ix = await Task.Run(() => DiskIndex.Scan(drive, CancellationToken.None)); if (!ix.Cancelled) DiskIndex.Last = ix; }
                var res = await Task.Run(() => Leftovers.Find(ix));
                if (IsDisposed) return;
                groups = res; Fill();
                long free = groups.Where(g => g.Info.V == Verdict.Si).Sum(g => g.Size);
                lblStatus.Text = groups.Count + " grupos encontrados. Lo que se puede borrar sin problema suma " + Fmt.Bytes(free) + ". Elegí uno para ver qué es y qué hacer.";
                Log.Write("Sobrantes en " + drive + ": " + groups.Count + " grupos, " + Fmt.Bytes(free) + " borrables");
            }
            catch (Exception ex) { lblStatus.Text = "No se pudo completar: " + ex.Message; Log.Write("Sobrantes falló: " + ex); }
            busy = false; btnScan.Enabled = true;
        }

        void Fill()
        {
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var g in groups)
            {
                var it = new ListViewItem(g.Info.Title) { Tag = g };
                it.SubItems.Add(g.Count.ToString("N0")); it.SubItems[1].Tag = g.Count;
                it.SubItems.Add(Fmt.Bytes(g.Size)); it.SubItems[2].Tag = g.Size;
                it.SubItems.Add(g.Info.VerdictText);
                it.ForeColor = g.Info.V == Verdict.Si ? UI.Good : g.Info.V == Verdict.Cuidado ? UI.Warn : UI.Ink;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            if (lv.Items.Count > 0) lv.Items[0].Selected = true;
        }

        void ShowSel()
        {
            var g = Sel();
            btnRecycle.Visible = btnGo.Visible = btnCmd.Visible = btnRun.Visible = btnOpen.Visible = false;
            if (g == null) { dTitle.Text = "Elegí una fila para ver qué es"; dVerdict.Text = dWhat.Text = dHow.Text = dPaths.Text = ""; return; }
            var k = g.Info;
            dTitle.Text = k.Title;
            dVerdict.Text = k.VerdictText + "  ·  " + Fmt.Bytes(g.Size) + (g.Count > 1 ? " en " + g.Count.ToString("N0") + " elementos" : "");
            dVerdict.ForeColor = k.V == Verdict.Si ? UI.Good : k.V == Verdict.Cuidado ? UI.Warn : k.V == Verdict.No ? UI.Bad : UI.Ink;
            dWhat.Text = k.What; dHow.Text = "Qué hacer: " + k.How + (k.Command != null && !IsTool(k.Command) ? "\nComando: " + k.Command : "");
            dPaths.Text = string.Join("\n", g.Paths.Take(8).ToArray()) + (g.Paths.Count > 8 ? "\n… y " + (g.Paths.Count - 8) + " más" : "");
            btnRecycle.Visible = g.AllRecyclable;
            btnGo.Visible = k.Page != null; if (k.Page != null) btnGo.Text = "Abrir " + PageName(k.Page);
            btnCmd.Visible = k.Command != null && !IsTool(k.Command);
            btnRun.Visible = k.Command != null && IsTool(k.Command);
            btnOpen.Visible = g.Paths.Count > 0;
        }

        static bool IsTool(string cmd) { return cmd.StartsWith("SystemPropertiesProtection", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("cleanmgr", StringComparison.OrdinalIgnoreCase); }
        static string PageName(string p) { return p == "limpieza" ? "Limpieza" : p == "optimizar" ? "Optimizar" : p == "reparar" ? "Reparar" : p == "espacio" ? "Proyectos" : p; }

        void RunTool()
        {
            var g = Sel(); if (g == null || g.Info.Command == null) return;
            var parts = g.Info.Command.Split(new[] { ' ' }, 2);
            try { Process.Start(new ProcessStartInfo(parts[0], parts.Length > 1 ? parts[1] : "") { UseShellExecute = true }); }
            catch (Exception ex) { UI.Toast(this, "No se pudo abrir: " + ex.Message); }
        }

        void RecycleSel()
        {
            var g = Sel(); if (g == null) return;
            if (!UI.Confirm(this, "¿Enviar a la papelera " + (g.Count == 1 ? "esto" : "estos " + g.Count.ToString("N0") + " elementos") + "?\n\n" + g.Info.Title + " · " + Fmt.Bytes(g.Size) + "\n\n" + string.Join("\n", g.Paths.Take(6).ToArray()) + (g.Paths.Count > 6 ? "\n…" : ""), "Enviar a la papelera")) return;
            int ok = 0; var errs = new List<string>();
            Cursor = Cursors.WaitCursor;
            foreach (var p in g.Paths)
            {
                string err = EspacioPage.Recycle(p);
                if (err == null) ok++; else errs.Add(System.IO.Path.GetFileName(p) + ": " + err);
            }
            Cursor = Cursors.Default;
            Log.Write("Sobrantes: «" + g.Info.Title + "» " + ok + " de " + g.Paths.Count + " enviados a la papelera");
            UI.Toast(this, ok + " de " + g.Paths.Count + " enviados a la papelera." + (errs.Count > 0 ? "\n\nNo se pudieron mover:\n" + string.Join("\n", errs.Take(5).ToArray()) + (errs.Count > 5 ? "\n…" : "") : ""));
            if (ok > 0) { groups.Remove(g); DiskIndex.Last = null; Fill(); ShowSel(); }
        }
    }
}
