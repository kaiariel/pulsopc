using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PulsoPC
{
    // Vista previa del informe para IA con opciones de privacidad, copiar, guardar y abrir el chat
    class AiReportForm : Form
    {
        readonly SysSnapshot snap;
        readonly CheckBox chkAnon, chkMon, chkMap;
        readonly TextBox txt;
        readonly Label lblSize;

        public AiReportForm(SysSnapshot s)
        {
            snap = s;
            Text = "Informe para IA"; Font = UI.Body; BackColor = UI.Bg; ShowIcon = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(UI.S(980), UI.S(700)); MinimumSize = new Size(UI.S(720), UI.S(480));

            var top = new DBPanel { Dock = DockStyle.Top, Height = UI.S(96), BackColor = UI.Bg, Padding = UI.P(16, 12, 16, 0) };
            var intro = UI.Lbl("Este texto junta el diagnóstico y lo que mediste, con instrucciones para que la IA lo analice. Revisalo antes de enviarlo: PulsoPC no lo manda a ningún lado por su cuenta.", UI.Body, UI.Ink);
            intro.MaximumSize = new Size(UI.S(920), 0); intro.Location = new Point(UI.S(16), UI.S(12));
            chkAnon = new CheckBox { Text = "Anonimizar (nombre del equipo, usuario, correos y ruta del perfil)", Checked = true, AutoSize = true, Font = UI.Body, Location = new Point(UI.S(16), UI.S(56)) };
            chkMon = new CheckBox { Text = "Incluir el monitor de trabas", Checked = HangMonitor.Count > 0 || HangMonitor.Running, AutoSize = true, Font = UI.Body, Location = new Point(UI.S(470), UI.S(56)) };
            chkMap = new CheckBox { Text = "Incluir el mapa del disco", Checked = DiskIndex.Last != null, AutoSize = true, Font = UI.Body, Location = new Point(UI.S(700), UI.S(56)) };
            top.Controls.Add(intro); top.Controls.Add(chkAnon); top.Controls.Add(chkMon); top.Controls.Add(chkMap);

            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(UI.S(2)) };
            txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = UI.Mono, BorderStyle = BorderStyle.None, BackColor = Color.White, WordWrap = true };
            card.Controls.Add(txt);
            var wrap = new DBPanel { Dock = DockStyle.Fill, Padding = UI.P(16, 4, 16, 0), BackColor = UI.Bg }; wrap.Controls.Add(card);

            var bar = new DBPanel { Dock = DockStyle.Bottom, Height = UI.S(60), BackColor = UI.Bg, Padding = UI.P(16, 0, 16, 0) };
            var bCopy = UI.Btn("Copiar", BtnKind.Primary, UI.S(110)); bCopy.Location = new Point(UI.S(16), UI.S(13)); bCopy.Click += (s2, e) => { if (Copy()) UI.Toast(this, "Copiado. Pegalo en el chat con Ctrl+V."); };
            var bSave = UI.Btn("Guardar .md", BtnKind.Secondary, UI.S(120)); bSave.Location = new Point(bCopy.Right + UI.S(8), UI.S(13)); bSave.Click += (s2, e) => Save();
            var bClaude = UI.Btn("Copiar y abrir Claude", BtnKind.Secondary); bClaude.Location = new Point(bSave.Right + UI.S(8), UI.S(13)); bClaude.Click += (s2, e) => OpenChat("https://claude.ai/new");
            var bGpt = UI.Btn("Copiar y abrir ChatGPT", BtnKind.Secondary); bGpt.Location = new Point(bClaude.Right + UI.S(8), UI.S(13)); bGpt.Click += (s2, e) => OpenChat("https://chatgpt.com/");
            lblSize = UI.Lbl("", UI.Small, UI.Mute); lblSize.Location = new Point(bGpt.Right + UI.S(14), UI.S(23));
            bar.Controls.Add(bCopy); bar.Controls.Add(bSave); bar.Controls.Add(bClaude); bar.Controls.Add(bGpt); bar.Controls.Add(lblSize);

            Controls.Add(wrap); Controls.Add(bar); Controls.Add(top);
            chkAnon.CheckedChanged += (s2, e) => Rebuild(); chkMon.CheckedChanged += (s2, e) => Rebuild(); chkMap.CheckedChanged += (s2, e) => Rebuild();
            Rebuild();
        }

        void Rebuild()
        {
            txt.Text = AiReport.Build(snap, chkAnon.Checked, chkMon.Checked, chkMap.Checked).Replace("\r\n", "\n").Replace("\n", "\r\n");
            // Aproximado: una palabra en español son unos 1,4 tokens
            int words = txt.Text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            lblSize.Text = txt.Text.Length.ToString("N0") + " caracteres  ·  unos " + ((int)(words * 1.4 / 100) * 100).ToString("N0") + " tokens";
        }

        bool Copy()
        {
            try { Clipboard.SetText(txt.Text); return true; }
            catch (Exception ex) { UI.Toast(this, "No se pudo copiar: " + ex.Message); return false; }
        }

        void Save()
        {
            using (var dlg = new SaveFileDialog { Title = "Guardar informe para IA", Filter = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt", FileName = "PulsoPC_para_IA_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".md", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dlg.FileName, txt.Text, new UTF8Encoding(false)); ShellOpen.Location(dlg.FileName); Log.Write("Informe para IA guardado: " + dlg.FileName + (chkAnon.Checked ? " (anonimizado)" : "")); }
                catch (Exception ex) { UI.Toast(this, "No se pudo guardar: " + ex.Message); }
            }
        }

        void OpenChat(string url)
        {
            if (!Copy()) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            UI.Toast(this, "Copié el informe. En el chat que se abrió, pegalo con Ctrl+V y envialo.");
        }
    }
}
