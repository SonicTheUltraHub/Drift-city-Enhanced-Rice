using Newtonsoft.Json;
using Rice.Server.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Rice.Server.Packets.Game
{
    // Drift Shop > Sticker > Register Sticker (a imagem que voce importou)
    //  93 StickerNameCheck (DriftCity.exe 0x863830, 0x8e bytes): u32, u32, wchar[32] nome, wchar[32], u32
    //  94 StickerNameCheckAck (0x53d450, 0x52 bytes): wchar[36] nome, u32 slot, u32 resultado (1 = ok) - com 1 o jogo
    //     manda a imagem (71 StickerUpload)
    //  72 StickerUploadAck (0x53d220, 0x4e bytes): "sticker registrado" e fecha a janela
    // Offline nao ha servidor de imagens: o nome e sempre aceito e a imagem fica so no seu jogo.
    public static class Sticker
    {
        static string Hex(byte[] b, int max = 96) { return b == null ? "" : BitConverter.ToString(b, 0, Math.Min(b.Length, max)); }
        static int logs;

        [RicePacket(93, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void NameCheck(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            string name = b.Length >= 0x48 ? Encoding.Unicode.GetString(b, 8, 0x40).Split('\0')[0] : "sticker";
            // 93: u32 categoria, u32, wchar[32] dono, wchar[32] nome do sticker, u32
            var pend = PendingOf(packet.Sender);
            pend.Cat = b.Length >= 4 ? BitConverter.ToInt32(b, 0) : 0;
            pend.Name = b.Length >= 0x88 ? Encoding.Unicode.GetString(b, 0x48, 0x40).Split('\0')[0] : name;
            pend.Images.Clear(); pend.Cur = null;
            if (logs++ < 6) Log.WriteLine("Sticker: nome \"{0}\" ({1}).", name, Hex(b));
            var ack = new RicePacket(94);
            var nb = new byte[0x48];
            var raw = Encoding.Unicode.GetBytes(name);
            Array.Copy(raw, nb, Math.Min(raw.Length, 0x46));
            ack.Writer.Write(nb);
            ack.Writer.Write(1u);       // slot da imagem
            ack.Writer.Write(1u);       // 1 = nome aceito
            packet.Sender.Send(ack);
        }

        // ---- coleta de dados (Atualizacao 203): os pacotes de sticker que o servidor ainda nao responde aparecem no log
        // com o conteudo, para montar a lista da comunidade, a imagem e o "colar no carro" (salvar) -------------------
        static int dumps;
        static void Dump(RicePacket packet, string name)
        {
            if (dumps++ > 60) return;
            var b = packet.Buffer ?? new byte[0];
            Log.WriteLine("Sticker: {0} ({1} bytes): {2}", name, b.Length, Hex(b, 128));
        }
        [RicePacket(73, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void List(RicePacket packet)
        {
            Dump(packet, "StickerList 73");
            var b = packet.Buffer ?? new byte[0];
            // 73: u32 tipo (2 = todos, 3 = so os do corredor), u32, wchar nome (@8), ..., u32 pagina (@0x4c), u32 por pagina (@0x58)
            int type = b.Length >= 4 ? BitConverter.ToInt32(b, 0) : 0;
            string who = b.Length >= 0x28 ? Encoding.Unicode.GetString(b, 8, 0x20).Split('\0')[0] : "";
            int page = b.Length >= 0x50 ? BitConverter.ToInt32(b, 0x4c) : 1;
            int per = b.Length >= 0x5c ? BitConverter.ToInt32(b, 0x58) : 6;
            if (page < 1) page = 1;
            if (per < 1 || per > 50) per = 6;
            List<StickerRec> all;
            lock (Sync) all = Recs().Where(r => type != 3 || string.IsNullOrEmpty(who) || string.Equals(r.Owner, who, StringComparison.OrdinalIgnoreCase))
                                    .OrderByDescending(r => r.Id).ToList();
            int pages = Math.Max(1, (all.Count + per - 1) / per);
            if (page > pages) page = pages;
            var shown = all.Skip((page - 1) * per).Take(per).ToList();
            // Cmd_StickerListAck (74, DriftCity.exe 0x53d270 / 0x6c9d60 / 0x6c68b0): 16 bytes de cabecalho (@4 e @8 vao para
            // a tela), u32 quantidade, 160 bytes por sticker:
            //   @0 id, @4 0, @8/@0xc chave da imagem (o jogo pede a imagem com ela: GetImage @0xc/@0x10), @0x10 wchar[32] nome,
            //   @0x50 categoria, @0x54, @0x58 downloads, @0x5c data, @0x60 wchar[32] autor
            var ack = new RicePacket(74);
            ack.Writer.Write(type);
            ack.Writer.Write(page);
            ack.Writer.Write(pages);
            ack.Writer.Write(all.Count);
            ack.Writer.Write(shown.Count);
            foreach (var r in shown)
            {
                ack.Writer.Write(r.Id);
                ack.Writer.Write(0);
                ack.Writer.Write(r.Id);
                ack.Writer.Write(0);
                ack.Writer.Write(W(r.Name, 0x40));
                ack.Writer.Write(r.Cat);
                ack.Writer.Write(0);
                ack.Writer.Write(r.Downloads);
                ack.Writer.Write((int) r.Date);
                ack.Writer.Write(W(r.Owner, 0x40));
            }
            packet.Sender.Send(ack);
            Log.WriteLine("Sticker: lista tipo {0} de \"{1}\" pagina {2}/{3}: {4} de {5} sticker(s).", type, who, page, pages, shown.Count, all.Count);
        }

        [RicePacket(64, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void GetImage(RicePacket packet)
        {
            Dump(packet, "GetImage 64");
            var b = packet.Buffer ?? new byte[0];
            if (b.Length < 0x14) return;
            // 64 (DriftCity.exe 0x8630a0): @0 tipo (0x3f6 = sticker), @4, @8, @0xc id, @0x10
            int type = BitConverter.ToInt32(b, 0), a4 = BitConverter.ToInt32(b, 4), a5 = BitConverter.ToInt32(b, 8);
            int a2 = BitConverter.ToInt32(b, 0xc), a3 = BitConverter.ToInt32(b, 0x10);
            StickerRec r;
            lock (Sync) r = Recs().FirstOrDefault(x => x.Id == a2) ?? Recs().FirstOrDefault(x => x.Id == a4) ?? Recs().FirstOrDefault(x => x.Id == a5);
            byte[] img = null;
            if (r != null)
            {
                int idx = 2;   // a maior (128 px): com a de 32 o sticker ficava borrado no carro
                for (int i = idx; i >= 0 && img == null; i--) img = ReadImage(r.Id, i);
                if (img == null) for (int i = 0; i < 3 && img == null; i++) img = ReadImage(r.Id, i);
                if (img != null) r.Downloads++;
            }
            // Cmd_GetImageAck (65, 0x53de70): @0 tipo, @4 estado (0 = nao tem), @8/@0xc id, @0x10/@0x14, @0x18,
            // @0x20 posicao, @0x24 tamanho do pedaco, @0x28 total, depois os bytes (a imagem chega em pedacos)
            if (img == null)
            {
                SendImageChunk(packet.Sender, type, 0, a2, a3, a4, a5, 0, 0, 0, null);
                if (logs++ < 20) Log.WriteLine("Sticker: imagem {0} nao encontrada.", a2);
                return;
            }
            const int chunk = 2048;
            for (int off = 0; off < img.Length; off += chunk)
            {
                int n = Math.Min(chunk, img.Length - off);
                var part = new byte[n];
                Array.Copy(img, off, part, 0, n);
                SendImageChunk(packet.Sender, type, 1, a2, a3, a4, a5, off, n, img.Length, part);
            }
        }

        static void SendImageChunk(RiceClient c, int type, int status, int a2, int a3, int a4, int a5, int off, int n, int total, byte[] data)
        {
            var ack = new RicePacket(65);
            ack.Writer.Write(type);
            ack.Writer.Write(status);
            ack.Writer.Write(a2);
            ack.Writer.Write(a3);
            ack.Writer.Write(a4);
            ack.Writer.Write(a5);
            ack.Writer.Write(a4);
            ack.Writer.Write(0);
            ack.Writer.Write(off);
            ack.Writer.Write(n);
            ack.Writer.Write(total);
            if (data != null) ack.Writer.Write(data);
            c.Send(ack);
        }
        [RicePacket(67, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void PushImage(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            if (b.Length < 12) return;
            // 67: u32 posicao, u32 tamanho, u32 total, bytes (a imagem DDS em pedacos de 2048; 3 tamanhos: 32, 64 e 128)
            int off = BitConverter.ToInt32(b, 0), n = BitConverter.ToInt32(b, 4), total = BitConverter.ToInt32(b, 8);
            var pend = PendingOf(packet.Sender);
            if (off == 0 || pend.Cur == null) pend.Cur = new MemoryStream();
            n = Math.Max(0, Math.Min(n, b.Length - 12));
            pend.Cur.Position = Math.Min(off, (int) pend.Cur.Length);
            pend.Cur.Write(b, 12, n);
            if (total > 0 && pend.Cur.Length >= total)
            {
                pend.Images.Add(pend.Cur.ToArray());
                pend.Cur = null;
            }
        }
        [RicePacket(75, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void SmallImage(RicePacket packet) { Dump(packet, "StickerSmallImage 75"); }
        [RicePacket(77, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void Search(RicePacket packet) { Dump(packet, "StickerSearch 77"); }
        [RicePacket(1350, RiceServer.ServerType.Game)]
        public static void MyList(RicePacket packet)
        {
            Dump(packet, "MyStickerList 1350");
            // Cmd_MyStickerListAck (1351, DriftCity.exe 0x532550 / 0x779ff0): u32 quantidade, u32 0x40000 (= lista nova),
            // 64 bytes por sticker. Vazia por enquanto; antes nao tinha resposta.
            var ack = new RicePacket(1351);
            ack.Writer.Write(0);
            ack.Writer.Write(0x40000);
            packet.Sender.Send(ack);
        }
        [RicePacket(1352, RiceServer.ServerType.Game)]
        public static void Attach(RicePacket packet) { Dump(packet, "AttachSticker 1352"); }
        [RicePacket(1354, RiceServer.ServerType.Game)]
        public static void Detach(RicePacket packet) { Dump(packet, "DetachSticker 1354"); }

        [RicePacket(71, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void Upload(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            if (logs++ < 12) Log.WriteLine("Sticker: imagem enviada ({0} bytes): {1}", b.Length, Hex(b, 160));
            var pend = PendingOf(packet.Sender);
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (pend.Cur != null && pend.Cur.Length > 0) { pend.Images.Add(pend.Cur.ToArray()); pend.Cur = null; }
            if (pend.Images.Count > 0)
            {
                lock (Sync)
                {
                    var list = Recs();
                    var r = new StickerRec
                    {
                        Id = list.Count == 0 ? 100001 : list.Max(x => x.Id) + 1,
                        OwnerCid = ch != null ? (long) ch.CID : 0,
                        Owner = ch != null ? ch.Name : "",
                        Name = string.IsNullOrEmpty(pend.Name) ? "sticker" : pend.Name,
                        Cat = b.Length >= 4 ? BitConverter.ToInt32(b, 0) : pend.Cat,
                        Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        Images = pend.Images.Count
                    };
                    try
                    {
                        Directory.CreateDirectory(ImgDir());
                        for (int i = 0; i < pend.Images.Count; i++) File.WriteAllBytes(ImgPath(r.Id, i), pend.Images[i]);
                        list.Add(r);
                        SaveRecs();
                        Log.WriteLine("Sticker: \"{0}\" de {1} registrado (id {2}, {3} imagem(ns)).", r.Name, r.Owner, r.Id, r.Images);
                    }
                    catch (Exception ex) { Log.WriteError("Sticker: " + ex.Message); }
                }
            }
            pend.Images.Clear();
            var ack = new RicePacket(72);
            ack.Writer.Write(new byte[0x4c]);
            packet.Sender.Send(ack);
        }

        // ---- stickers registrados (save/stickers.json + save/stickers/<id>_<n>.dds) ---------------------------------
        public class StickerRec
        {
            public int Id, Cat, Downloads, Images;
            public long OwnerCid, Date;
            public string Owner, Name;
        }
        class Pending { public int Cat; public string Name; public MemoryStream Cur; public List<byte[]> Images = new List<byte[]>(); }

        static readonly object Sync = new object();
        static List<StickerRec> recs;
        static readonly Dictionary<RiceClient, Pending> pending = new Dictionary<RiceClient, Pending>();

        static Pending PendingOf(RiceClient c)
        {
            lock (pending)
            {
                Pending p;
                if (!pending.TryGetValue(c, out p)) pending[c] = p = new Pending();
                return p;
            }
        }

        static string ImgDir() { return Paths.SaveFile("stickers"); }
        static string ImgPath(int id, int i) { return Path.Combine(ImgDir(), id + "_" + i + ".dds"); }
        static byte[] ReadImage(int id, int i)
        {
            try { var p = ImgPath(id, i); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
            catch (Exception) { return null; }
        }

        static List<StickerRec> Recs()
        {
            if (recs != null) return recs;
            try
            {
                var p = Paths.SaveFile("stickers.json");
                recs = File.Exists(p) ? JsonConvert.DeserializeObject<List<StickerRec>>(File.ReadAllText(p)) : null;
            }
            catch (Exception ex) { Log.WriteError("stickers.json: " + ex.Message); }
            if (recs == null) recs = new List<StickerRec>();
            return recs;
        }

        static void SaveRecs()
        {
            try { File.WriteAllText(Paths.SaveFile("stickers.json"), JsonConvert.SerializeObject(recs, Formatting.Indented)); }
            catch (Exception ex) { Log.WriteError("stickers.json: " + ex.Message); }
        }

        static byte[] W(string text, int size)
        {
            var o = new byte[size];
            var e = Encoding.Unicode.GetBytes(text ?? "");
            Array.Copy(e, o, Math.Min(e.Length, size - 2));
            return o;
        }
    }
}
