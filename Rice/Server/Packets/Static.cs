using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rice.Server.Packets
{
    // TODO: Keep this as empty as possible
    public static class Static
    {
        public static byte[] GameSettings = new byte[] { 
            0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x06, 0x00, 0x2F, 0x37, 0x05, 0x00, 0x00, 0x00, 0x0F, 0x00, 0xE4, 0xAF, 0x77, 0x00, 0xE8, 0x0F,
            0x8B, 0x14, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xF3, 0x60, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x28,
            0x20, 0x1C, 0x08, 0x87, 0x01, 0x01, 0x58, 0x02, 0x00, 0x00, 0xFD, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0xFF, 0x7B, 0x27, 0xE3, 0xCD, 0xF0, 0x8C, 0x02, 0x0A, 0x5A, 0xAA, 0xD5, 0x29, 0x00,
            0x00, 0x00, 0x0F, 0x00, 0x00, 0x00, 0x05, 0xE0, 0x51, 0x00, 0x41, 0x60, 0xC8, 0x00, 0x00, 0x08,
            0x28, 0x02, 0x88, 0x12, 0x83, 0x00, 0x64, 0x80, 0x5A, 0xCB, 0x01, 0x07, 0x30, 0x08, 0x00, 0x00,
            0x00, 0x00 };

        // Copia das configuracoes com os ajustes do server.json.
        // BattleZone.FrontAi (cliente 2016): flags a partir do byte 8; dword em +0x34, bits 6..9.
        public static byte[] BuildGameSettings()
        {
            var b = (byte[]) GameSettings.Clone();
            int at = 8 + 0x34;
            uint dw = BitConverter.ToUInt32(b, at);
            uint front = (uint) Math.Max(0, Math.Min(15, Rice.Server.Core.RiceServer.Config.BattleFrontAi));
            dw = (dw & ~(0xFu << 6)) | (front << 6);
            BitConverter.GetBytes(dw).CopyTo(b, at);
            // Main.Apprentice (dword +0xc, bit 11): tutorial do jogo com o carro de treino
            int ap = 8 + 0xc;
            uint dw2 = BitConverter.ToUInt32(b, ap);
            if (Rice.Server.Core.RiceServer.Config.ApprenticeTutorial) dw2 |= 1u << 11; else dw2 &= ~(1u << 11);
            BitConverter.GetBytes(dw2).CopyTo(b, ap);
            // Main.OptionItem (dword +0x8, bit 12) e Main.DockingItem (bit 13): os quadrados de opcao das pecas no carro
            // (sem o flag o jogo mostra um X e nao deixa equipar Option Parts)
            int op = 8 + 0x8;
            uint dw3 = BitConverter.ToUInt32(b, op);
            if (Rice.Server.Core.RiceServer.Config.OptionParts) dw3 |= 1u << 12; else dw3 &= ~(1u << 12);
            if (Rice.Server.Core.RiceServer.Config.DockingParts) dw3 |= 1u << 13; else dw3 &= ~(1u << 13);
            BitConverter.GetBytes(dw3).CopyTo(b, op);
            // Evento especial (server.json "Evento": natal, valentine, halloween, auto) e depois os flags soltos ("Events")
            string nome = EventoAtivo(Rice.Server.Core.RiceServer.Config.Evento, DateTime.Now);
            Dictionary<string, int> preset;
            if (nome != "" && Presets.TryGetValue(nome, out preset))
                foreach (var kv in preset) SetFlag(b, kv.Key, kv.Value);
            var ev = Rice.Server.Core.RiceServer.Config.Events;
            if (ev != null)
                foreach (var kv in ev) SetFlag(b, kv.Key, kv.Value);
            return b;
        }

        static void SetFlag(byte[] b, string name, int value)
        {
            int[] f;
            if (!EventFlags.TryGetValue(name, out f)) { Log.WriteError("Evento desconhecido: " + name); return; }
            int at = 8 + f[0];
            if (at + 4 > b.Length) return;
            uint d = BitConverter.ToUInt32(b, at);
            uint mask = (uint) (((1L << f[2]) - 1) << f[1]);
            d = (d & ~mask) | (((uint) value << f[1]) & mask);
            BitConverter.GetBytes(d).CopyTo(b, at);
        }

        // os eventos visuais que existem: os outros ficam desligados para nao misturar (o jogo veio com Valentine ligado)
        static readonly string[] Visuais = { "Christmas", "ChristmasTree", "SnowEffectOn", "Valentine", "ValentainChocolate", "Halloween" };

        static Dictionary<string, int> Preset(params string[] on)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in Visuais) d[v] = 0;
            foreach (var v in on) d[v] = 1;
            return d;
        }

        // evento especial -> flags do cliente
        public static readonly Dictionary<string, Dictionary<string, int>> Presets = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
        {
            {"natal", Preset("Christmas", "ChristmasTree", "SnowEffectOn")},
            {"valentine", Preset("Valentine", "ValentainChocolate")},
            {"halloween", Preset("Halloween")},
            {"nenhum", Preset()},
        };

        // "auto" escolhe pelo mes (cada evento dura 1 mes): outubro Halloween, dezembro Natal, fevereiro Valentine
        public static string EventoAtivo(string cfg, DateTime hoje)
        {
            string e = (cfg ?? "").Trim().ToLowerInvariant();
            if (e == "pascoa" || e == "páscoa") { Log.WriteError("Evento: o cliente nao tem visual de Pascoa (so a entrega do coelho)."); return ""; }
            if (e != "auto") return e;
            int m = hoje.Month;
            if (m == 12) return "natal";
            if (m == 2) return "valentine";
            if (m == 10) return "halloween";
            return "nenhum";
        }

        // flags de evento do cliente 2016 (Etc.*): nome -> {dword, bit, largura} (tabela do openDC, flags_2016.tsv)
        public static readonly Dictionary<string, int[]> EventFlags = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            {"Christmas", new[] {0x54, 13, 1}},          // roupa de Papai Noel no piloto
            {"ChristmasTree", new[] {0x4c, 8, 1}},
            {"ChristmasChase", new[] {0x4c, 7, 1}},
            {"SnowEffectOn", new[] {0x54, 14, 1}},       // neve
            {"Halloween", new[] {0x4c, 9, 1}},
            {"Valentine", new[] {0x54, 17, 1}},
            {"ValentainChocolate", new[] {0x5c, 3, 1}},
            {"GoodHarvest", new[] {0x58, 26, 1}},
            {"WaterMelon", new[] {0x4c, 6, 1}},
            {"IndependenceDay", new[] {0x58, 29, 1}},
            {"Event3Year", new[] {0x54, 16, 1}},
            {"GWFestival", new[] {0x50, 14, 1}},
            {"LuckyBag", new[] {0x50, 0, 1}},
            {"Hongbao", new[] {0x50, 1, 1}},
            {"HongbaoRush", new[] {0x50, 2, 1}},
            {"FreeOBGacha", new[] {0x50, 13, 1}},
            {"PartyPatrol", new[] {0x4c, 10, 1}},
            {"2xPartyBonus", new[] {0x4c, 11, 1}},
            {"NewChar2Exp", new[] {0x4c, 3, 1}},
            {"Quest", new[] {0x54, 11, 1}},
            {"TeamPoint", new[] {0x58, 30, 1}},
            {"ModifyBetter", new[] {0x5c, 0, 3}},
        };
    }

}
