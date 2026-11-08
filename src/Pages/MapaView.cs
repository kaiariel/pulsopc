using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulsoPC
{
    // Mapa del disco: un solo escaneo de toda la unidad, árbol desplegable y panel por tipo de archivo
    class MapaView : UserControl
    {
        readonly ComboBox cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UI.Body };
        readonly Button btnScan, btnStop, btnOpen, btnCopy, btnDel, btnExport;
        readonly Label lblStatus, lblInfo, lblHint;
        readonly Card kPanel; readonly Label kTitle, kText; readonly Button kBtn;
        readonly TreeList tree = new TreeList();
        readonly ExtList ext = new ExtList();
        readonly System.Windows.Forms.Timer prog = new System.Windows.Forms.Timer { Interval = 300 };
        CancellationTokenSource cts; DiskIndex ix, scanning;
        public static string AutoScan; // solo para las capturas automáticas
        readonly ToolTip tip = new ToolTip();

        public MapaView()
        {
            BackColor = UI.Bg; DoubleBuffered = true;
            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(46), BackColor = UI.Bg };
            var lu = UI.Lbl("Unidad:", UI.Body, UI.Mute); lu.Location = new Point(0, UI.S(14));
            cmb.Location = new Point(UI.S(58), UI.S(10)); cmb.Width = UI.S(220);
            btnScan = UI.Btn("Analizar", BtnKind.Primary, UI.S(110)); btnScan.Location = new Point(UI.S(290), UI.S(6)); btnScan.Click += (s, e) => Scan();
            btnStop = UI.Btn("Detener"); btnStop.Location = new Point(UI.S(408), UI.S(6)); btnStop.Visible = false; btnStop.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            lblStatus = UI.Lbl("Elegí una unidad y tocá Analizar. Recorre todo el disco una sola vez.", UI.Body, UI.Mute); lblStatus.Location = new Point(UI.S(410), UI.S(14));
            btnExport = UI.Btn("Exportar informe para IA", BtnKind.Secondary); btnExport.Location = new Point(UI.S(408), UI.S(6)); btnExport.Visible = false; btnExport.Click += (s, e) => Export();
            top.Controls.Add(lu); top.Controls.Add(cmb); top.Controls.Add(btnScan); top.Controls.Add(btnStop); top.Controls.Add(btnExport); top.Controls.Add(lblStatus);

            var info = new DBPanel { Dock = DockStyle.Top, Height = UI.S(28), BackColor = UI.Bg };
            lblInfo = UI.Lbl("", UI.Small, UI.Ink); lblInfo.Location = new Point(0, UI.S(4)); info.Controls.Add(lblInfo);

            // Árbol a la izquierda y tipos de archivo a la derecha (ancho fijo)
            var split = new DBPanel { Dock = DockStyle.Fill, BackColor = UI.Bg };
            var cTree = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) }; tree.Dock = DockStyle.Fill; cTree.Controls.Add(tree);
            var cExt = new Card { Dock = DockStyle.Right, Width = UI.S(345), Padding = new Padding(UI.S(2)) }; ext.Dock = DockStyle.Fill; cExt.Controls.Add(ext);
            split.Controls.Add(cTree); split.Controls.Add(new DBPanel { Dock = DockStyle.Right, Width = UI.S(12), BackColor = UI.Bg }); split.Controls.Add(cExt);

            var bar = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(54), BackColor = UI.Bg };
            btnOpen = UI.Btn("Abrir ubicación"); btnOpen.Location = new Point(0, UI.S(12)); btnOpen.Click += (s, e) => { var p = SelPath(); if (p != null) ShellOpen.Location(p); };
            btnCopy = UI.Btn("Copiar ruta"); btnCopy.Location = new Point(btnOpen.Right + UI.S(8), UI.S(12)); btnCopy.Click += (s, e) => { var p = SelPath(); if (p != null) try { Clipboard.SetText(p); } catch { } };
            btnDel = UI.Btn("Enviar a la papelera", BtnKind.Danger); btnDel.Location = new Point(btnCopy.Right + UI.S(8), UI.S(12)); btnDel.Click += (s, e) => DeleteSel();
            lblHint = UI.Lbl("Doble clic: desplegar  ·  en un tipo: ver sus archivos más grandes", UI.Small, UI.Mute);
            lblHint.Location = new Point(btnDel.Right + UI.S(16), UI.S(21));
            bar.Controls.Add(btnOpen); bar.Controls.Add(btnCopy); bar.Controls.Add(btnDel); bar.Controls.Add(lblHint);

            kPanel = new Card { Dock = DockStyle.Bottom, Height = UI.S(66), Padding = UI.P(14, 8, 14, 8) };
            kTitle = UI.Lbl("", UI.BodyBold, UI.Mute); kTitle.Location = new Point(UI.S(14), UI.S(8));
            kText = UI.Lbl("", UI.Small, UI.Ink); kText.AutoSize = false; kText.Location = new Point(UI.S(14), UI.S(30)); kText.Height = UI.S(32);
            kBtn = UI.Btn("Abrir"); kBtn.Top = UI.S(15); kBtn.Visible = false; kBtn.Click += (s, e) => KnownAction();
            kPanel.Controls.Add(kTitle); kPanel.Controls.Add(kText); kPanel.Controls.Add(kBtn);
            kPanel.Resize += (s, e) => { kBtn.Left = kPanel.Width - kBtn.Width - UI.S(12); kText.Width = kBtn.Left - UI.S(24); };
            var kGap = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(10), BackColor = UI.Bg };

            Controls.Add(split); Controls.Add(kGap); Controls.Add(kPanel); Controls.Add(bar); Controls.Add(info); Controls.Add(top);

            tree.LoadFiles = d => DiskIndex.FilesOf(d, 1000);
            tree.SelectedIndexChanged += (s, e) => UpdateButtons();
            tree.OpenRequested += r => { var p = SelPath(); if (p != null) ShellOpen.Location(p); };

            // Menú del clic derecho
            var menu = new ContextMenuStrip { Font = UI.Body, ShowImageMargin = false };
            var mOpen = menu.Items.Add("Abrir en el Explorador", null, (s, e) => { var p = SelPath(); if (p != null) ShellOpen.Location(p); });
            var mReveal = menu.Items.Add("Mostrar en su carpeta", null, (s, e) => { var p = SelPath(); if (p != null) ShellOpen.Reveal(p); });
            var mCopy = menu.Items.Add("Copiar ruta", null, (s, e) => { var p = SelPath(); if (p != null) try { Clipboard.SetText(p); } catch { } });
            menu.Items.Add(new ToolStripSeparator());
            var mWhat = menu.Items.Add("¿Qué es esto?", null, (s, e) => ShowWhat());
            menu.Items.Add(new ToolStripSeparator());
            var mDel = menu.Items.Add("Enviar a la papelera", null, (s, e) => DeleteSel());
            mDel.ForeColor = UI.Bad;
            mOpen.Font = UI.BodyBold;
            menu.Opening += (s, e) =>
            {
                var r = tree.Selected; string p = SelPath();
                if (r == null || p == null) { e.Cancel = true; return; }
                mOpen.Text = r.File != null ? "Abrir su carpeta en el Explorador" : "Abrir en el Explorador";
                mReveal.Visible = r.File == null && !r.Group && r.Depth > 0;
                mWhat.Enabled = !r.Group;
                mDel.Enabled = btnDel.Enabled;
            };
            tree.ContextMenuStrip = menu;
            ext.DoubleClick += (s, e) => ShowExt();
            prog.Tick += (s, e) => Progress();
            VisibleChanged += (s, e) =>
            {
                if (!Visible || cmb.Items.Count > 0) return;
                LoadDrives();
                if (AutoScan == null) return;
                for (int i = 0; i < cmb.Items.Count; i++) if (((KeyValuePair<string, string>)cmb.Items[i]).Key.Equals(AutoScan, StringComparison.OrdinalIgnoreCase)) cmb.SelectedIndex = i;
                AutoScan = null; Scan();
            };
            UpdateButtons();
        }

        void LoadDrives()
        {
            cmb.Items.Clear();
            foreach (var d in DiskIndex.Drives())
            {
                string lbl = d.Name.TrimEnd('\\') + "  " + (string.IsNullOrEmpty(d.VolumeLabel) ? "Disco local" : d.VolumeLabel) + "  (" + Fmt.Bytes(d.TotalSize - d.TotalFreeSpace) + " usados)";
                cmb.Items.Add(new KeyValuePair<string, string>(d.Name, lbl));
            }
            cmb.DisplayMember = "Value";
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        async void Scan()
        {
            if (cmb.SelectedItem == null || scanning != null) return;
            string drive = ((KeyValuePair<string, string>)cmb.SelectedItem).Key;
            cts = new CancellationTokenSource(); var ct = cts.Token;
            scanning = new DiskIndex();
            btnScan.Enabled = false; btnStop.Visible = true; btnExport.Visible = false; lblStatus.Location = new Point(btnStop.Right + UI.S(12), UI.S(14));
            tree.SetRoot(null); ext.Items.Clear(); lblInfo.Text = ""; prog.Start();
            DiskIndex res = null;
            try { var into = scanning; res = await Task.Run(() => DiskIndex.Scan(drive, ct, into)); }
            catch (Exception ex) { Log.Write("Mapa del disco falló: " + ex); }
            prog.Stop(); scanning = null;
            if (IsDisposed) return;
            btnScan.Enabled = true; btnStop.Visible = false; lblStatus.Location = new Point(btnStop.Left, UI.S(14));
            if (res == null) { lblStatus.Text = "No se pudo analizar la unidad."; return; }
            ix = res; if (!ix.Cancelled) DiskIndex.Last = ix;
            btnExport.Visible = !ix.Cancelled; if (btnExport.Visible) lblStatus.Location = new Point(btnExport.Right + UI.S(12), UI.S(14));
            tree.SetRoot(ix.Root); ext.Fill(ix);
            lblStatus.Text = (ix.Cancelled ? "Análisis detenido (resultado parcial) " : "Análisis completo ") + "en " + ix.Elapsed.TotalSeconds.ToString("0.0") + " s (" + ix.Method + ")";
            long hidden = ix.Total - ix.Free - ix.Root.Alloc;
            lblInfo.Text = "Total " + Fmt.Bytes(ix.Total) + "  ·  usado " + Fmt.Bytes(ix.Total - ix.Free) + "  ·  libre " + Fmt.Bytes(ix.Free) + "      "
                + ix.ScannedFiles.ToString("N0") + " archivos, " + ix.ScannedDirs.ToString("N0") + " carpetas"
                + (ix.HardLinks > 0 ? "  ·  " + ix.HardLinks.ToString("N0") + " enlaces duros (su espacio cuenta una vez)" : "")
                + (ix.DeniedDirs > 0 ? "  ·  " + ix.DeniedDirs.ToString("N0") + " sin acceso" : "")
                + (hidden > 1073741824L ? "  ·  " + Fmt.Bytes(hidden) + " no aparecen" : "");
            tip.SetToolTip(lblInfo, hidden > 1073741824L ? "Lo que «no aparece» está en carpetas sin acceso, instantáneas de Windows (puntos de restauración) y archivos internos del sistema de archivos." : "");
            Log.Write("Mapa del disco " + drive + ": " + ix.ScannedFiles + " archivos, " + ix.ScannedDirs + " carpetas en " + ix.Elapsed.TotalSeconds.ToString("0.0") + " s");
            UpdateButtons();
        }

        void Progress()
        {
            var s = scanning; if (s == null) return;
            string cur = s.Current ?? ""; if (cur.Length > 70) cur = "…" + cur.Substring(cur.Length - 69);
            lblStatus.Text = s.ScannedFiles.ToString("N0") + " archivos  ·  " + s.ScannedDirs.ToString("N0") + " carpetas  ·  " + Fmt.Bytes(s.ScannedBytes) + "   " + cur;
        }

        string SelPath()
        {
            var r = tree.Selected; if (r == null) return null;
            if (r.File != null) return r.File.Path;
            if (r.Extra != null) return null;
            return r.Dir.FullPath;
        }

        void UpdateButtons()
        {
            var r = tree.Selected;
            bool any = r != null && r.Extra == null;
            btnOpen.Enabled = any; btnCopy.Enabled = any;
            btnDel.Enabled = any && !r.Group && r.Depth > 0;
            ExplainSel();
        }

        // "¿Qué es esto?": explicación del elemento elegido, si PulsoPC lo conoce
        KnownInfo cur;
        void ExplainSel()
        {
            var r = tree.Selected; string path = SelPath();
            cur = path == null || r.Group ? null : Known.Explain(path, r.File == null);
            if (cur == null)
            {
                kTitle.Text = r == null ? "" : "¿Qué es esto?"; kTitle.ForeColor = UI.Mute;
                kText.Text = r == null ? "" : "PulsoPC no lo tiene identificado. Si no sabés qué es, no lo borres: buscá el nombre de la carpeta o preguntale a la IA con el Informe para IA.";
                kBtn.Visible = false; return;
            }
            kTitle.Text = cur.VerdictText + "  ·  " + cur.Title;
            kTitle.ForeColor = cur.V == Verdict.Si ? UI.Good : cur.V == Verdict.Cuidado ? UI.Warn : cur.V == Verdict.No ? UI.Bad : UI.Ink;
            kText.Text = cur.What + "  " + cur.How;
            kBtn.Visible = cur.Page != null || cur.Command != null;
            if (cur.Page != null) kBtn.Text = "Abrir " + (cur.Page == "limpieza" ? "Limpieza" : cur.Page == "optimizar" ? "Optimizar" : cur.Page == "reparar" ? "Reparar" : "Proyectos");
            else if (cur.Command != null) kBtn.Text = "Copiar comando";
            kBtn.Left = kPanel.Width - kBtn.Width - UI.S(12);
        }

        // Exporta el informe completo de almacenamiento (.md, .json, .tsv) para decidir con ayuda de la IA
        async void Export()
        {
            if (ix == null) return;
            btnExport.Enabled = false; string old = lblStatus.Text; lblStatus.Text = "Buscando sobrantes y armando el informe...";
            string[] files = null; string err = null; var cur = ix;
            try { files = await Task.Run(() => StorageExport.Write(cur, Leftovers.Find(cur))); }
            catch (Exception ex) { err = ex.Message; Log.Write("Exportar almacenamiento falló: " + ex); }
            if (IsDisposed) return;
            btnExport.Enabled = true; lblStatus.Text = old;
            if (files == null) { UI.Toast(this, "No se pudo exportar: " + err); return; }
            using (var f = new ExportDoneForm(files, ix.Drive)) f.ShowDialog(FindForm());
        }

        void ShowWhat()
        {
            var r = tree.Selected; string p = SelPath(); if (r == null || p == null) return;
            var k = Known.Explain(p, r.File == null);
            string size = Fmt.Bytes(r.Disk) + " en disco" + (r.File == null && !r.Group ? ", " + r.Dir.Files.ToString("N0") + " archivos" : "");
            if (k == null) { UI.Toast(this, p + "\n" + size + "\n\nPulsoPC no lo tiene identificado. Si no sabés qué es, no lo borres: buscá el nombre o preguntale a la IA con el Informe para IA (Resumen)."); return; }
            UI.Toast(this, k.Title + "  ·  " + k.VerdictText + "\n" + p + "\n" + size + "\n\n" + k.What + "\n\nQué hacer: " + k.How + (k.Command != null ? "\n\nComando: " + k.Command : ""));
        }

        void KnownAction()
        {
            if (cur == null) return;
            if (cur.Page != null) { MainForm.Instance.Go(cur.Page); return; }
            if (cur.Command != null) try { Clipboard.SetText(cur.Command); UI.Toast(this, "Copiado: " + cur.Command); } catch { }
        }

        void DeleteSel()
        {
            var r = tree.Selected; if (r == null || r.Group || r.Extra != null || r.Depth == 0) return;
            string path = SelPath();
            if (!Safe.CanDelete(path)) { UI.Toast(this, "Esta ruta está protegida y no se puede borrar desde aquí."); return; }
            if (!UI.Confirm(this, "¿Enviar a la papelera?\n\n" + path + "\n\n" + Fmt.Bytes(r.Size) + (r.File == null ? " en " + r.Dir.Files.ToString("N0") + " archivos" : ""), "Enviar a la papelera")) return;
            string err = EspacioPage.Recycle(path);
            if (err != null) { UI.Toast(this, "No se pudo: " + err); return; }
            int i = tree.SelectedIndices[0];
            if (r.File != null) ix.RemoveFile(r.Dir, r.File.Size); else ix.RemoveDir(r.Dir);
            tree.RemoveRow(i); ext.Invalidate();
        }

        void ShowExt()
        {
            if (ix == null || ext.SelectedItems.Count == 0) return;
            var st = ext.SelectedItems[0].Tag as ExtStat; if (st == null) return;
            List<BigFile> files; ix.TopByExt.TryGetValue(st.Ext, out files);
            using (var f = new Form { Text = "Los más grandes: " + (st.Ext.Length == 0 ? "(sin extensión)" : "." + st.Ext), Size = new Size(UI.S(900), UI.S(520)), StartPosition = FormStartPosition.CenterParent, Font = UI.Body, BackColor = UI.Bg, ShowIcon = false, MinimizeBox = false })
            {
                var lv = new SortableList { Dock = DockStyle.Fill };
                lv.Columns.Add("Tamaño", UI.S(90), HorizontalAlignment.Right); lv.Columns.Add("Archivo", UI.S(260)); lv.Columns.Add("Carpeta", UI.S(500));
                foreach (var b in files ?? new List<BigFile>())
                {
                    var it = new ListViewItem(Fmt.Bytes(b.Size)) { Tag = b.Path }; it.SubItems[0].Tag = b.Size;
                    it.SubItems.Add(b.Name); it.SubItems.Add(b.Dir.FullPath); lv.Items.Add(it);
                }
                lv.SetSort(0, false);
                var bottom = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(50), BackColor = UI.Bg };
                var open = UI.Btn("Abrir ubicación"); open.Location = new Point(UI.S(10), UI.S(8));
                open.Click += (s, e) => { if (lv.SelectedItems.Count > 0) ShellOpen.Location((string)lv.SelectedItems[0].Tag); };
                var note = UI.Lbl(files == null || files.Count == 0 ? "No hay archivos de 1 MB o más de este tipo." : "Se muestran los " + files.Count + " más grandes (de 1 MB o más). Total del tipo: " + Fmt.Bytes(st.Alloc) + " en " + st.Count.ToString("N0") + " archivos.", UI.Small, UI.Mute);
                note.Location = new Point(open.Right + UI.S(14), UI.S(18));
                bottom.Controls.Add(open); bottom.Controls.Add(note);
                f.Controls.Add(lv); f.Controls.Add(bottom);
                f.ShowDialog(FindForm());
            }
        }
    }
}
