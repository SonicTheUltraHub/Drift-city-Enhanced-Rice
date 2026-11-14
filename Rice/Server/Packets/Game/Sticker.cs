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
            bool shareAll = RiceServer.Config.StickerShareAll;
            lock (Sync) all = Recs().Where(r => type != 3 || shareAll || string.IsNullOrEmpty(who) || string.Equals(r.Owner, who, StringComparison.OrdinalIgnoreCase))
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
            lock (Sync)
            {
                r = Recs().FirstOrDefault(x => x.Id == a2) ?? Recs().FirstOrDefault(x => x.Id == a4) ?? Recs().FirstOrDefault(x => x.Id == a5);
                if (r == null)
                {
                    // id de uma colagem (o mesmo sticker colado 2 vezes tem 2 ids): acha o sticker dela
                    AttachedOf(0);
                    var hit = attached.Values.SelectMany(l => l).FirstOrDefault(x => BitConverter.ToInt32(x, 4) == a2);
                    if (hit != null) { int cat = CatalogId(hit); r = Recs().FirstOrDefault(x => x.Id == cat); }
                }
                if (r == null)
                {
                    // stickers do carro de OUTRO jogador (804): o jogo pede o id com os 2 bits de baixo zerados
                    // (100029 -> 100028); os nossos ids sao 4n+1
                    int baseId = (a2 & ~3) | 1;
                    r = Recs().FirstOrDefault(x => x.Id == baseId);
                    if (r == null)
                    {
                        var hit2 = attached.Values.SelectMany(l => l).FirstOrDefault(x => BitConverter.ToInt32(x, 4) == baseId);
                        if (hit2 != null) { int cat2 = CatalogId(hit2); r = Recs().FirstOrDefault(x => x.Id == cat2); }
                    }
                }
            }
            byte[] img = null;
            if (r != null)
            {
                // @4 = tamanho pedido (0 = 32 px da lista, 1 = 64, 2 = 128). Mandar a de 128 quando o jogo pede a de 32 fechava
                // o jogo na aba Sticker (a imagem nao cabe no espaco da miniatura).
                // A crash da aba Sticker era a L1 com 10 camadas, nao o tamanho: vai sempre a maior (128 px), mais nitida no carro.
                int idx = 2;
                for (int i = idx; i >= 0 && img == null; i--) img = ReadImage(r, i);
                if (img == null) for (int i = 0; i < 3 && img == null; i++) img = ReadImage(r, i);
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
            if (logs++ < 30) Log.WriteLine("Sticker: imagem {0} ({1} bytes, md5 {2}).", r.Id, img.Length, Md5(img));
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
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            List<byte[]> list;
            lock (Sync) list = ch != null ? AttachedOf((long) ch.CID).ToList() : new List<byte[]>();
            var ack = new RicePacket(1351);
            ack.Writer.Write(list.Count);
            ack.Writer.Write(0x40000);
            foreach (var e in list) { NameInto(e, CatalogId(e)); ack.Writer.Write(e); }
            packet.Sender.Send(ack);
        }
        [RicePacket(1352, RiceServer.ServerType.Game)]
        public static void Attach(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            Log.WriteLine("Sticker: colar no carro 1352 ({0} bytes): {1}", b.Length, Hex(b, 182));
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || b.Length < 0x24) return;
            // 1352 (montado pelo jogo com o 79): @0, @4 24 bytes de posicao (lugar/giro/tamanho), @0x1c, @0x20, @0x24 wchar[32],
            // @0x64, @0x68, @0x6c wchar[32], @0xac, @0xb0. O id do sticker e o valor que bate com um registrado.
            int[] cand = { BitConverter.ToInt32(b, 0), BitConverter.ToInt32(b, 0x1c), BitConverter.ToInt32(b, 0x20) };
            int sid;
            lock (Sync) sid = cand.FirstOrDefault(v => Recs().Any(rr => rr.Id == v));
            if (sid == 0) sid = cand[0];
            int car = ch.Vehicle != null ? ch.Vehicle.CarID : 0;
            // entrada da "meus stickers" (64 bytes, DriftCity.exe 0x77c3e0 / 0x5577f0): @0 carro, @4 id, @0x28 posicao (24)
            var e = new byte[64];
            Array.Copy(BitConverter.GetBytes(car), 0, e, 0, 4);
            Array.Copy(b, 4, e, 0x28, 24);
            Array.Copy(BitConverter.GetBytes(sid), 0, e, 0x28, 4);
            // cada colagem tem o seu id (@4): o mesmo sticker pode ir nos dois lados do carro. A primeira usa o id do sticker.
            int inst = sid;
            lock (Sync)
                if (AttachedOf((long) ch.CID).Any(x => BitConverter.ToInt32(x, 0) == car && BitConverter.ToInt32(x, 4) == sid))
                    inst = NextId(Recs());
            Array.Copy(BitConverter.GetBytes(inst), 0, e, 4, 4);
            NameInto(e, sid);
            long price = Math.Max(0, RiceServer.Config.StickerPrice);
            bool paid = price == 0 || ch.SpendMito((ulong) price);
            if (paid)
            {
                lock (Sync)
                {
                    var list = AttachedOf((long) ch.CID);
                    list.Add(e);
                    SaveAttached();
                }
                StickerRec r;
                lock (Sync) { r = Recs().FirstOrDefault(x => x.Id == sid); if (r != null) { r.Downloads++; SaveRec(r); } }
                Log.WriteLine("Sticker: {0} colou o sticker {1} no carro {2} (-{3} mito){4}.", ch.Name, sid, car, price,
                              inst != sid ? " como colagem " + inst : "");
            }
            // Cmd_AttachStickerAck (1353, 0x532260, 0x5e bytes): u32, u16, entrada de 64 bytes, i64 mito (soma no seu mito), 16 bytes
            var ack = new RicePacket(1353);
            int onCar;
            lock (Sync) onCar = AttachedOf((long) ch.CID).Count(x => BitConverter.ToInt32(x, 0) == car);
            ack.Writer.Write(paid ? 1 : 0);
            ack.Writer.Write((ushort) onCar);   // quantos stickers ficam no carro
            ack.Writer.Write(e);
            ack.Writer.Write(paid ? -price : 0L);
            ack.Writer.Write(new byte[16]);
            packet.Sender.Send(ack);
        }
        [RicePacket(1354, RiceServer.ServerType.Game)]
        public static void Detach(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            Dump(packet, "DetachSticker 1354");
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || b.Length < 4) return;
            // 1354: u32 id do sticker (Delete Mode do inventario / tirar do carro)
            int sid = BitConverter.ToInt32(b, 0);
            int car = ch.Vehicle != null ? ch.Vehicle.CarID : 0;
            int left;
            lock (Sync)
            {
                var list = AttachedOf((long) ch.CID);
                int n = list.RemoveAll(x => BitConverter.ToInt32(x, 4) == sid && BitConverter.ToInt32(x, 0) == car);
                if (n == 0) list.RemoveAll(x => BitConverter.ToInt32(x, 4) == sid);
                left = list.Count(x => BitConverter.ToInt32(x, 0) == car);
                SaveAttached();
            }
            Log.WriteLine("Sticker: {0} tirou o sticker {1} do carro {2}.", ch.Name, sid, car);
            // Cmd_DetachStickerAck (1355, 0x532440, 0xc bytes): u32 id, u16 quantos ficaram no carro, u32
            var ack = new RicePacket(1355);
            ack.Writer.Write(sid);
            ack.Writer.Write((ushort) left);
            ack.Writer.Write(0);
            packet.Sender.Send(ack);
        }

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
                        Id = NextId(list),
                        OwnerCid = ch != null ? (long) ch.CID : 0,
                        Owner = ch != null ? ch.Name : "",
                        Name = string.IsNullOrEmpty(pend.Name) ? "sticker" : pend.Name,
                        Cat = b.Length >= 4 ? BitConverter.ToInt32(b, 0) : pend.Cat,
                        Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                    try
                    {
                        r.Dir = NewDir(r.Name);
                        Directory.CreateDirectory(r.Dir);
                        // as 3 imagens (32, 64, 128 px); faltando alguma, repete a maior que veio
                        for (int i = 0; i < 3; i++)
                        {
                            var img = pend.Images[Math.Min(i, pend.Images.Count - 1)];
                            File.WriteAllBytes(Path.Combine(r.Dir, SizeFiles[i]), img);
                            Log.WriteLine("Sticker: imagem {0} de {1} salva ({2} bytes, md5 {3}).", SizeFiles[i], r.Name, img.Length, Md5(img));
                        }
                        SaveRec(r);
                        list.Add(r);
                        scanSig = Signature();
                        Log.WriteLine("Sticker: \"{0}\" de {1} registrado (id {2}) em {3}.", r.Name, r.Owner, r.Id, r.Dir);
                    }
                    catch (Exception ex) { Log.WriteError("Sticker: " + ex.Message); }
                }
            }
            pend.Images.Clear();
            var ack = new RicePacket(72);
            ack.Writer.Write(new byte[0x4c]);
            packet.Sender.Send(ack);
        }

        // 76 StickerReportAttach (lobby): o "Purchase" de um sticker. A resposta 79 (Cmd_StickerReportAttachAck, 0x53d350,
        // 0xbe bytes) faz o jogo mandar o 1352 (colar no carro) para o servidor do jogo com os campos dela.
        [RicePacket(76, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void ReportAttach(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            Log.WriteLine("Sticker: comprar 76 ({0} bytes): {1}", b.Length, Hex(b, 200));
            var o = new byte[188];
            Array.Copy(b, o, Math.Min(b.Length, o.Length));
            var ack = new RicePacket(79);
            ack.Writer.Write(o);
            packet.Sender.Send(ack);
        }

        // 95 DeleteStickerCatalog: u32 tipo, u32, u32 id, u32 - apaga o sticker registrado (os ja colados nos carros ficam;
        // a pasta vai para stickers/_apagados)
        [RicePacket(95, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void DeleteCatalog(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            Dump(packet, "DeleteStickerCatalog 95");
            if (b.Length < 12) return;
            int id = BitConverter.ToInt32(b, 8);
            lock (Sync)
            {
                var list = Recs();
                var r = list.FirstOrDefault(x => x.Id == id);
                if (r == null) return;
                list.Remove(r);
                try
                {
                    string del = Path.Combine(Root(), DeletedDir), to = Path.Combine(del, Path.GetFileName(r.Dir));
                    Directory.CreateDirectory(del);
                    for (int n = 2; Directory.Exists(to); n++) to = Path.Combine(del, Path.GetFileName(r.Dir) + " (" + n + ")");
                    Directory.Move(r.Dir, to);
                }
                catch (Exception ex) { Log.WriteError("Sticker: " + ex.Message); }
                scanSig = Signature();
                Log.WriteLine("Sticker: \"{0}\" (id {1}) apagado da lista (pasta em {2}/{3}).", r.Name, r.Id, Root(), DeletedDir);
            }
        }

        // ---- stickers colados nos carros (save/stickers_carros.json: cid -> entradas de 64 bytes em base64) ----------
        // 804 StickerInfoRes (DriftCity.exe 0x53ab20, "Cmd_StickerInfoRes"): os stickers do carro de OUTRO jogador.
        //   u16 serial do carro, u16 idade, u32 id do carro, u16 quantos (ate 100), depois 24 bytes cada (como no openDC;
        //   o handler le com 2 bytes de cabecalho antes, por isso no codigo aparece serial @2 e quantos @0xa)
        //   (a mesma posicao do 1352/1351 @0x28: id do sticker, x, y, largura, altura, ..., lado). O jogo pede a imagem
        //   pelo id (GetImage 64) e cola no carro daquele serial.
        public static void SendCarStickers(RiceClient c, ushort serial, long cid, int carId, bool quiet = false)
        {
            if (c == null || !RiceServer.Config.StickerSystem) return;
            List<byte[]> list;
            lock (Sync) list = AttachedOf(cid).Where(x => BitConverter.ToInt32(x, 0) == carId).Take(100).ToList();
            if (list.Count == 0) return;
            var p = new RicePacket(804);
            p.Writer.Write(serial);
            p.Writer.Write((ushort) RiceServer.Config.BotCarAge);
            p.Writer.Write((uint) carId);
            p.Writer.Write((ushort) list.Count);
            foreach (var e in list) p.Writer.Write(e, 0x28, 24);
            c.Send(p);
            if (!quiet) Log.WriteLine("Sticker: {0} sticker(s) do carro {1} (corredor {2}) mandados para o serial {3}.", list.Count, carId, cid, serial);
        }

        // seu corredor como bot: os stickers do carro que ele esta usando
        public static void SendBotStickers(RiceClient c, Bots.BotProfile b)
        {
            if (c == null || b == null || !b.IsAlt) return;
            try
            {
                int carId;
                using (var rc = Rice.Database.GetContext())
                {
                    var dc = rc.Characters.Find(b.Cid);
                    if (dc == null) return;
                    carId = dc.CurrentCarID;
                }
                ushort serial = (ushort) BotSocial.SerialOf(b);
                SendCarStickers(c, serial, b.Cid, carId);
                // o jogo so cola se o carro do bot ja estiver carregado (DriftCity.exe 0x53ac5e); na hora do 801 o
                // modelo ainda esta carregando: manda de novo depois de uns segundos
                foreach (int ms in new[] { 2500, 7000 })
                    System.Threading.Tasks.Task.Delay(ms).ContinueWith(_ =>
                    {
                        try { SendCarStickers(c, serial, b.Cid, carId, true); }
                        catch (Exception) { }
                    });
            }
            catch (Exception ex) { Log.WriteError("Sticker: bot " + b.Name + ": " + ex.Message); }
        }

        static Dictionary<long, List<byte[]>> attached;
        static List<byte[]> AttachedOf(long cid)
        {
            if (attached == null)
            {
                attached = new Dictionary<long, List<byte[]>>();
                try
                {
                    var p = Paths.SaveFile("stickers_carros.json");
                    if (File.Exists(p))
                        foreach (var kv in JsonConvert.DeserializeObject<Dictionary<long, List<string>>>(File.ReadAllText(p)))
                            attached[kv.Key] = kv.Value.Select(Convert.FromBase64String).ToList();
                }
                catch (Exception ex) { Log.WriteError("stickers_carros.json: " + ex.Message); }
            }
            List<byte[]> l;
            if (!attached.TryGetValue(cid, out l)) attached[cid] = l = new List<byte[]>();
            return l;
        }
        static void SaveAttached()
        {
            try
            {
                File.WriteAllText(Paths.SaveFile("stickers_carros.json"),
                    JsonConvert.SerializeObject(attached.ToDictionary(k => k.Key, k => k.Value.Select(Convert.ToBase64String).ToList()), Formatting.Indented));
            }
            catch (Exception ex) { Log.WriteError("stickers_carros.json: " + ex.Message); }
        }

        // ---- stickers registrados: pasta stickers/ (ao lado do Rice.exe), uma pasta por sticker ---------------------
        //   stickers/<nome>/sticker.json   id, nome, autor, categoria, data, downloads
        //   stickers/<nome>/32.dds 64.dds 128.dds   as 3 imagens (DXT5 com mipmaps, como o jogo manda)
        // Para passar stickers para outra pessoa: copie a pasta. Uma imagem solta (.png .jpg .bmp .gif) na pasta stickers
        // vira sticker sozinha (nome = nome do arquivo). Apagar no jogo move a pasta para stickers/_apagados.
        // A pasta e lida de novo quando muda (nao precisa reiniciar o servidor). Todos os corredores veem todos.
        // le a pasta stickers ao ligar o servidor (imagens novas viram sticker antes de alguem entrar no jogo)
        public static void Start()
        {
            new System.Threading.Thread(() =>
            {
                try
                {
                    int n;
                    lock (Sync) n = Recs().Count;
                    Log.WriteLine("Sticker: {0} sticker(s) na pasta {1}.", n, Root());
                }
                catch (Exception ex) { Log.WriteError("Sticker: " + ex.Message); }
            }) { IsBackground = true, Name = "Stickers" }.Start();
        }

        // id do sticker (da lista) de uma entrada colada no carro: a posicao (@0x28) comeca com ele.
        // @4 e o id desta colagem (um por colagem, para poder ter o mesmo sticker mais de uma vez no carro).
        static int CatalogId(byte[] e) { return BitConverter.ToInt32(e, 0x28); }

        public class StickerRec
        {
            public int Id, Cat, Downloads;
            public long OwnerCid, Date;
            public string Owner, Name;
            [JsonIgnore] public string Dir;
        }
        class Pending { public int Cat; public string Name; public MemoryStream Cur; public List<byte[]> Images = new List<byte[]>(); }

        static readonly object Sync = new object();
        static List<StickerRec> recs;
        static string scanSig;
        static readonly Dictionary<RiceClient, Pending> pending = new Dictionary<RiceClient, Pending>();
        static readonly string[] SizeFiles = { "32.dds", "64.dds", "128.dds" };
        static readonly int[] SizePx = { 32, 64, 128 };
        static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        const string InfoFile = "sticker.json", DeletedDir = "_apagados";

        static Pending PendingOf(RiceClient c)
        {
            lock (pending)
            {
                Pending p;
                if (!pending.TryGetValue(c, out p)) pending[c] = p = new Pending();
                return p;
            }
        }

        static string Root()
        {
            var f = RiceServer.Config != null ? RiceServer.Config.StickerFolder : null;
            return string.IsNullOrWhiteSpace(f) ? "stickers" : f;
        }

        static byte[] ReadImage(StickerRec r, int i)
        {
            try { var p = Path.Combine(r.Dir, SizeFiles[i]); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
            catch (Exception) { return null; }
        }

        // o que tem na pasta (muda quando alguem cola/apaga uma pasta ou imagem)
        static string Signature()
        {
            try
            {
                if (!Directory.Exists(Root())) return "";
                return string.Join("|", Directory.GetFileSystemEntries(Root()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception) { return ""; }
        }

        static List<StickerRec> Recs()
        {
            string sig = Signature();
            if (recs != null && sig == scanSig) return recs;
            Scan();
            scanSig = Signature();
            return recs;
        }

        static void Scan()
        {
            var old = recs ?? new List<StickerRec>();
            var list = new List<StickerRec>();
            try
            {
                Directory.CreateDirectory(Root());
                MigrateOld(list);
                WriteHelp();
                // imagens soltas viram sticker
                foreach (var f in Directory.GetFiles(Root()))
                {
                    if (!ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        string nd = NewDir(name);
                        Directory.CreateDirectory(nd);
                        File.Move(f, Path.Combine(nd, "original" + Path.GetExtension(f).ToLowerInvariant()));
                        Log.WriteLine("Sticker: imagem \"{0}\" virou sticker.", Path.GetFileName(f));
                    }
                    catch (Exception ex) { Log.WriteError("Sticker: " + Path.GetFileName(f) + ": " + ex.Message); }
                }
                // pastas: as que o servidor ja conhecia primeiro (mantem o id), depois as mais antigas
                var dirs = Directory.GetDirectories(Root()).Where(d => !Path.GetFileName(d).StartsWith("_"))
                    .OrderBy(d => old.Any(o => SamePath(o.Dir, d)) ? 0 : 1).ThenBy(d => Directory.GetCreationTimeUtc(d)).ToList();
                var used = new HashSet<int>(list.Select(x => x.Id));
                var fresh = new List<StickerRec>();
                foreach (var dir in dirs)
                {
                    if (list.Any(x => SamePath(x.Dir, dir))) continue;
                    var r = LoadDir(dir);
                    if (r == null) continue;
                    if (r.Id <= 0 || (r.Id & 3) != 1 || used.Contains(r.Id)) { r.Id = 0; fresh.Add(r); }
                    else used.Add(r.Id);
                    list.Add(r);
                }
                foreach (var r in fresh)
                {
                    r.Id = NextId(list);
                    SaveRec(r);
                    Log.WriteLine("Sticker: \"{0}\" entrou na lista (id {1}).", r.Name, r.Id);
                }
            }
            catch (Exception ex) { Log.WriteError("Sticker: pasta " + Root() + ": " + ex.Message); }
            recs = list;
        }

        static bool SamePath(string a, string b)
        {
            return a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        // le uma pasta de sticker; sem as .dds mas com uma imagem, gera as 3
        static StickerRec LoadDir(string d)
        {
            StickerRec r = null;
            var info = Path.Combine(d, InfoFile);
            try { if (File.Exists(info)) r = JsonConvert.DeserializeObject<StickerRec>(File.ReadAllText(info)); }
            catch (Exception ex) { Log.WriteError("Sticker: " + info + ": " + ex.Message); }
            bool write = r == null;
            if (r == null) r = new StickerRec { Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            r.Dir = d;
            if (string.IsNullOrWhiteSpace(r.Name)) { r.Name = Path.GetFileName(d); write = true; }
            if (r.Name.Length > 31) r.Name = r.Name.Substring(0, 31);
            if (r.Owner == null) { r.Owner = ""; write = true; }
            if (!SizeFiles.All(s => File.Exists(Path.Combine(d, s))))
            {
                var img = Directory.GetFiles(d).FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()));
                if (img == null)
                {
                    // so algumas .dds: usa a maior para as que faltam
                    var have = SizeFiles.Select(s => Path.Combine(d, s)).Where(File.Exists).ToList();
                    if (have.Count == 0) return null;
                    foreach (var sf in SizeFiles) if (!File.Exists(Path.Combine(d, sf))) File.Copy(have.Last(), Path.Combine(d, sf));
                }
                else
                {
                    try
                    {
                        using (var bmp = new System.Drawing.Bitmap(img))
                            for (int i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(d, SizeFiles[i]), StickerDds.Encode(bmp, SizePx[i]));
                        Log.WriteLine("Sticker: \"{0}\": imagens 32/64/128 feitas de {1}.", r.Name, Path.GetFileName(img));
                    }
                    catch (Exception ex) { Log.WriteError("Sticker: " + img + ": " + ex.Message); return null; }
                }
            }
            if (write && r.Id > 0) SaveRec(r);
            return r;
        }

        static string NewDir(string name)
        {
            string b = Paths.Safe(string.IsNullOrWhiteSpace(name) ? "sticker" : name), d = Path.Combine(Root(), b);
            for (int n = 2; Directory.Exists(d) || File.Exists(d); n++) d = Path.Combine(Root(), b + " (" + n + ")");
            return d;
        }

        static void WriteHelp()
        {
            var p = Path.Combine(Root(), "_COMO USAR.txt");
            if (File.Exists(p)) return;
            File.WriteAllText(p,
                "Stickers do Enhanced Rice\r\n\r\n" +
                "Cada pasta aqui e um sticker (sticker.json + 32.dds, 64.dds, 128.dds). Todos os corredores de todas as contas\r\n" +
                "veem todos os stickers na Drift Shop > Sticker.\r\n\r\n" +
                "- Para passar um sticker para alguem: mande a pasta dele. Quem receber cola a pasta aqui e ele aparece no jogo.\r\n" +
                "- Para criar um sticker sem o jogo: cole uma imagem (.png, .jpg, .bmp ou .gif) aqui; o nome do arquivo vira o\r\n" +
                "  nome do sticker (ate 31 letras). Imagem quadrada fica melhor (as outras sao esticadas).\r\n" +
                "- Apagar no jogo move a pasta para _apagados (para recuperar, mova de volta para ca).\r\n" +
                "- Nao precisa reiniciar o servidor: a lista e lida de novo quando a pasta muda.\r\n");
        }

        // stickers antigos (save/stickers.json + save/stickers/<id>_<n>.dds) vao para a pasta nova com o mesmo id
        static void MigrateOld(List<StickerRec> list)
        {
            var p = Paths.SaveFile("stickers.json");
            if (!File.Exists(p)) return;
            try
            {
                var olds = JsonConvert.DeserializeObject<List<StickerRec>>(File.ReadAllText(p)) ?? new List<StickerRec>();
                foreach (var r in olds)
                {
                    var src = Enumerable.Range(0, 3).Select(i => Path.Combine(Paths.SaveFile("stickers"), r.Id + "_" + i + ".dds")).ToList();
                    if (!src.Any(File.Exists)) continue;
                    r.Dir = NewDir(r.Name);
                    Directory.CreateDirectory(r.Dir);
                    for (int i = 0; i < 3; i++) if (File.Exists(src[i])) File.Move(src[i], Path.Combine(r.Dir, SizeFiles[i]));
                    SaveRec(r);
                }
                File.Move(p, p + ".migrado");
                Log.WriteLine("Sticker: {0} sticker(s) antigos foram para a pasta {1}.", olds.Count, Root());
            }
            catch (Exception ex) { Log.WriteError("Sticker: stickers antigos: " + ex.Message); }
        }

        // O jogo guarda a imagem pelo id com os 2 bits de baixo trocados pelo tamanho (DriftCity.exe 0x6e84a3: id & ~3 | 1):
        // os ids sao 4n+1 (100001, 100005...). Nao repete id que ainda esta colado em algum carro.
        static int NextId(List<StickerRec> list)
        {
            int max = 99997;
            foreach (var r in list) if (r.Id > max) max = r.Id;
            AttachedOf(0);
            foreach (var l in attached.Values) foreach (var e in l) max = Math.Max(max, BitConverter.ToInt32(e, 4));
            if (attached.ContainsKey(0) && attached[0].Count == 0) attached.Remove(0);
            return (max & ~3) + 5;
        }

        static void SaveRec(StickerRec r)
        {
            try
            {
                Directory.CreateDirectory(r.Dir);
                File.WriteAllText(Path.Combine(r.Dir, InfoFile), JsonConvert.SerializeObject(r, Formatting.Indented));
            }
            catch (Exception ex) { Log.WriteError("Sticker: " + r.Dir + ": " + ex.Message); }
        }

        // nome do sticker na entrada de 64 bytes (@8, 32 bytes): o jogo le como texto de 1 byte por letra (com wchar so
        // aparecia a 1a letra: "b", "g")
        static void NameInto(byte[] e, int sid)
        {
            StickerRec r;
            lock (Sync) r = Recs().FirstOrDefault(x => x.Id == sid);
            if (r == null) return;
            var n = new byte[0x20];
            var t = Encoding.Default.GetBytes(r.Name ?? "");
            Array.Copy(t, n, Math.Min(t.Length, 0x1f));
            Array.Copy(n, 0, e, 8, 0x20);
        }

        static string Md5(byte[] d)
        {
            using (var m = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(m.ComputeHash(d), 0, 4).Replace("-", "");
        }

        static byte[] W(string text, int size)
        {
            var o = new byte[size];
            var e = Encoding.Unicode.GetBytes(text ?? "");
            Array.Copy(e, o, Math.Min(e.Length, size - 2));
            return o;
        }
    }

    // Imagem comum (.png .jpg ...) -> DDS DXT5 com mipmaps, igual ao que o jogo manda no registro do sticker
    // (cabecalho flags 0x21007, caps 0x401008; 32 px = 1520 bytes, 64 = 5616, 128 = 22000).
    public static class StickerDds
    {
        public static byte[] Encode(System.Drawing.Image src, int size)
        {
            int mips = 1;
            for (int s = size; s > 1; s /= 2) mips++;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(0x20534444);            // "DDS "
            w.Write(124); w.Write(0x21007); w.Write(size); w.Write(size); w.Write(0); w.Write(0); w.Write(mips);
            for (int k = 0; k < 11; k++) w.Write(0);
            w.Write(32); w.Write(4); w.Write(0x35545844);   // pixel format: FourCC DXT5
            for (int k = 0; k < 5; k++) w.Write(0);
            w.Write(0x401008); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int m = 0, s = size; m < mips; m++, s = Math.Max(1, s / 2))
                EncodeLevel(Pixels(src, s), s, w);
            w.Flush();
            return ms.ToArray();
        }

        // imagem esticada para s x s (ARGB)
        static int[] Pixels(System.Drawing.Image src, int s)
        {
            using (var bmp = new System.Drawing.Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                using (var at = new System.Drawing.Imaging.ImageAttributes())
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    at.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                    g.DrawImage(src, new System.Drawing.Rectangle(0, 0, s, s), 0, 0, src.Width, src.Height, System.Drawing.GraphicsUnit.Pixel, at);
                }
                var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, s, s), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var px = new int[s * s];
                for (int y = 0; y < s; y++)
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * s, s);
                bmp.UnlockBits(data);
                return px;
            }
        }

        static void EncodeLevel(int[] px, int s, BinaryWriter w)
        {
            int blocks = Math.Max(1, (s + 3) / 4);
            var a = new int[16]; var r = new int[16]; var g = new int[16]; var b = new int[16];
            for (int by = 0; by < blocks; by++)
                for (int bx = 0; bx < blocks; bx++)
                {
                    for (int k = 0; k < 16; k++)
                    {
                        int x = Math.Min(s - 1, bx * 4 + (k & 3)), y = Math.Min(s - 1, by * 4 + (k >> 2));
                        int c = px[y * s + x];
                        a[k] = (c >> 24) & 255; r[k] = (c >> 16) & 255; g[k] = (c >> 8) & 255; b[k] = c & 255;
                    }
                    WriteAlpha(a, w);
                    WriteColor(r, g, b, w);
                }
        }

        static void WriteAlpha(int[] a, BinaryWriter w)
        {
            int lo = a.Min(), hi = a.Max();
            w.Write((byte) hi); w.Write((byte) lo);
            ulong bits = 0;
            if (hi > lo)
            {
                // a0 > a1: 8 valores (a0, a1 e 6 entre eles)
                var pal = new int[8];
                pal[0] = hi; pal[1] = lo;
                for (int k = 1; k <= 6; k++) pal[k + 1] = ((7 - k) * hi + k * lo) / 7;
                for (int k = 0; k < 16; k++)
                {
                    int best = 0, bd = int.MaxValue;
                    for (int p = 0; p < 8; p++) { int dd = Math.Abs(pal[p] - a[k]); if (dd < bd) { bd = dd; best = p; } }
                    bits |= (ulong) best << (3 * k);
                }
            }
            for (int k = 0; k < 6; k++) w.Write((byte) (bits >> (8 * k)));
        }

        static void WriteColor(int[] r, int[] g, int[] b, BinaryWriter w)
        {
            int r0 = r.Max(), g0 = g.Max(), b0 = b.Max(), r1 = r.Min(), g1 = g.Min(), b1 = b.Min();
            // a diagonal do "caixote" de cores vai no sentido em que as cores realmente variam
            double mr = r.Average(), mg = g.Average(), mb = b.Average();
            double crg = 0, cgb = 0;
            for (int k = 0; k < 16; k++) { crg += (r[k] - mr) * (g[k] - mg); cgb += (g[k] - mg) * (b[k] - mb); }
            if (crg < 0) { int t = g0; g0 = g1; g1 = t; }
            if ((crg < 0 ? -cgb : cgb) < 0) { int t = b0; b0 = b1; b1 = t; }
            int c0 = To565(r0, g0, b0), c1 = To565(r1, g1, b1);
            if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
            uint bits = 0;
            if (c0 != c1)
            {
                var pal = new int[4, 3];
                From565(c0, out pal[0, 0], out pal[0, 1], out pal[0, 2]);
                From565(c1, out pal[1, 0], out pal[1, 1], out pal[1, 2]);
                for (int ch = 0; ch < 3; ch++)
                {
                    pal[2, ch] = (2 * pal[0, ch] + pal[1, ch]) / 3;
                    pal[3, ch] = (pal[0, ch] + 2 * pal[1, ch]) / 3;
                }
                for (int k = 0; k < 16; k++)
                {
                    int best = 0, bd = int.MaxValue;
                    for (int p = 0; p < 4; p++)
                    {
                        int dr = pal[p, 0] - r[k], dg = pal[p, 1] - g[k], db = pal[p, 2] - b[k];
                        int dd = dr * dr + dg * dg + db * db;
                        if (dd < bd) { bd = dd; best = p; }
                    }
                    bits |= (uint) best << (2 * k);
                }
            }
            w.Write((ushort) c0); w.Write((ushort) c1); w.Write(bits);
        }

        static int To565(int r, int g, int b) { return ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255); }
        static void From565(int c, out int r, out int g, out int b)
        {
            r = ((c >> 11) & 31) * 255 / 31; g = ((c >> 5) & 63) * 255 / 63; b = (c & 31) * 255 / 31;
        }
    }
}
