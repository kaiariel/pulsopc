using System;
using System.Linq;

namespace PulsoPC
{
    static partial class SelfTest
    {
        // $DATA no residente con VCN inicial y lista de tramos a mano
        static byte[] DataSeg(long startVcn, long lastVcn, byte[] runlist)
        {
            int len = (64 + runlist.Length + 7) & ~7; var b = new byte[len];
            BitConverter.GetBytes(0x80u).CopyTo(b, 0); BitConverter.GetBytes(len).CopyTo(b, 4); b[8] = 1; b[9] = 0;
            BitConverter.GetBytes((ushort)64).CopyTo(b, 10);
            BitConverter.GetBytes(startVcn).CopyTo(b, 16); BitConverter.GetBytes(lastVcn).CopyTo(b, 24); BitConverter.GetBytes((ushort)64).CopyTo(b, 32);
            long bytes = (lastVcn - startVcn + 1) * 4096; BitConverter.GetBytes(bytes).CopyTo(b, 40); BitConverter.GetBytes(bytes).CopyTo(b, 48); BitConverter.GetBytes(bytes).CopyTo(b, 56);
            runlist.CopyTo(b, 64); return b;
        }

        static byte[] ListEntry(uint type, long startVcn, long segment)
        {
            var e = new byte[32]; BitConverter.GetBytes(type).CopyTo(e, 0); BitConverter.GetBytes((ushort)32).CopyTo(e, 4); e[6] = 0; e[7] = 26;
            BitConverter.GetBytes(startVcn).CopyTo(e, 8); BitConverter.GetBytes(segment | (1L << 48)).CopyTo(e, 16); return e;
        }

        // La MFT partida en dos registros (lo que pasa cuando crece mucho): el registro 0 apunta al 3 con una lista de atributos
        static void TestMftAttrList()
        {
            const int cl = 4096;
            var volume = new byte[200 * cl];
            var recs = new byte[32 * R];
            var list = ListEntry(0x80, 0, 0).Concat(ListEntry(0x80, 8, 3)).ToArray();
            Put(recs, 0, 1, 0, Attr(0x20, false, list), DataSeg(0, 7, new byte[] { 0x11, 0x08, 0x0A, 0x00 }));   // tramo 1: 8 clústeres en LCN 10
            Put(recs, 3, 1, 0, DataSeg(8, 15, new byte[] { 0x11, 0x08, 0x64, 0x00 }));                         // tramo 2: 8 clústeres en LCN 100
            recs.CopyTo(volume, 10 * cl);
            int reads = 0;
            Func<long, int, byte[]> read = (pos, n) => { reads++; var r = new byte[n]; Buffer.BlockCopy(volume, (int)pos, r, 0, n); return r; };
            var rec0 = read(10 * cl, cl);
            MftReader.Fixup(rec0, 0, R);
            var runs = MftReader.MftRuns(rec0, R, cl, read);
            Check("MFT partida: junta los tramos de los dos registros", runs != null && runs.Count == 2 && runs[0][0] == 10 && runs[0][1] == 8 && runs[1][0] == 100 && runs[1][1] == 8,
                runs == null ? "null" : string.Join(" ", runs.Select(x => x[0] + ":" + x[1]).ToArray()));

            // Sin lista de atributos sigue funcionando como antes
            var simple = new byte[R]; var tmp = new byte[R * 1];
            Put(tmp, 0, 1, 0, DataSeg(0, 7, new byte[] { 0x11, 0x08, 0x0A, 0x00 }));
            MftReader.Fixup(tmp, 0, R);
            var r2 = MftReader.MftRuns(tmp, R, cl, read);
            Check("MFT simple: un solo tramo", r2 != null && r2.Count == 1 && r2[0][0] == 10);

            // Si la lista apunta a un registro que no se puede leer, falla con un mensaje claro (y PulsoPC usa el recorrido)
            var recsBad = new byte[32 * R];
            Put(recsBad, 0, 1, 0, Attr(0x20, false, ListEntry(0x80, 0, 0).Concat(ListEntry(0x80, 8, 30)).ToArray()), DataSeg(0, 7, new byte[] { 0x11, 0x08, 0x0A, 0x00 }));
            var vol2 = new byte[200 * cl]; recsBad.CopyTo(vol2, 10 * cl);
            Func<long, int, byte[]> read2 = (pos, n) => { var r = new byte[n]; Buffer.BlockCopy(vol2, (int)pos, r, 0, n); return r; };
            var rb = read2(10 * cl, cl); MftReader.Fixup(rb, 0, R);
            string err = null; try { MftReader.MftRuns(rb, R, cl, read2); } catch (Exception ex) { err = ex.Message; }
            Check("MFT partida: si falta un tramo avisa en vez de leer mal", err != null, err ?? "no falló");
        }
    }
}
