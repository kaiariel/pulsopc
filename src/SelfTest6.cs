using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PulsoPC
{
    static partial class SelfTest
    {
        // ---- Fabricación de registros FILE de la MFT para probar el decodificador sin leer el disco ----
        const int R = 1024;

        static byte[] Attr(uint type, bool nonRes, byte[] value, string name = null, ushort flags = 0, long alloc = 0, long size = 0, long compSize = -1)
        {
            byte[] nm = name == null ? new byte[0] : Encoding.Unicode.GetBytes(name);
            var b = new List<byte>();
            Action<long, int> put = (v, n) => { for (int i = 0; i < n; i++) b.Add((byte)(v >> (8 * i))); };
            if (!nonRes)
            {
                int nameOff = 24, valOff = (nameOff + nm.Length + 7) & ~7, len = (valOff + value.Length + 7) & ~7;
                put(type, 4); put(len, 4); b.Add(0); b.Add((byte)(nm.Length / 2)); put(nameOff, 2); put(flags, 2); put(0, 2);
                put(value.Length, 4); put(valOff, 2); b.Add(0); b.Add(0);
                b.AddRange(nm); while (b.Count < valOff) b.Add(0); b.AddRange(value); while (b.Count < len) b.Add(0);
            }
            else
            {
                int hdr = compSize >= 0 ? 72 : 64, nameOff = hdr, runOff = (nameOff + nm.Length + 7) & ~7, len = (runOff + 8 + 7) & ~7;
                put(type, 4); put(len, 4); b.Add(1); b.Add((byte)(nm.Length / 2)); put(nameOff, 2); put(flags, 2); put(0, 2);
                put(0, 8); put(Math.Max(0, alloc / 4096 - 1), 8); put(runOff, 2); put(compSize >= 0 ? 4 : 0, 2); put(0, 4);
                put(alloc, 8); put(size, 8); put(size, 8); if (compSize >= 0) put(compSize, 8);
                b.AddRange(nm); while (b.Count < runOff) b.Add(0); b.Add(0); while (b.Count < len) b.Add(0); // lista de tramos vacía
            }
            return b.ToArray();
        }

        static byte[] StdInfo(long mod, uint attrs)
        {
            var v = new byte[48]; BitConverter.GetBytes(mod).CopyTo(v, 8); BitConverter.GetBytes(attrs).CopyTo(v, 32);
            return Attr(0x10, false, v);
        }

        static byte[] FileName(long parent, string name, byte ns)
        {
            var nm = Encoding.Unicode.GetBytes(name); var v = new byte[66 + nm.Length];
            BitConverter.GetBytes(parent | (1L << 48)).CopyTo(v, 0); v[64] = (byte)name.Length; v[65] = ns; nm.CopyTo(v, 66);
            return Attr(0x30, false, v);
        }

        // Arma el registro y aplica la protección de sectores como lo hace NTFS (marca al final de cada 512 bytes)
        static void Put(byte[] mft, int index, ushort flags, long baseRef, params byte[][] attrs)
        {
            int o = index * R; var b = new byte[R];
            Encoding.ASCII.GetBytes("FILE").CopyTo(b, 0);
            int usaOff = 0x30, usaCount = R / 512 + 1, first = 0x38;
            b[4] = (byte)usaOff; b[6] = (byte)usaCount; b[0x14] = (byte)first; BitConverter.GetBytes(flags).CopyTo(b, 0x16);
            BitConverter.GetBytes(baseRef).CopyTo(b, 0x20);
            int p = first; foreach (var a in attrs) { a.CopyTo(b, p); p += a.Length; }
            BitConverter.GetBytes(0xFFFFFFFF).CopyTo(b, p); BitConverter.GetBytes(p + 8).CopyTo(b, 0x18);
            ushort usn = 0x0007; BitConverter.GetBytes(usn).CopyTo(b, usaOff);
            for (int i = 1; i < usaCount; i++) { int pos = i * 512 - 2; b[usaOff + i * 2] = b[pos]; b[usaOff + i * 2 + 1] = b[pos + 1]; BitConverter.GetBytes(usn).CopyTo(b, pos); }
            b.CopyTo(mft, o);
        }

        static void TestMft()
        {
            // Tramos: 0x21 = 1 byte de largo y 2 de desplazamiento; el segundo tramo es relativo y negativo
            var runs = MftReader.Runs(new byte[] { 0x21, 0x18, 0x34, 0x56, 0x21, 0x04, 0x00, 0xF0, 0x01, 0x10, 0x00 }, 0, 11);
            Check("MFT: decodifica tramos absolutos, relativos negativos y dispersos", runs.Count == 3 && runs[0][0] == 0x5634 && runs[0][1] == 0x18 && runs[1][0] == 0x5634 - 4096 && runs[1][1] == 4 && runs[2][0] == -1 && runs[2][1] == 16,
                string.Join(" ", runs.Select(x => x[0] + ":" + x[1]).ToArray()));

            int n = 60; var mft = new byte[n * R];
            long t0 = new DateTime(2026, 10, 1).ToFileTime();
            Put(mft, 5, 3, 0, StdInfo(t0, 0x16), FileName(5, ".", 3));                                                     // raíz
            Put(mft, 40, 3, 0, StdInfo(t0, 0x10), FileName(5, "Docs", 1));                                                 // carpeta
            Put(mft, 41, 1, 0, StdInfo(t0, 0x20), FileName(40, "foto.jpg", 1), Attr(0x80, true, null, null, 0, 8192, 5000)); // archivo normal
            Put(mft, 42, 1, 0, StdInfo(t0, 0x20), FileName(40, "Grande.MP4", 1), Attr(0x80, true, null, null, 0, 3145728, 3145000));
            Put(mft, 44, 1, 0, StdInfo(t0, 0x20), FileName(40, "parcial.bin", 1), Attr(0x20, false, new byte[8]));       // datos en otro registro
            Put(mft, 43, 1, 44, Attr(0x80, true, null, null, 0, 8192, 7000));                                              // registro de extensión de 44
            Put(mft, 45, 1, 0, StdInfo(t0, 0x20), FileName(5, "NOMBRE~1.TXT", 2), FileName(5, "nombre largo.txt", 1), Attr(0x80, false, new byte[100]));
            Put(mft, 46, 1, 0, StdInfo(t0, 0x20), FileName(5, "link.dat", 1), FileName(40, "link.dat", 1), Attr(0x80, true, null, null, 0, 4096, 1000)); // enlace duro
            Put(mft, 47, 0, 0, StdInfo(t0, 0x20), FileName(5, "borrado.tmp", 1), Attr(0x80, true, null, null, 0, 4096, 4000)); // no en uso
            Put(mft, 48, 1, 0, StdInfo(t0, 0x220), FileName(5, "disperso.vhdx", 1), Attr(0x80, true, null, null, 0x8000, 10737418240L, 10737418240L, 65536));
            Put(mft, 49, 1, 0, StdInfo(t0, 0x20), FileName(999, "huerfano.log", 1), Attr(0x80, true, null, null, 0, 4096, 300));
            Put(mft, 50, 1, 0, StdInfo(t0, 0x20), FileName(40, "flujo.pdf", 1), Attr(0x80, true, null, null, 0, 4096, 2000), Attr(0x80, true, null, "Zone.Identifier", 0, 4096, 26));

            var t = new MftTable(n);
            for (int i = 0; i < n; i++) MftReader.ParseRecord(mft, i * R, R, i, t);
            Check("MFT: valida y repone la protección de sectores", (t.Flags[41] & MftTable.HasName) != 0 && t.Size[41] == 5000);
            var bad = (byte[])mft.Clone(); bad[41 * R + 510] ^= 0xFF;
            var tb = new MftTable(n); MftReader.ParseRecord(bad, 41 * R, R, 41, tb);
            Check("MFT: descarta un registro con un sector dañado", tb.Flags[41] == 0);

            var ix = new DiskIndex { Drive = "X:\\", Root = new DNode { Name = "X:\\", Attr = System.IO.FileAttributes.Directory } };
            MftReader.Build(t, ix);
            var docs = ix.Root.Kids == null ? null : ix.Root.Kids.FirstOrDefault(k => k.Name == "Docs");
            Check("MFT: arma las carpetas", docs != null && docs.FullPath == "X:\\Docs", docs == null ? "sin Docs" : docs.FullPath);
            if (docs == null) return;
            Check("MFT: archivos dentro de la carpeta (incluido el enlace)", docs.DirectFiles == 5 && docs.FilesSize == 5000 + 3145000 + 7000 + 2000 + 1000, docs.DirectFiles + " / " + docs.FilesSize);
            Check("MFT: datos de un registro de extensión van a su archivo", t.Size[44] == 7000 && t.Alloc[44] == 8192);
            Check("MFT: prefiere el nombre largo al de DOS", t.Ext[45] >= 0 && t.ExtNames[t.Ext[45]] == "txt" && t.Size[45] == 100 && t.Alloc[45] == 0);
            Check("MFT: un enlace duro aparece en sus dos carpetas", ix.Root.Files == 9 && ix.HardLinks == 1, ix.Root.Files + " archivos, " + ix.HardLinks + " enlace");
            long allocExpect = 8192 + 3145728 + 8192 + 0 + 4096 + 65536 + 4096 + 8192;
            Check("MFT: el espacio de un enlace duro cuenta una sola vez", ix.Root.Alloc == allocExpect && docs.FilesAlloc == 8192 + 3145728 + 8192 + 8192, ix.Root.Alloc + " de " + allocExpect);
            var tbad = new MftTable(10); tbad.Flags[8] = MftTable.InUse | MftTable.HasName; tbad.Parent[8] = 5; tbad.Alloc[8] = 500L * 1073741824; tbad.Ext[8] = -1;
            var ixb = new DiskIndex { Drive = "Y:\\", Root = new DNode { Name = "Y:\\" } }; MftReader.Build(tbad, ixb);
            Check("MFT: $BadClus no suma espacio", ixb.Root.Alloc == 0);
            Check("MFT: ignora registros borrados", ix.Root.Kids.All(k => k.Name != "borrado.tmp") && !t.Names.ContainsValue("borrado.tmp"));
            Check("MFT: archivo disperso ocupa lo comprimido, no el tamaño", t.Size[48] == 10737418240L && t.Alloc[48] == 65536);
            Check("MFT: los huérfanos quedan en la raíz", ix.Root.DirectFiles == 4 && t.Parent[49] == 999, ix.Root.DirectFiles + " en la raíz");
            Check("MFT: flujos alternativos suman al espacio, no al tamaño", t.Size[50] == 2000 && t.Alloc[50] == 8192);
            Check("MFT: extensiones en minúscula", ix.Ext.ContainsKey("mp4") && ix.Ext["mp4"].Count == 1 && ix.Ext["jpg"].Size == 5000);
            Check("MFT: nombres de los archivos grandes", ix.TopFiles.Count >= 2 && ix.TopFiles[0].Name == "disperso.vhdx" && ix.TopFiles.Any(f => f.Name == "Grande.MP4" && f.Path == "X:\\Docs\\Grande.MP4"),
                string.Join(", ", ix.TopFiles.Select(f => f.Path).ToArray()));
            long expect = 5000 + 3145000 + 7000 + 100 + 1000 + 1000 + 10737418240L + 300 + 2000; // el enlace suma tamaño en las dos carpetas
            Check("MFT: total de la unidad", ix.Root.Size == expect, ix.Root.Size + " de " + expect);
            Check("MFT: fecha de la carpeta", docs.Modified == t0);
            Info("MFT: disponible en esta sesión: " + MftReader.Available(Env.SysDrive) + (Program.IsAdmin ? "" : " (hace falta administrador)"));
        }
    }
}
