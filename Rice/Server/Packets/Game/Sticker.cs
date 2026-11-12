using Rice.Server.Core;
using System;
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
            // Cmd_StickerListAck (74, DriftCity.exe 0x53d270 / 0x6c9d60): 16 bytes de cabecalho, u32 quantidade, 160 bytes
            // por sticker. Por enquanto a lista vai vazia (o formato das entradas ainda nao foi decifrado); antes o jogo
            // ficava sem resposta nenhuma.
            var ack = new RicePacket(74);
            ack.Writer.Write(new byte[16]);
            ack.Writer.Write(0);
            packet.Sender.Send(ack);
        }
        [RicePacket(64, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void GetImage(RicePacket packet) { Dump(packet, "GetImage 64"); }
        [RicePacket(67, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game)]
        public static void PushImage(RicePacket packet) { Dump(packet, "PushImageData 67"); }
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
            if (logs++ < 12) Log.WriteLine("Sticker: imagem enviada ({0} bytes): {1}", b.Length, Hex(b, 48));
            var ack = new RicePacket(72);
            ack.Writer.Write(new byte[0x4c]);
            packet.Sender.Send(ack);
        }
    }
}
