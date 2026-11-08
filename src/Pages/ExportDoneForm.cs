using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PulsoPC
{
    // Después de exportar el informe de almacenamiento: dónde quedó y cómo pasárselo a la IA
    class ExportDoneForm : Form
    {
        public ExportDoneForm(string[] files, string drive)
        {
            Text = "Informe de almacenamiento listo"; Font = UI.Body; BackColor = UI.Bg; ShowIcon = false; MinimizeBox = false; MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(UI.S(720), UI.S(330));
            string md = files[0], json = files[1];
            var t = UI.Lbl("Se guardaron tres archivos:", UI.H2); t.Location = new Point(UI.S(20), UI.S(16));
            var p = UI.Lbl(Path.GetFileName(md) + "   resumen con instrucciones para la IA\n" + Path.GetFileName(json) + "   todos los datos (carpetas, tipos, archivos, sobrantes)\n" + Path.GetFileName(files[2]) + "   foto para comparar con el próximo informe\n\nEn " + Path.GetDirectoryName(md), UI.Body, UI.Ink);
            p.Location = new Point(UI.S(20), UI.S(50)); p.MaximumSize = new Size(UI.S(680), 0);
            var h = UI.Lbl("Si usás Claude Code en esta PC, copiá el pedido y pegalo: Claude lee los archivos directo, sin subir nada. Para un chat web, copiá el informe (va anonimizado) y pegalo.", UI.Small, UI.Mute);
            h.Location = new Point(UI.S(20), UI.S(180)); h.MaximumSize = new Size(UI.S(680), 0);

            string ask = "Leé el informe de almacenamiento de PulsoPC en \"" + md + "\" (los datos completos están en \"" + json + "\") y ayudame a decidir qué borrar, qué mover a otro disco y qué no tocar en " + drive.TrimEnd('\\') + ".";
            var b1 = UI.Btn("Copiar pedido para Claude Code", BtnKind.Primary); b1.Location = new Point(UI.S(20), UI.S(232));
            b1.Click += (s, e) => { try { Clipboard.SetText(ask); UI.Toast(this, "Copiado. Pegalo en Claude Code."); } catch { } };
            var b2 = UI.Btn("Copiar informe para un chat"); b2.Location = new Point(b1.Right + UI.S(8), UI.S(232));
            b2.Click += (s, e) => { try { Clipboard.SetText(AiReport.Anonymize(File.ReadAllText(md))); UI.Toast(this, "Copiado (anonimizado). Pegalo en Claude o ChatGPT."); } catch (Exception ex) { UI.Toast(this, ex.Message); } };
            var b3 = UI.Btn("Abrir el informe"); b3.Location = new Point(b2.Right + UI.S(8), UI.S(232));
            b3.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + md + "\"") { UseShellExecute = true }); } catch { } };
            var b4 = UI.Btn("Abrir carpeta"); b4.Location = new Point(UI.S(20), UI.S(278));
            b4.Click += (s, e) => ShellOpen.Reveal(md);
            var b5 = UI.Btn("Cerrar"); b5.Location = new Point(ClientSize.Width - b5.Width - UI.S(20), UI.S(278)); b5.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { t, p, h, b1, b2, b3, b4, b5 });
        }
    }
}
