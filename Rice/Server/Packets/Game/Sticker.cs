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
