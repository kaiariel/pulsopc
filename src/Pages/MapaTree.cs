using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PulsoPC
{
    // Una fila visible del árbol: carpeta, grupo "[N archivos]" o archivo
    class TRow
    {
        public DNode Dir;          // carpeta (o la carpeta dueña del grupo / del archivo)
        public bool Group;         // "[N archivos en esta carpeta]"
        public BigFile File;       // archivo suelto
        public string Extra;       // fila de "... y N más"
        public int Depth; public bool Expanded;
        public bool CanExpand { get { return File == null && Extra == null && (Group ? Dir.DirectFiles > 0 : (Dir.Kids != null && Dir.Kids.Count > 0) || Dir.DirectFiles > 0); } }
        public long Size { get { return File != null ? File.Size : Group ? Dir.FilesSize : Extra != null ? ExtraSize : Dir.Size; } }
        public long ExtraSize;
        // Espacio real en disco: con él se ordena y se calcula el %, como en WizTree (los archivos sueltos usan su tamaño)
        public long Disk { get { return File != null ? File.Size : Group ? Dir.FilesAlloc : Extra != null ? ExtraSize : Dir.Alloc; } }
        public long ParentDisk
        {
            get
            {
                if (File != null || Group || Extra != null) return Dir.Alloc;
                return Dir.Parent != null ? Dir.Parent.Alloc : Dir.Alloc;
            }
        }
        public string Name
        {
            get
            {
                if (File != null) return File.Name;
                if (Extra != null) return Extra;
                if (Group) return "[" + Dir.DirectFiles.ToString("N0") + (Dir.DirectFiles == 1 ? " archivo" : " archivos") + " en esta carpeta]";
                return Dir.Parent == null ? Dir.Name : Dir.Name;
            }
        }
    }

    // Lista virtual con aspecto de árbol, barra de % y columnas como WizTree
    class TreeList : ListView
    {
        public readonly List<TRow> Rows = new List<TRow>();
        public Func<DNode, List<BigFile>> LoadFiles;
        static readonly Font GlyphSmall = new Font("Segoe MDL2 Assets", 8f), GlyphIcon = new Font("Segoe MDL2 Assets", 10f);

        public TreeList()
        {
            View = View.Details; FullRowSelect = true; HideSelection = false; BorderStyle = BorderStyle.None; MultiSelect = false;
            Font = UI.Body; DoubleBuffered = true; VirtualMode = true; OwnerDraw = true; BackColor = Color.White;
            SmallImageList = new ImageList { ImageSize = new Size(1, UI.S(26)) };
            Columns.Add("Carpeta", UI.S(235)); Columns.Add("% de su carpeta", UI.S(100)); Columns.Add("Tamaño", UI.S(76), HorizontalAlignment.Right);
            Columns.Add("En disco", UI.S(76), HorizontalAlignment.Right);
            Columns.Add("Archivos", UI.S(72), HorizontalAlignment.Right); Columns.Add("Carpetas", UI.S(64), HorizontalAlignment.Right); Columns.Add("Modificado", UI.S(112));
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { Native.SetWindowTheme(Handle, "explorer", null); } catch { } }

        public void SetRoot(DNode root)
        {
            Rows.Clear();
            if (root != null) { var r = new TRow { Dir = root, Depth = 0 }; Rows.Add(r); Expand(0); }
            VirtualListSize = Rows.Count; Invalidate();
            if (Rows.Count > 0) { SelectedIndices.Clear(); SelectedIndices.Add(0); }
        }

        public TRow Selected { get { return SelectedIndices.Count == 1 && SelectedIndices[0] < Rows.Count ? Rows[SelectedIndices[0]] : null; } }

        public void Toggle(int i)
        {
            if (i < 0 || i >= Rows.Count) return;
            if (Rows[i].Expanded) Collapse(i); else Expand(i);
            VirtualListSize = Rows.Count; Invalidate();
        }

        void Expand(int i)
        {
            var r = Rows[i]; if (!r.CanExpand || r.Expanded) return;
            var add = new List<TRow>();
            if (r.Group)
            {
                var files = LoadFiles != null ? LoadFiles(r.Dir) : new List<BigFile>();
                foreach (var f in files) add.Add(new TRow { Dir = r.Dir, File = f, Depth = r.Depth + 1 });
                if (r.Dir.DirectFiles > files.Count)
                    add.Add(new TRow { Dir = r.Dir, Extra = "… y " + (r.Dir.DirectFiles - files.Count).ToString("N0") + " archivos más pequeños", ExtraSize = Math.Max(0, r.Dir.FilesSize - files.Sum(x => x.Size)), Depth = r.Depth + 1 });
            }
            else
            {
                if (r.Dir.Kids != null) foreach (var k in r.Dir.Kids) add.Add(new TRow { Dir = k, Depth = r.Depth + 1 });
                if (r.Dir.DirectFiles > 0) add.Add(new TRow { Dir = r.Dir, Group = true, Depth = r.Depth + 1 });
                add = add.OrderByDescending(x => x.Disk).ThenByDescending(x => x.Size).ToList();
            }
            r.Expanded = true;
            Rows.InsertRange(i + 1, add);
        }

        void Collapse(int i)
        {
            var r = Rows[i]; int j = i + 1;
            while (j < Rows.Count && Rows[j].Depth > r.Depth) j++;
            Rows.RemoveRange(i + 1, j - i - 1); r.Expanded = false;
        }

        // Quita una fila (y lo que tenga desplegado debajo) después de mandarla a la papelera
        public void RemoveRow(int i)
        {
            if (i <= 0 || i >= Rows.Count) return;
            int d = Rows[i].Depth, j = i + 1;
            while (j < Rows.Count && Rows[j].Depth > d) j++;
            Rows.RemoveRange(i, j - i);
            VirtualListSize = Rows.Count; Invalidate();
        }

        int Indent(TRow r) { return UI.S(6) + r.Depth * UI.S(18); }

        protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
        {
            var r = e.ItemIndex < Rows.Count ? Rows[e.ItemIndex] : null;
            var it = new ListViewItem(r == null ? "" : r.Name);
            if (r != null)
            {
                bool dir = r.File == null && !r.Group && r.Extra == null;
                it.SubItems.Add("");
                it.SubItems.Add(Fmt.Bytes(r.Size));
                it.SubItems.Add(r.File != null ? "" : r.Group ? Fmt.Bytes(r.Dir.FilesAlloc) : r.Extra != null ? "" : Fmt.Bytes(r.Dir.Alloc));
                it.SubItems.Add(dir ? r.Dir.Files.ToString("N0") : r.Group ? r.Dir.DirectFiles.ToString("N0") : "");
                it.SubItems.Add(dir ? r.Dir.Dirs.ToString("N0") : "");
                long mod = r.File != null ? r.File.Modified : r.Extra != null ? 0 : r.Dir.Modified;
                it.SubItems.Add(mod > 0 ? SafeDate(mod) : "");
                if (dir && r.Dir.Denied) it.ToolTipText = "Sin acceso: Windows no deja leer esta carpeta (su tamaño puede ser mayor).";
            }
            else for (int k = 1; k < Columns.Count; k++) it.SubItems.Add("");
            e.Item = it;
        }

        static string SafeDate(long ft) { try { return DateTime.FromFileTime(ft).ToString("dd/MM/yyyy"); } catch { return ""; } }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e) { e.DrawDefault = true; }
        protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            var r = e.ItemIndex < Rows.Count ? Rows[e.ItemIndex] : null;
            var g = e.Graphics; var b = e.Bounds;
            bool sel = SelectedIndices.Contains(e.ItemIndex);
            using (var bg = new SolidBrush(sel ? UI.AccentLt : Color.White)) g.FillRectangle(bg, b);
            if (r == null) return;
            Color ink = r.Extra != null || r.Group ? UI.Mute : (r.File == null && (r.Dir.Denied || r.Dir.Reparse)) ? UI.Mute : UI.Ink;
            if (e.ColumnIndex == 0)
            {
                int x = b.X + Indent(r);
                if (r.CanExpand) TextRenderer.DrawText(g, r.Expanded ? "" : "", GlyphSmall, new Rectangle(x, b.Y, UI.S(16), b.Height), UI.Mute, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                x += UI.S(18);
                string glyph = r.File != null || r.Group || r.Extra != null ? "" : "";
                Color gc = r.File == null && !r.Group && r.Extra == null ? ColorTranslator.FromHtml("#E8A317") : UI.Mute;
                TextRenderer.DrawText(g, glyph, GlyphIcon, new Rectangle(x, b.Y, UI.S(20), b.Height), gc, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += UI.S(22);
                string name = r.Name + (r.File == null && r.Dir.Denied ? "  (sin acceso)" : r.File == null && r.Dir.Reparse ? "  (enlace)" : "");
                TextRenderer.DrawText(g, name, r.Depth == 0 ? UI.BodyBold : UI.Body, new Rectangle(x, b.Y, b.Right - x - UI.S(4), b.Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                return;
            }
            if (e.ColumnIndex == 1)
            {
                double pct = r.ParentDisk > 0 ? Math.Max(0, Math.Min(100, r.Disk * 100.0 / r.ParentDisk)) : 0;
                var bar = new Rectangle(b.X + UI.S(4), b.Y + UI.S(6), b.Width - UI.S(10), b.Height - UI.S(12));
                using (var br = new SolidBrush(ColorTranslator.FromHtml("#EEF2F8"))) g.FillRectangle(br, bar);
                int w = (int)(bar.Width * pct / 100.0);
                if (w > 0) using (var br = new SolidBrush(ColorTranslator.FromHtml("#9DB8F5"))) g.FillRectangle(br, new Rectangle(bar.X, bar.Y, w, bar.Height));
                TextRenderer.DrawText(g, pct.ToString(pct >= 10 ? "0" : "0.0") + " %", UI.Small, bar, UI.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
                return;
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            var tb = new Rectangle(b.X + UI.S(6), b.Y, b.Width - UI.S(12), b.Height);
            TextRenderer.DrawText(g, e.SubItem.Text, UI.Body, tb, ink, flags);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var hit = HitTest(e.Location);
            if (hit.Item == null) return;
            var r = Rows[hit.Item.Index];
            // Clic derecho: primero se elige la fila de abajo del mouse, después se abre el menú
            if (e.Button == MouseButtons.Right && !SelectedIndices.Contains(hit.Item.Index)) { SelectedIndices.Clear(); SelectedIndices.Add(hit.Item.Index); }
            int gx = Columns[0].Width > 0 ? Indent(r) : 0;
            if (e.Button == MouseButtons.Left && e.X >= gx - UI.S(2) && e.X <= gx + UI.S(18) && hit.SubItem == hit.Item.SubItems[0]) Toggle(hit.Item.Index);
        }

        // Doble clic: despliega las carpetas; en un archivo, lo muestra en el Explorador
        public event Action<TRow> OpenRequested;
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e); var r = Selected; if (r == null || e.Button != MouseButtons.Left) return;
            if (r.CanExpand) Toggle(SelectedIndices[0]);
            else if (r.File != null && OpenRequested != null) OpenRequested(r);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var r = Selected; if (r == null) return; int i = SelectedIndices[0];
            if ((e.KeyCode == Keys.Right && !r.Expanded) || (e.KeyCode == Keys.Left && r.Expanded) || e.KeyCode == Keys.Enter) { Toggle(i); e.Handled = true; }
            else if (e.KeyCode == Keys.Left && r.Depth > 0)
            {
                for (int j = i - 1; j >= 0; j--) if (Rows[j].Depth < r.Depth) { SelectedIndices.Clear(); SelectedIndices.Add(j); EnsureVisible(j); break; }
                e.Handled = true;
            }
        }
    }

    // Tabla por tipo de archivo, con el color de cada tipo
    class ExtList : ListView
    {
        public static readonly Color[] Palette = {
            ColorTranslator.FromHtml("#E8710A"), ColorTranslator.FromHtml("#C5C21A"), ColorTranslator.FromHtml("#1E9E4A"), ColorTranslator.FromHtml("#8E44D4"),
            ColorTranslator.FromHtml("#2B6FE8"), ColorTranslator.FromHtml("#16B6C8"), ColorTranslator.FromHtml("#D6338A"), ColorTranslator.FromHtml("#7A8B1F"),
            ColorTranslator.FromHtml("#B5651D"), ColorTranslator.FromHtml("#5560D6"), ColorTranslator.FromHtml("#2FA88B"), ColorTranslator.FromHtml("#C0392B") };
        public static readonly Color Other = ColorTranslator.FromHtml("#9AA3B2");
        readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        public ExtList()
        {
            View = View.Details; FullRowSelect = true; HideSelection = false; BorderStyle = BorderStyle.None; MultiSelect = false;
            Font = UI.Body; DoubleBuffered = true; OwnerDraw = true; BackColor = Color.White;
            SmallImageList = new ImageList { ImageSize = new Size(1, UI.S(26)) };
            Columns.Add("Tipo", UI.S(68)); Columns.Add("Descripción", UI.S(86)); Columns.Add("%", UI.S(54), HorizontalAlignment.Right);
            Columns.Add("En disco", UI.S(70), HorizontalAlignment.Right); Columns.Add("Archivos", UI.S(56), HorizontalAlignment.Right);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { Native.SetWindowTheme(Handle, "explorer", null); } catch { } }

        public void Fill(DiskIndex ix)
        {
            BeginUpdate(); Items.Clear(); colors.Clear();
            long used = Math.Max(1, ix.Root.Alloc); int rank = 0;
            foreach (var e in ix.Ext.Values.OrderByDescending(x => x.Alloc).Take(400))
            {
                if (rank < Palette.Length) colors[e.Ext] = Palette[rank]; rank++;
                var it = new ListViewItem(e.Ext.Length == 0 ? "(sin extensión)" : "." + e.Ext) { Tag = e };
                it.SubItems.Add(TypeName(e.Ext));
                it.SubItems.Add((e.Alloc * 100.0 / used).ToString("0.0") + " %");
                it.SubItems.Add(Fmt.Bytes(e.Alloc));
                it.SubItems.Add(e.Count.ToString("N0"));
                Items.Add(it);
            }
            EndUpdate();
        }

        public Color ColorOf(string ext) { Color c; return colors.TryGetValue(ext ?? "", out c) ? c : Other; }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e) { e.DrawDefault = true; }
        protected override void OnDrawItem(DrawListViewItemEventArgs e) { }
        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            var g = e.Graphics; var b = e.Bounds; var st = e.Item.Tag as ExtStat;
            using (var bg = new SolidBrush(e.Item.Selected ? UI.AccentLt : Color.White)) g.FillRectangle(bg, b);
            int x = b.X + UI.S(6);
            if (e.ColumnIndex == 0 && st != null)
            {
                using (var br = new SolidBrush(ColorOf(st.Ext))) g.FillRectangle(br, new Rectangle(x, b.Y + (b.Height - UI.S(12)) / 2, UI.S(12), UI.S(12)));
                x += UI.S(18);
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            TextRenderer.DrawText(g, e.SubItem.Text, UI.Body, new Rectangle(x, b.Y, b.Right - x - UI.S(6), b.Height), e.ColumnIndex == 1 ? UI.Mute : UI.Ink, flags);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);

        // Descripción que Windows usa para la extensión ("Aplicación", "Archivo PNG"...)
        public static string TypeName(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return "Sin extensión";
            try
            {
                var info = new SHFILEINFO();
                SHGetFileInfo("x." + ext, 0x80, ref info, (uint)Marshal.SizeOf(info), 0x400 | 0x10);
                return string.IsNullOrWhiteSpace(info.szTypeName) ? "Archivo " + ext.ToUpperInvariant() : info.szTypeName;
            }
            catch { return "Archivo " + ext.ToUpperInvariant(); }
        }
    }
}
