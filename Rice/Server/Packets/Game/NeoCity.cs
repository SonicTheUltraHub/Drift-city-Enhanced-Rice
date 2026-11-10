using System;
using System.Collections.Generic;
using System.Linq;
using Rice.Server.Core;
using Rice.Server.Structures;

namespace Rice.Server.Packets.Game
{
    // Neo City (Neo Oros, capitulos e operacoes). Primeira etapa: entrar na cidade.
    // Pacotes do DriftCity.exe 2016: 3720 NeoCitySuggest (o jogo manda ao apertar "Enter City": u32 serial, u32 tempo,
    // u32 capitulo/missao, u32 ?, u32 0, u32 ?), 3721 NeoCityLoad (o servidor manda: carrega a Neo City; o jogo le os
    // campos @4, @8, @12), 3723 Reject, 3724 Agree (party). Sozinho nao ha party para aceitar: o servidor responde o
    // 3721 na hora com os mesmos campos do pedido. Os proximos passos (3800+ capitulo/quest, HUVs da Neo City,
    // HP) dependem do que o jogo mandar depois - tudo fica no console ("Neo City:").
    public static class NeoCity
    {
        // areas das Neo Cities (o jogo carrega a area 84 + (cidade - 6): 84 = Neo Oros ... 87)
        public static readonly Dictionary<ulong, int> QuestOverride = new Dictionary<ulong, int>();
        public static readonly Dictionary<ulong, int> ChapterOverride = new Dictionary<ulong, int>();
        public static bool IsNeoArea(int area) { return area >= 84 && area <= 87; }

        static string Hex(byte[] b, int max = 80) { return BitConverter.ToString(b, 0, Math.Min(b.Length, max)); }

        [RicePacket(3720, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Suggest(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("Neo City: Enter City (3720, {0} bytes): {1}", buf.Length, Hex(buf));
            if (!RiceServer.Config.NeoCityEnter || buf.Length < 16) return;
            var pk = new RicePacket(3721);
            var body = new byte[28];
            Array.Copy(buf, body, Math.Min(28, buf.Length));
            // o jogo so carrega a cidade se o campo @4 for o numero da Neo City (6 = Neo Oros; < 10): no pedido ele vem
            // em @8 (em @4 vem o tempo). @8 = cidade e @12 = capitulo ele repassa no 3800.
            Array.Copy(buf, 8, body, 4, 4);
            pk.Writer.Write(body);
            packet.Sender.Send(pk);
            Log.WriteLine("Neo City: 3721 NeoCityLoad enviado ({0}).", Hex(body));
        }

        // 3800 NeoChapterStart (o jogo manda depois do 3721: u32 cidade (6 = Neo Oros), u32 capitulo/operacao da lista)
        // -> 3801 NeoChapterStartAck (16 bytes; o jogo le a cidade @0 e o capitulo @12)
        [RicePacket(3800, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChapterStart(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("Neo City: ChapterStart (3800): {0}", Hex(buf));
            if (buf.Length < 8) return;
            uint city = BitConverter.ToUInt32(buf, 0), chapter = BitConverter.ToUInt32(buf, 4);
            int ov;
            if (ChapterOverride.TryGetValue(packet.Sender.Player.ActiveCharacter.CID, out ov)) { Log.WriteLine("Neo City: teste !!neocap: capitulo {0} -> {1}.", chapter, ov); chapter = (uint) ov; }
            lock (cache) { curChapter[packet.Sender.Player.ActiveCharacter.CID] = chapter; runs[packet.Sender.Player.ActiveCharacter.CID] = new Run(); }
            lock (lives) lives.Clear();                     // capitulo novo: a party comeca com a vida cheia
            var pk = new RicePacket(3801);
            // DriftCity.exe (0x526ce0): @0 = o capitulo que o jogo procura na lista dele (0x7a9790: indice na tabela
            // NC_quest, na ordem Chapter 1-4 = 0-3, Operation 1-3 = 4-6, Natal = 7) e @12 = a linha de balanceamento por
            // numero de jogadores (0 = sozinho). Antes ia @0 = 6 (a cidade) e por isso abria sempre a Operation 3.
            pk.Writer.Write(chapter); pk.Writer.Write(city); pk.Writer.Write(0u); pk.Writer.Write(0u);
            packet.Sender.Send(pk);
            Log.WriteLine("Neo City: 3801 ChapterStartAck enviado (cidade {0}, capitulo {1}).", city, chapter);
        }

        // EXIT dentro da Neo City: 3824 (u32 cidade, u32 capitulo, u32 0, u32 1) -> 3825 NeoChapterGiveUpAck
        // (u32 0, u32 seu serial; com o seu serial o jogo sai da Neo City e volta)
        [RicePacket(3824, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChapterGiveUp(RicePacket packet)
        {
            var ch = packet.Sender.Player.ActiveCharacter;
            Log.WriteLine("Neo City: EXIT (3824): {0}", Hex(packet.Buffer));
            var pk = new RicePacket(3825);
            pk.Writer.Write(0u);
            pk.Writer.Write((uint) ch.Serial);
            packet.Sender.Send(pk);
        }

        // 3815 NeoQuestGoalPlace (u32 cidade, u32 capitulo, u32 quest, u32 estado): o jogo manda quando o carro entra (3/1)
        // ou sai (2/0) de um ponto da quest (o "!" e os pontos das missoes) e espera o 3818 com os mesmos campos - so com
        // ele abre a conversa do "!" (estado 3) ou comeca a missao do ponto (estado 1).
        [RicePacket(3815, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void GoalPlace(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (q3815++ < 40) Log.WriteLine("Neo City: ponto da quest (3815): {0}", Hex(buf));
            if (buf.Length < 16) return;
            lock (cache) curMission[packet.Sender.Player.ActiveCharacter.CID] = new[] { BitConverter.ToUInt32(buf, 0), BitConverter.ToUInt32(buf, 4), BitConverter.ToUInt32(buf, 8) };
            Echo(packet, 3818, 16);
        }
        // capitulo, quest e missao (objetivo) em que voce esta (do 3815/3819)
        static readonly Dictionary<ulong, uint[]> curMission = new Dictionary<ulong, uint[]>();
        static int q3815;

        // responde com os primeiros n bytes do pedido (os acks da Neo City conferem cidade/capitulo/quest com o que o jogo
        // tem e seguem sozinhos)
        static void Echo(RicePacket packet, ushort id, int n)
        {
            var body = new byte[n];
            Array.Copy(packet.Buffer, body, Math.Min(n, packet.Buffer.Length));
            var pk = new RicePacket(id);
            pk.Writer.Write(body);
            packet.Sender.Send(pk);
        }

        // recompensas das quests (NC_quest.xlt: quest -> exp, mito, itens)
        class Q { public int Exp, Mito; public string[] Items; public Q(int e, int m, string it) { Exp = e; Mito = m; Items = it.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); } }
        static readonly Dictionary<int, Q> Quests = new Dictionary<int, Q> {
            {1,new Q(100,100,"neo_wp_c_99999")}, {2,new Q(100,100,"neo_wp_a_99999")}, {3,new Q(100,100,"neo_wp_w_99999")}, {4,new Q(100,100,"neo_wp_m_99999")}, {5,new Q(100,100,"")}, {6,new Q(100,100,"")}, {7,new Q(100,100,"")}, {8,new Q(100,100,"")}, {9,new Q(100,100,"")}, {10,new Q(100,100,"")}, {11,new Q(100,100,"")}, {12,new Q(100,100,"")}, {13,new Q(100,100,"")}, {14,new Q(100,100,"")}, {15,new Q(100,100,"")}, {16,new Q(100,100,"")}, {17,new Q(100,100,"")}, {18,new Q(1000,10000,"")}, {19,new Q(1000,10000,"")}, {20,new Q(1000,10000,"")}, {21,new Q(1000,10000,"")}, {22,new Q(1000,10000,"")}, {62,new Q(0,0,"")}, {63,new Q(0,0,"")}, {64,new Q(0,0,"")}, {65,new Q(0,0,"")}, {66,new Q(0,0,"")}, {67,new Q(0,0,"")}, {68,new Q(10000,10000,"")}, {70,new Q(0,0,"")}, {71,new Q(0,0,"")}, {72,new Q(0,0,"")}, {73,new Q(10000,10000,"")}, {39,new Q(5000,5000,"")}, {45,new Q(70000,100000,"neo_wp_a_22101 neo_cop_10001 neo_arp_l_10001")}, {46,new Q(50000,100000,"neo_wp_c_21101 neo_fup_10001 neo_i_00005")}, {47,new Q(50000,100000,"neo_wp_w_23101 neo_i_00010 neo_i_00008")}, {48,new Q(50000,100000,"neo_wp_m_24101 neo_i_00004 neo_i_00005")}, {52,new Q(0,0,"")}, {53,new Q(0,0,"neo_wp_c_21101 neo_wp_a_22101 neo_wp_w_23101")}, {54,new Q(5000,5000,"")}, {55,new Q(30000,30000,"")}, {56,new Q(40000,40000,"")}, {61,new Q(50000,50000,"")}, {69,new Q(5000,10000,"")}, {74,new Q(0,0,"")}, {75,new Q(0,0,"")}, {76,new Q(0,0,"")}, {77,new Q(10000,10000,"")}
        };
        // recompensas pela tabela US (NC_quest.xlt: capitulo x 100 + quest dentro dele -> exp, mito, itens)
        static readonly Dictionary<int, Q> UsQuests = new Dictionary<int, Q> {
            {0, new Q(0, 0, "")}, {1, new Q(0, 0, "neo_wp_c_21101 neo_wp_a_22101 neo_wp_w_23101")}, {2, new Q(5000, 5000, "neo_arp_l_10001 neo_arp_h_10001")},
            {100, new Q(20000, 20000, "")},
            {200, new Q(0, 0, "")}, {201, new Q(0, 0, "")}, {202, new Q(10000, 10000, "")}, {203, new Q(0, 0, "")}, {204, new Q(15000, 15000, "")},
            {300, new Q(0, 0, "")}, {301, new Q(0, 0, "")}, {302, new Q(0, 0, "")}, {303, new Q(20000, 20000, "")},
            {400, new Q(30000, 30000, "")}, {500, new Q(40000, 40000, "")}, {600, new Q(50000, 50000, "")}, {700, new Q(5000, 10000, "")}
        };
        // quests ja pagas nesta sessao (para nao pagar duas vezes se o jogo repetir o pedido)
        static readonly HashSet<string> paid = new HashSet<string>();

        // 3804 NeoQuestStart (u32 cidade, u32 quest) -> 3805 NeoQuestStartAck (u32 cidade, u32 quest): aceitou a quest no "!"
        [RicePacket(3804, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestStart(RicePacket packet)
        {
            Log.WriteLine("Neo City: quest aceita (3804): {0}", Hex(packet.Buffer));
            int qo;
            if (packet.Buffer.Length >= 8 && QuestOverride.TryGetValue(packet.Sender.Player.ActiveCharacter.CID, out qo))
            {
                Log.WriteLine("Neo City: teste !!neoquest: quest {0} -> {1}.", BitConverter.ToUInt32(packet.Buffer, 4), qo);
                Array.Copy(BitConverter.GetBytes((uint) qo), 0, packet.Buffer, 4, 4);
            }
            lock (cache) spawns.Remove(packet.Sender.Player.ActiveCharacter.CID);
            Echo(packet, 3805, 8);
            // o jogo pede os HUVs (198) e torres (3905) logo depois; se nao pedir (ja tinha pedido nesta operacao antes),
            // o servidor manda de novo os que ele pediu da ultima vez, senao a missao fica sem inimigos
            var client = packet.Sender;
            ulong cid = client.Player.ActiveCharacter.CID;
            // a missao e identificada pelo que o jogo manda aqui (cidade, quest) e nao pelo item da lista (3800): a mesma
            // Operacao pode vir por itens diferentes, e o jogo so pede os inimigos (198) na primeira vez de cada sessao
            uint qkey = packet.Buffer.Length >= 8 ? 0x80000000u | (BitConverter.ToUInt32(packet.Buffer, 0) << 16) | (BitConverter.ToUInt32(packet.Buffer, 4) & 0xffff) : 0;
            lock (cache) { asked[cid] = false; if (qkey != 0) curChapter[cid] = qkey; }
            new System.Threading.Timer(st =>
            {
                try
                {
                    List<RicePacket> again = null;
                    lock (cache)
                    {
                        bool a; asked.TryGetValue(cid, out a);
                        uint ch; curChapter.TryGetValue(cid, out ch);
                        List<byte[]> l;
                        if (!a && cache.TryGetValue(cid + ":" + ch, out l))
                            again = l.Select(b => { var pk = new RicePacket(BitConverter.ToUInt16(b, 0)); pk.Writer.Write(b.Skip(2).ToArray()); return pk; }).ToList();
                    }
                    if (again == null) return;
                    lock (cache)
                    {
                        List<byte[]> l2;
                        uint ch2; curChapter.TryGetValue(cid, out ch2);
                        if (cache.TryGetValue(cid + ":" + ch2, out l2))
                            foreach (var b in l2) if (BitConverter.ToUInt16(b, 0) == 199) Track(client, b.Skip(2).ToArray());
                    }
                    foreach (var pk in again) client.Send(pk);
                    Log.WriteLine("Neo City: o jogo nao pediu os inimigos desta vez; reenviados {0} grupo(s) da ultima vez.", again.Count);
                }
                catch (Exception ex) { Log.WriteError("Neo City (reenvio dos inimigos): " + ex.Message); }
            }, null, 4000, System.Threading.Timeout.Infinite);
        }

        // inimigos (199/3906) que o jogo pediu por corredor e capitulo, para reenviar se ele nao pedir de novo
        static readonly Dictionary<string, List<byte[]>> cache = new Dictionary<string, List<byte[]>>();
        static readonly Dictionary<ulong, uint> curChapter = new Dictionary<ulong, uint>();
        static readonly Dictionary<ulong, bool> asked = new Dictionary<ulong, bool>();
        static void Remember(RicePacket packet, ushort id, byte[] body)
        {
            ulong cid = packet.Sender.Player.ActiveCharacter.CID;
            lock (cache)
            {
                uint ch; curChapter.TryGetValue(cid, out ch);
                string key = cid + ":" + ch;
                bool a; asked.TryGetValue(cid, out a);
                List<byte[]> l;
                // primeiro pedido depois de aceitar a quest: comeca a lista de novo
                if (!a || !cache.TryGetValue(key, out l)) { l = new List<byte[]>(); cache[key] = l; }
                asked[cid] = true;
                var b = new byte[body.Length + 2];
                b[0] = (byte) id; b[1] = (byte) (id >> 8);
                Array.Copy(body, 0, b, 2, body.Length);
                l.Add(b);
            }
        }

        // 3806 NeoQuestComplete (u32 cidade, u32 capitulo, u32 quest) -> 3807 NeoQuestCompleteAck (cidade, capitulo,
        // quest, u32 exp, ExpInfo, u16 nivel, u16 n itens, 3 x u32 item da tabela) - mesmo formato do 267 das missoes
        [RicePacket(3806, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestComplete(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("Neo City: quest completa (3806): {0}", Hex(buf));
            if (buf.Length < 12) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            // 3806 (DriftCity.exe 0x533cb0 le o 3807): @0 = capitulo, @4 = quest dentro do capitulo
            uint chapter = BitConverter.ToUInt32(buf, 0), qidx = BitConverter.ToUInt32(buf, 4), quest = qidx;
            Q q; UsQuests.TryGetValue((int) (chapter * 100 + qidx), out q);
            bool first = paid.Add(ch.CID + ":" + chapter + ":" + qidx);
            int exp = q != null && first ? q.Exp : 0, mito = q != null && first ? q.Mito : 0;
            var items = new List<uint>();
            if (first)
            {
                if (exp > 0) ch.GrantExperience(exp);
                if (mito > 0) ch.GrantMito((ulong) mito);
                if (q != null)
                    foreach (var id in q.Items)
                    {
                        if (items.Count >= 3) break;
                        int idx = Structures.Resources.ItemTable.Items.FindIndex(itm => itm.ID == id);
                        if (idx < 0) { Log.WriteLine("Neo City: item {0} nao existe na tabela.", id); continue; }
                        Rice.Game.Item got;
                        if (ch.GrantItem(id, 1, out got)) items.Add((uint) idx);
                    }
            }
            var pk = new RicePacket(3807);
            // DriftCity.exe 0x533cb0: @8 = exp e @12 = mito (o jogo SOMA o @12 no mito dele); antes ia trocado
            pk.Writer.Write(chapter); pk.Writer.Write(qidx); pk.Writer.Write(exp); pk.Writer.Write(mito);
            lock (cache) { var r = GetRun(ch.CID); r.Exp += exp; r.Mito += mito; }
            pk.Writer.Write(ch.GetExpInfo());
            pk.Writer.Write((ushort) ch.Level);
            pk.Writer.Write((ushort) items.Count);
            for (int i = 0; i < 3; i++) pk.Writer.Write(i < items.Count ? items[i] : 0u);
            packet.Sender.Send(pk);
            Log.WriteLine("Neo City: capitulo {0} quest {1} paga: {2} exp, {3} mito, {4} item(ns).", chapter, quest, exp, mito, items.Count);
            ch.FlushModInfo(packet.Sender);
            ch.FlushNotices(packet.Sender);
        }

        // 3810 NeoQuestFail (cidade, capitulo, quest, ?) -> 3811 (cidade, capitulo)
        [RicePacket(3810, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestFail(RicePacket packet)
        {
            Log.WriteLine("Neo City: quest falhou (3810): {0}", Hex(packet.Buffer));
            lock (cache) GetRun(packet.Sender.Player.ActiveCharacter.CID).Fails++;
            Echo(packet, 3811, 8);
        }

        // 3812 NeoQuestGiveUp (cidade, capitulo, quest, ?) -> 3813 (cidade, capitulo, u32 seu serial)
        [RicePacket(3812, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestGiveUp(RicePacket packet)
        {
            Log.WriteLine("Neo City: desistiu da quest (3812): {0}", Hex(packet.Buffer));
            var pk = new RicePacket(3813);
            var body = new byte[8];
            Array.Copy(packet.Buffer, body, Math.Min(8, packet.Buffer.Length));
            pk.Writer.Write(body);
            pk.Writer.Write((uint) packet.Sender.Player.ActiveCharacter.Serial);
            packet.Sender.Send(pk);
        }

        // 3819 NeoMissionCompleteParam (6 x u32) -> 3820 (os mesmos 24 bytes): progresso da missao (contagem de HUVs etc.)
        [RicePacket(3819, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void MissionParam(RicePacket packet)
        {
            if (q3819++ < 40) Log.WriteLine("Neo City: progresso da missao (3819): {0}", Hex(packet.Buffer));
            var buf = packet.Buffer;
            if (buf.Length >= 12)
                lock (cache) curMission[packet.Sender.Player.ActiveCharacter.CID] = new[] { BitConverter.ToUInt32(buf, 0), BitConverter.ToUInt32(buf, 4), BitConverter.ToUInt32(buf, 8) };
            Echo(packet, 3820, 24);
        }
        static int q3819;

        // 3821 NeoMissionComplete (cidade, capitulo, quest) -> 3822 (os mesmos 12 bytes): passa para a proxima missao
        [RicePacket(3821, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void MissionComplete(RicePacket packet)
        {
            Log.WriteLine("Neo City: missao completa (3821): {0}", Hex(packet.Buffer));
            Echo(packet, 3822, 12);
        }

        // 198 NeoHuvReq (u32 tempo, u32 n, n x 64 bytes: tipo, HP, Huv Param ID, posicao...): o jogo pede para criar
        // os HUVs inimigos da missao (ele mesmo escolhe da tabela NC_HuvRand). 199 NeoHuvRes cria eles: mesmo pedido
        // com 2 bytes a mais depois do n (u32 tempo, u32 n, u16 0, n x 64 bytes).
        [RicePacket(198, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void HuvReq(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            int n = (int) Math.Min(BitConverter.ToUInt32(buf, 4), (uint) ((buf.Length - 8) / 64));
            Log.WriteLine("Neo City: HUVs inimigos pedidos (198): {0} HUV(s), tipo/param do 1o: {1}/{2}.", n,
                n > 0 ? BitConverter.ToUInt16(buf, 8) : 0, n > 0 ? BitConverter.ToUInt32(buf, 8 + 0xe) : 0);
            var body = new byte[10 + n * 64];
            Array.Copy(buf, 0, body, 0, 8);
            Array.Copy(buf, 8, body, 10, n * 64);
            // o campo @10 de cada entrada (u16) tem que ser > 0 senao o jogo nao cria aquele HUV: os HUBs ja vem com 1,
            // mas os HUVs (e o chefe ★HUV-T01 da Operacao 3) vem com 0 - quem liberava era o servidor
            // Pelo DriftCity.exe (0x523450) cada HUV e identificado por (@10 numero do HUV, @6) e tem um dono em @8
            // (quem controla: anda, persegue e atira). O jogo pede com @10 = 0 e @8 = 0 nos HUVs e quem dava o numero
            // e o dono era o servidor; com @10 = 0 o jogo nem cria o HUV. Aqui: numero proprio para cada HUV e o dono e
            // voce (sozinho voce e o lider). Os HUBs ja vem numerados pelo jogo.
            int freed = 0;
            ushort me = packet.Sender.Player.ActiveCharacter.Serial;
            if (RiceServer.Config.NeoHuvSpawnAll)
                for (int i = 0; i < n; i++)
                {
                    int b = 10 + i * 64;
                    if (BitConverter.ToUInt16(body, b + 10) == 0)
                    {
                        ushort num; lock (cache) { if (++huvSerial < 0x200 || huvSerial > 0x7f00) huvSerial = 0x200; num = huvSerial; }
                        body[b + 10] = (byte) num; body[b + 11] = (byte) (num >> 8); freed++;
                    }
                    if (RiceServer.Config.NeoHuvOwnerIsPlayer && BitConverter.ToUInt16(body, b + 8) == 0) { body[b + 8] = (byte) me; body[b + 9] = (byte) (me >> 8); }
                }
            if (freed > 0) Log.WriteLine("Neo City: {0} HUV(s) liberados para aparecer (1o: {1}).", freed, Hex(body.Skip(10).Take(64).ToArray(), 64));
            if (n > 0 && n <= 2)
                for (int i = 0; i < n; i++)
                    Log.WriteLine("Neo City:   HUV {0} em ({1:0}, {2:0}).", BitConverter.ToUInt32(buf, 8 + 64 * i + 0xe),
                        BitConverter.ToSingle(buf, 8 + 64 * i + 0x20), BitConverter.ToSingle(buf, 8 + 64 * i + 0x24));
            var pk = new RicePacket(199);
            pk.Writer.Write(body);
            packet.Sender.Send(pk);
            Remember(packet, 199, body);
            Track(packet.Sender, body);
        }

        // 3905 NeoTurretReq (u32 n, n x [u16 dono, u16 id, 3 x float posicao]) -> 3906 NeoTurretRes (u32 n, u16 0,
        // u16 0, as mesmas entradas): cria as torres (dono 0 = torre inimiga)
        [RicePacket(3905, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void TurretReq(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int n = (int) Math.Min(BitConverter.ToUInt32(buf, 0), (uint) ((buf.Length - 4) / 16));
            if (q3905++ < 3) Log.WriteLine("Neo City: torres pedidas (3905): {0}", Hex(buf));
            var ents = new byte[n * 16];
            Array.Copy(buf, 4, ents, 0, ents.Length);
            // o dono da torre e quem "controla" ela (mira, atira e leva dano): com dono 0 ninguem controla e a torre fica
            // parada e sem levar dano. Sozinho, quem controla e voce (como o lider da party no jogo original).
            ushort me = packet.Sender.Player.ActiveCharacter.Serial;
            if (RiceServer.Config.NeoTurretOwnerIsPlayer)
                for (int i = 0; i < n; i++) if (BitConverter.ToUInt16(ents, i * 16) == 0) { ents[i * 16] = (byte) me; ents[i * 16 + 1] = (byte) (me >> 8); }
            var body = new byte[8 + ents.Length];
            Array.Copy(BitConverter.GetBytes((uint) n), body, 4);
            Array.Copy(ents, 0, body, 8, ents.Length);
            var pk = new RicePacket(3906);
            pk.Writer.Write(body);
            packet.Sender.Send(pk);
            Remember(packet, 3906, body);
        }
        static int q3905;
        static ushort huvSerial = 0x200;

        // combate da Neo City: o jogo manda e espera de volta o mesmo pacote (o servidor repassa para a party, incluindo
        // quem mandou; e com ele que o jogo aplica o golpe/vida). 205 NeoHuvHit (40 bytes: quem bateu, HUV, vida...),
        // 206 NeoHuvEnd (62), 207 NeoHubTargeting (9), 208 NeoAggro (14),
        // 3919 explosao -> 3920 (16)
        [RicePacket(205, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void HuvHit(RicePacket packet)
        {
            // 205 de volta (DriftCity.exe 0x525a10): @0 quem bateu, @4 dono, @6 numero do HUV, @10 vida que sobrou ... e os
            // campos que o SERVIDOR preenche: @24 exp ganha (u32), @28 exp total (i64) e @36 mito ganho (u32). Devolver o
            // pedido como veio punha ali as posicoes (floats) = exp e mito "infinitos"/aleatorios. Agora: so no golpe que
            // destroi (vida 0) e uma vez por HUV, exp e mito pelo nivel do HUV.
            var buf = packet.Buffer;
            int c; relayLog.TryGetValue(205, out c); relayLog[205] = c + 1;
            if (c < 15) Log.WriteLine("Neo City: golpe no HUV (205): {0}", Hex(buf, 64));
            if (buf.Length < 0x18) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            var body = new byte[40];
            Array.Copy(buf, body, 0x18);
            uint exp = 0, mito = 0;
            ushort hitter = BitConverter.ToUInt16(buf, 0), num = BitConverter.ToUInt16(buf, 6);
            int hp = buf.Length >= 0xe ? BitConverter.ToInt32(buf, 0xa) : 1;
            lock (cache)
            {
                Dictionary<ushort, int> hm;
                if (!hpOf.TryGetValue(ch.CID, out hm)) hpOf[ch.CID] = hm = new Dictionary<ushort, int>();
                hm[num] = hp;                                              // vida que o seu jogo diz que sobrou
                // dano medio dos seus tiros (sem o golpe final): base do dano calculado dos bots
                int pd = buf.Length >= 0x12 ? BitConverter.ToInt32(buf, 0xe) : 0;
                if (hitter == ch.Serial && hp > 0 && pd > 0 && pd < 100000)
                {
                    double av; myDmg.TryGetValue(ch.CID, out av);
                    myDmg[ch.CID] = av <= 0 ? pd : av * 0.85 + pd * 0.15;
                }
            }
            if (hp <= 0 && hitter == ch.Serial)
            {
                bool first; int level = 60; bool boss = false; int kind = 0;
                lock (cache)
                {
                    HashSet<ushort> k;
                    if (!killed.TryGetValue(ch.CID, out k)) killed[ch.CID] = k = new HashSet<ushort>();
                    first = k.Add(num);
                    List<Spawn> l;
                    if (spawns.TryGetValue(ch.CID, out l))
                    {
                        var sp = l.FirstOrDefault(x => BitConverter.ToUInt16(x.Ent, 10) == num);
                        if (sp != null) { level = Math.Max(1, (int) BitConverter.ToUInt16(sp.Ent, 4)); boss = sp.Boss; kind = BitConverter.ToUInt16(sp.Ent, 0); }
                    }
                }
                if (first)
                {
                    var cfg = RiceServer.Config;
                    double mult = boss ? cfg.NeoBossRewardMultiplier : 1.0;
                    exp = (uint) Math.Max(0, Math.Round(level * cfg.NeoHuvExpPerLevel * mult));
                    mito = (uint) Math.Max(0, Math.Round(level * cfg.NeoHuvMitoPerLevel * mult));
                    if (exp > 0) ch.GrantExperience(exp);
                    if (mito > 0) ch.GrantMito(mito);
                    lock (cache)
                    {
                        var r = GetRun(ch.CID); r.Exp += exp; r.Mito += mito;
                        if (kind == 1) r.Hub++; else if (kind == 0) r.Huv++; else r.Hus++;
                    }
                    try { Drop(packet.Sender, ch, boss); } catch (Exception ex) { Log.WriteError("Neo City (item do HUV): " + ex.Message); }
                    try { PartyReward(ch, level, mult, 0); } catch (Exception ex) { Log.WriteError("Neo City (party): " + ex.Message); }
                }
            }
            // DriftCity.exe 0x525a10: @24 = mito ganho ("Huv Mito %d"), @28 = MITO TOTAL (i64, o jogo troca o mito dele
            // por este valor) e @36 = exp ganha ("Huv Exp %d"). Antes ia a exp total no @28 e o mito da tela virava a exp.
            Array.Copy(BitConverter.GetBytes(mito), 0, body, 0x18, 4);
            Array.Copy(BitConverter.GetBytes((long) ch.Mito), 0, body, 0x1c, 8);
            Array.Copy(BitConverter.GetBytes(exp), 0, body, 0x24, 4);
            var pk = new RicePacket(205);
            pk.Writer.Write(body);
            packet.Sender.Send(pk);
            if (exp > 0 || mito > 0)
            {
                Log.WriteLine("Neo City: HUV {0:X} destruido por {1}: +{2} exp, +{3} mito.", num, ch.Name, exp, mito);
                try { ch.FlushNotices(packet.Sender); } catch (Exception) { }
            }
        }
        static readonly Dictionary<ulong, HashSet<ushort>> killed = new Dictionary<ulong, HashSet<ushort>>();
        [RicePacket(206, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void HuvEnd(RicePacket packet)
        {
            Relay(packet, 206, 62, "HUV destruido (206)");
            try { Destroyed(packet); } catch (Exception ex) { Log.WriteError("Neo City (HUV destruido): " + ex.Message); }
        }

        // ---- HUVs que voltam (regen) e itens que caem ----
        // Na tabela HuvTable.tdf cada grupo tem regen_num/regen_time: o chefe ★HUV-T01 da Operacao 3 (grupo 1108) volta
        // 10 s depois de destruido, ate 10 vezes (a missao pede 5); os HUVs normais voltam em 30 s, 2 vezes. Quem fazia
        // isso era o servidor: aqui ele manda de novo o 199 daquele HUV.
        class Spawn { public byte[] Ent; public uint Time; public int Regens; public bool Boss; public bool Pending; }
        static readonly Dictionary<ulong, List<Spawn>> spawns = new Dictionary<ulong, List<Spawn>>();
        static void Track(RiceClient c, byte[] body)
        {
            if (body.Length < 10) return;
            int n = (int) Math.Min(BitConverter.ToUInt32(body, 4), (uint) ((body.Length - 10) / 64));
            ulong cid = c.Player.ActiveCharacter.CID;
            lock (cache)
            {
                List<Spawn> l;
                if (!spawns.TryGetValue(cid, out l)) spawns[cid] = l = new List<Spawn>();
                for (int i = 0; i < n; i++)
                {
                    var e = new byte[64];
                    Array.Copy(body, 10 + i * 64, e, 0, 64);
                    ushort type = BitConverter.ToUInt16(e, 0);
                    // onde cada HUV/HUB aparece: alvo dos bots enquanto ele ainda nao andou (HUB nunca anda)
                    Dictionary<ushort, float[]> pts;
                    if (!points.TryGetValue(cid, out pts)) points[cid] = pts = new Dictionary<ushort, float[]>();
                    ushort un = BitConverter.ToUInt16(e, 10);
                    if (un != 0)
                    {
                        pts[un] = new[] { BitConverter.ToSingle(e, 0x20), BitConverter.ToSingle(e, 0x24), BitConverter.ToSingle(e, 0x28) };
                        Dictionary<ushort, DateTime> rl;
                        if (!released.TryGetValue(cid, out rl)) released[cid] = rl = new Dictionary<ushort, DateTime>();
                        rl[un] = DateTime.UtcNow;
                        HashSet<ushort> dd; if (dead.TryGetValue(cid, out dd)) dd.Remove(un);
                        // o HUB da missao renasce com o MESMO numero (1): vida nova, sem marca de destruido
                        HashSet<ushort> kk; if (killed.TryGetValue(cid, out kk)) kk.Remove(un);
                        Dictionary<ushort, int> hh; if (hpOf.TryGetValue(cid, out hh)) hh.Remove(un);
                        Dictionary<ushort, uint[]> gi;
                        if (!groups.TryGetValue(cid, out gi)) groups[cid] = gi = new Dictionary<ushort, uint[]>();
                        gi[un] = new[] { BitConverter.ToUInt32(e, 0x12), (uint) BitConverter.ToUInt16(e, 4) };   // grupo (@18) e nivel
                    }
                    if (type == 1) continue;                                   // HUB (parado): nao volta
                    if (l.Any(x => BitConverter.ToUInt16(x.Ent, 10) == BitConverter.ToUInt16(e, 10))) continue;
                    // chefe: @24 = 10 (os HUVs de patrulha vem com 2 ou 3; os de escolta com 0)
                    l.Add(new Spawn { Ent = e, Time = BitConverter.ToUInt32(body, 0), Boss = BitConverter.ToUInt32(e, 0x18) >= 10 });
                }
            }
        }

        static readonly Dictionary<ulong, Dictionary<ushort, float[]>> points = new Dictionary<ulong, Dictionary<ushort, float[]>>();
        static readonly Dictionary<ulong, Dictionary<ushort, DateTime>> released = new Dictionary<ulong, Dictionary<ushort, DateTime>>();
        // quando o servidor liberou o HUV para aparecer (ele so existe depois que o seu jogo comeca a mexer nele)
        public static DateTime ReleasedAt(Rice.Game.Character ch, ushort num)
        {
            lock (cache)
            {
                Dictionary<ushort, DateTime> rl; DateTime t;
                return ch != null && released.TryGetValue(ch.CID, out rl) && rl.TryGetValue(num, out t) ? t : DateTime.MinValue;
            }
        }
        // 0 = HUV (atira), 1 = HUB (atira, parado), 3 = HUS (bate)
        static int KindOf(Rice.Game.Character ch, ushort num)
        {
            lock (cache)
            {
                List<Spawn> l;
                if (spawns.TryGetValue(ch.CID, out l))
                {
                    var sp = l.FirstOrDefault(x => BitConverter.ToUInt16(x.Ent, 10) == num);
                    if (sp != null) return BitConverter.ToUInt16(sp.Ent, 0);
                }
            }
            return 1;
        }
        static readonly Dictionary<ulong, Dictionary<ushort, uint[]>> groups = new Dictionary<ulong, Dictionary<ushort, uint[]>>();
        // HUV destruido por um bot: o seu jogo nao manda o 3819 (so manda quando VOCE destroi), entao o servidor manda o
        // 3820 (DriftCity.exe 0x53cee0: capitulo, quest e missao iguais aos atuais; @12 grupo do HUV e @16 nivel, como no
        // 3819 que o jogo manda) e o objetivo da missao sobe.
        static void CountForMission(RiceClient c, Rice.Game.Character ch, ushort num)
        {
            uint[] m, g;
            lock (cache)
            {
                if (!curMission.TryGetValue(ch.CID, out m)) return;
                Dictionary<ushort, uint[]> gi;
                if (!groups.TryGetValue(ch.CID, out gi) || !gi.TryGetValue(num, out g)) return;
            }
            if (g[0] == 0) return;                                        // HUV de escolta (sem grupo): nao e objetivo
            var pk = new RicePacket(3820);
            pk.Writer.Write(m[0]); pk.Writer.Write(m[1]); pk.Writer.Write(m[2]);
            pk.Writer.Write(g[0]); pk.Writer.Write(g[1]);
            pk.Writer.Write(ch.Serial); pk.Writer.Write(num);
            c.Send(pk);
            Log.WriteLine("Neo City: HUV {0:X} do bot contado na missao (capitulo {1}, quest {2}, missao {3}, grupo {4}).", num, m[0], m[1], m[2], g[0]);
        }
        static readonly Dictionary<ulong, HashSet<ushort>> dead = new Dictionary<ulong, HashSet<ushort>>();
        // HUVs/HUBs vivos e onde apareceram (numero, x, y, z), para os bots atirarem tambem nos que ainda nao andaram
        public static List<float[]> TargetPoints(Rice.Game.Character ch)
        {
            var r = new List<float[]>();
            if (ch == null) return r;
            lock (cache)
            {
                Dictionary<ushort, float[]> pts; HashSet<ushort> dd, kk;
                if (!points.TryGetValue(ch.CID, out pts)) return r;
                dead.TryGetValue(ch.CID, out dd); killed.TryGetValue(ch.CID, out kk);
                foreach (var kv in pts)
                    if ((dd == null || !dd.Contains(kv.Key)) && (kk == null || !kk.Contains(kv.Key)))
                        r.Add(new[] { kv.Key, kv.Value[0], kv.Value[1], kv.Value[2] });
            }
            return r;
        }

        static void Destroyed(RicePacket packet)
        {
            var buf = packet.Buffer;
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            var vals = new HashSet<ushort>();
            for (int i = 0; i + 2 <= Math.Min(buf.Length, 16); i += 2) vals.Add(BitConverter.ToUInt16(buf, i));
            if (buf.Length >= 4)
                lock (cache)
                {
                    HashSet<ushort> dd;
                    if (!dead.TryGetValue(ch.CID, out dd)) dead[ch.CID] = dd = new HashSet<ushort>();
                    dd.Add(BitConverter.ToUInt16(buf, 2));                    // 206: @2 = numero do HUV destruido
                }
            Spawn s = null;
            lock (cache)
            {
                List<Spawn> l;
                if (spawns.TryGetValue(ch.CID, out l))
                    s = l.FirstOrDefault(x => !x.Pending && BitConverter.ToUInt16(x.Ent, 10) >= 0x200 && vals.Contains(BitConverter.ToUInt16(x.Ent, 10)))
                        ?? l.FirstOrDefault(x => !x.Pending && vals.Contains(BitConverter.ToUInt16(x.Ent, 0x16)))
                        ?? l.FirstOrDefault(x => !x.Pending && vals.Contains(BitConverter.ToUInt16(x.Ent, 6)));
                var cfg = RiceServer.Config;
                if (s != null && s.Regens < (s.Boss ? cfg.NeoBossRegenCount : cfg.NeoHuvRegenCount))
                {
                    s.Regens++; s.Pending = true;
                    var sp = s;
                    int secs = Math.Max(3, s.Boss ? cfg.NeoBossRegenSeconds : cfg.NeoHuvRegenSeconds);
                    Log.WriteLine("Neo City: HUV {0}{1} destruido; volta em {2} s ({3}a vez).", BitConverter.ToUInt32(s.Ent, 0xe),
                        s.Boss ? " (chefe)" : "", secs, s.Regens);
                    System.Threading.Tasks.Task.Delay(secs * 1000).ContinueWith(_ =>
                    {
                        try
                        {
                            lock (cache) sp.Pending = false;
                            var e = (byte[]) sp.Ent.Clone();
                            var pk = new RicePacket(199);
                            pk.Writer.Write(sp.Time); pk.Writer.Write(1u); pk.Writer.Write((ushort) 0); pk.Writer.Write(e);
                            c.Send(pk);
                        }
                        catch (Exception ex) { Log.WriteError("Neo City (regen): " + ex.Message); }
                    });
                }
                else if (s == null && (cfg.NeoBossRegenCount > 0 || cfg.NeoHuvRegenCount > 0)) Log.WriteLine("Neo City: HUV destruido nao reconhecido ({0}).", Hex(buf, 16));
            }
            // o item agora cai no golpe que destroi o HUV (205), uma vez so por HUV
            try { BotKill(c, ch, vals, buf.Length >= 4 ? BitConverter.ToUInt16(buf, 2) : (ushort) 0); } catch (Exception ex) { Log.WriteError("Neo City (HUV do bot): " + ex.Message); }
        }

        // ---- bots da party ajudando nas missoes -------------------------------------------------------------------
        // Bot da sua party a ate NeoBotHitRange m de um HUV (ele vai atras dos HUVs como na perseguicao da cidade) atira
        // nele a cada NeoBotHitSeconds s: 205 com o serial do bot (DriftCity.exe 0x525a10: @14 = dano, que o jogo soma no
        // HUV; @10 = vida, 0 forcaria a morte, por isso vai 1). Quando a vida acaba o seu jogo destroi o HUV (206):
        // se foi um bot, voce ganha NeoBotKillShare da exp/mito do HUV.
        static readonly System.Threading.Timer botTimer = new System.Threading.Timer(_ => { try { BotTick(); } catch (Exception ex) { Log.WriteError("Neo City (bots): " + ex.Message); } }, null, 3000, 400);
        static readonly Dictionary<long, DateTime> botNext = new Dictionary<long, DateTime>();
        static readonly Dictionary<ulong, Dictionary<ushort, Tuple<long, DateTime>>> botHits = new Dictionary<ulong, Dictionary<ushort, Tuple<long, DateTime>>>();
        static int botLogs;
        static void BotTick()
        {
            var cfg = RiceServer.Config;
            if (!cfg.NeoBotsHelp) return;
            foreach (var p in RiceServer.GetPlayers())
            {
                var ch = p != null ? p.ActiveCharacter : null;
                var gc = p != null ? p.GameClient : null;
                if (ch == null || gc == null || ch.Area == null || !IsNeoArea(ch.Area.ID)) continue;
                try { BotKits(gc, ch); } catch (Exception ex) { Log.WriteError("Neo City (kits dos bots): " + ex.Message); }
                try { HuvAttacks(gc, ch); } catch (Exception ex) { Log.WriteError("Neo City (HUV nos bots): " + ex.Message); }
                var near = CityBots.NeoTargetsOf(ch);
                if (near.Count == 0) continue;
                var ac = p.AreaClient;
                var now = DateTime.UtcNow;
                foreach (var n in near)
                {
                    if (n.Number == 0) continue;                         // 0 = carro de jogador
                    var b = Bots.ByCid(n.Cid);
                    if (b == null) continue;
                    double skill = Math.Max(0.5, Math.Min(2.0, b.Skill));
                    lock (cache)
                    {
                        HashSet<ushort> k;
                        if (killed.TryGetValue(ch.CID, out k) && k.Contains(n.Number)) continue;
                        DateTime nx;
                        if (botNext.TryGetValue(n.Cid, out nx) && now < nx) continue;
                        botNext[n.Cid] = now.AddSeconds(Math.Max(0.3, cfg.NeoBotHitSeconds) * (0.8 + Rng.NextDouble() * 0.4) / skill);
                        Dictionary<ushort, Tuple<long, DateTime>> bh;
                        if (!botHits.TryGetValue(ch.CID, out bh)) botHits[ch.CID] = bh = new Dictionary<ushort, Tuple<long, DateTime>>();
                        bh[n.Number] = Tuple.Create(n.Cid, now);
                    }
                    if (IsDown(n.Cid)) continue;                         // destruido: esperando voltar
                    bool armed = HasWeapon(b);
                    if (!armed && n.Dist > Math.Max(2.0, cfg.NeoBotRamDistance)) continue;   // sem arma: so batendo
                    // dano REAL da arma (tabela de armas do jogo, Init\WeaponList.xlt -> res/neo_weapons.json): o mesmo
                    // Weapon Attack que o painel NEO Performance mostra (ex.: U Cannon 108 = 11623-13018), cadencia (cool
                    // time), rajada, alcance, precisao e critico da arma. Sem arma: batida (Crashing Attack pelo nivel).
                    int dmg; double cool;
                    if (!RealShot(b, n.Dist, out dmg, out cool))
                    {
                        if (armed) continue;                             // fora do alcance das armas dele
                        dmg = (int) Math.Round(b.Level * (1.8 + Rng.NextDouble() * 2.7));
                        cool = 1.0;
                    }
                    lock (cache) botNext[n.Cid] = now.AddSeconds(Math.Max(0.4, cool));
                    if (dmg <= 0) continue;                              // errou o tiro
                    var body = new byte[40];
                    // NeoBotKillsCountForYou: o golpe vai com o seu serial, para o jogo contar o HUV no objetivo da missao
                    BitConverter.GetBytes(cfg.NeoBotKillsCountForYou ? ch.Serial : (ushort) BotSocial.SerialOf(b)).CopyTo(body, 0);
                    BitConverter.GetBytes(ch.Serial).CopyTo(body, 4);
                    BitConverter.GetBytes(n.Number).CopyTo(body, 6);
                    // vida do HUV: a ultima que o seu jogo informou (menos os tiros dos bots depois) ou, sem informacao,
                    // NeoHuvHpPerLevel x nivel. O tiro que zera vai com vida 0 e com o seu nome: o jogo destroi o HUV e
                    // conta no objetivo da missao (como se fosse o seu tiro)
                    int left;
                    lock (cache)
                    {
                        Dictionary<ushort, int> hm;
                        if (!hpOf.TryGetValue(ch.CID, out hm)) hpOf[ch.CID] = hm = new Dictionary<ushort, int>();
                        int cur;
                        if (!hm.TryGetValue(n.Number, out cur) || cur <= 0) cur = EstimateHp(ch, n.Number);
                        left = Math.Max(0, cur - dmg);
                        hm[n.Number] = left;
                    }
                    // nunca manda vida 0: o jogo destroi o HUV sozinho quando o dano somado acaba com a vida dele (mandar 0
                    // antes da hora deixava o HUB "morto" preto flutuando, ainda andando e atirando). A missao conta pelo 3820.
                    if (left <= 0) left = 1;
                    BitConverter.GetBytes(left).CopyTo(body, 0xa);
                    BitConverter.GetBytes(dmg).CopyTo(body, 0xe);
                    BitConverter.GetBytes((long) ch.Mito).CopyTo(body, 0x1c);
                    var pk = new RicePacket(205);
                    pk.Writer.Write(body);
                    gc.Send(pk);
                    // o tiro aparece: 555 VehicleFire do carro do bot (no molde do seu ultimo tiro)
                    try { if (ac != null && cfg.NeoBotShowShots) ShowShot(ac, ch, b, n); } catch (Exception ex) { Log.WriteError("Neo City (tiro do bot): " + ex.Message); }
                    if (botLogs++ < 10) Log.WriteLine("Neo City: {0} atirou no HUV {1:X} ({2:0} m, dano {3}).", b.Name, n.Number, n.Dist, dmg);
                }
            }
        }

        // ---- vida dos bots da party na Neo City --------------------------------------------------------------------
        // Cada bot tem a vida dele (nivel + armadura): vai no estado do carro (XiCarState @0x28 meia-precisao, @0x2a e
        // @0x2e float; DriftCity.exe 0x551560) que o servidor manda - antes ia a sua vida copiada. Os HUVs perto dele
        // tiram vida (NeoHuvDamagePerLevel x nivel do HUV); zerou: some por NeoBotRespawnSeconds e volta cheio.
        class BotLife { public double Cur, Max; public DateTime LastHit = DateTime.MinValue, DownUntil = DateTime.MinValue, LastRegen = DateTime.MinValue; }
        static readonly Dictionary<long, BotLife> lives = new Dictionary<long, BotLife>();
        static int GearLevel(Bots.BotProfile b, string slot)
        {
            string id; if (b == null || b.Parts == null || !b.Parts.TryGetValue(slot, out id)) return 0;
            var e = Structures.Resources.ItemTable.Items.FirstOrDefault(x => x.ID == id) as Structures.Resources.ItemTableEntry;
            int m; return e != null && int.TryParse(e.MinLevel, out m) ? m : 0;
        }
        static double ArmorOf(Bots.BotProfile b)
        {
            string id; if (b == null || b.Parts == null || !b.Parts.TryGetValue("neo_armor", out id)) return 0;
            var e = Structures.Resources.ItemTable.Items.FirstOrDefault(x => x.ID == id) as Structures.Resources.ItemTableEntry;
            return e != null ? Math.Max(0, e.BasePoints) : 0;
        }
        public static double MaxHp(Bots.BotProfile b) { return Math.Max(1000, RiceServer.Config.NeoBotHpBase + b.Level * RiceServer.Config.NeoBotHpPerLevel + ArmorOf(b) * 2); }
        static BotLife LifeOf(Bots.BotProfile b)
        {
            BotLife l;
            lock (lives)
            {
                if (!lives.TryGetValue(b.Cid, out l)) lives[b.Cid] = l = new BotLife();
                double mx = MaxHp(b);
                if (l.Max <= 0) { l.Max = mx; l.Cur = mx; }
                if (Math.Abs(l.Max - mx) > 0.5) { l.Cur = l.Cur / l.Max * mx; l.Max = mx; }
                var now = DateTime.UtcNow;
                if (l.DownUntil != DateTime.MinValue && now >= l.DownUntil) { l.DownUntil = DateTime.MinValue; l.Cur = l.Max; }
                // sem recuperar sozinho: a vida so volta cheia quando ele e destruido e volta (ou no fim do capitulo)
            }
            return l;
        }
        public static bool IsDown(long cid)
        {
            lock (lives) { BotLife l; return lives.TryGetValue(cid, out l) && l.DownUntil != DateTime.MinValue && DateTime.UtcNow < l.DownUntil; }
        }
        static ushort Half(float f)
        {
            uint x = BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);
            int e = (int) ((x >> 23) & 0xff) - 127, sign = (int) (x >> 31); uint man = x & 0x7fffff;
            if (e > 15) { e = 15; man = 0x7fffff; } else if (e < -15) { e = -15; man = 0; }
            return (ushort) (((e - sign * 32 + 15) << 10) + (man >> 13));
        }
        // chamado pelo CityBots ao montar o movimento de um bot da party na Neo City
        public static void ApplyBotState(byte[] move, Bots.BotProfile b, bool inBase = false)
        {
            if (move == null || move.Length < 0x3a + 0x32 || b == null) return;
            var l = LifeOf(b);
            if (inBase)
            {
                // na base ROAD a vida volta como a sua (NeoBaseRegenPercent por segundo)
                lock (lives)
                {
                    var now = DateTime.UtcNow;
                    double secs = l.LastRegen == DateTime.MinValue ? 0 : Math.Min(2, (now - l.LastRegen).TotalSeconds);
                    l.LastRegen = now;
                    if (l.DownUntil != DateTime.MinValue) { l.DownUntil = DateTime.MinValue; l.Cur = Math.Max(l.Cur, 1); }
                    if (l.Cur < l.Max) l.Cur = Math.Min(l.Max, l.Cur + l.Max * RiceServer.Config.NeoBaseRegenPercent / 100.0 * secs);
                }
            }
            float cur = (float) Math.Max(1, l.Cur), mx = (float) l.Max;
            BitConverter.GetBytes(Half(cur)).CopyTo(move, 0x3a + 0x28);
            BitConverter.GetBytes(mx).CopyTo(move, 0x3a + 0x2a);
            BitConverter.GetBytes(cur).CopyTo(move, 0x3a + 0x2e);
        }
        // ---- HUVs atacando os bots -----------------------------------------------------------------------------------
        // O bot so perde vida pelos HUVs (atirar nao tira vida dele): cada HUV vivo ataca quem estiver mais perto dele -
        // voce (ai e o seu jogo que cuida) ou um bot da party. HUV/HUB atira ate NeoHuvShootRange m a cada 1,5-2,5 s;
        // HUS (o que bate) so encostando (NeoHuvRamRange m), a cada 1 s.
        static readonly Dictionary<long, DateTime> huvNext = new Dictionary<long, DateTime>();
        static void HuvAttacks(RiceClient c, Rice.Game.Character ch)
        {
            var cfg = RiceServer.Config;
            if (cfg.NeoHuvDamagePerLevel <= 0) return;
            var pbs = BotSocial.PartyBots(ch).ToList();
            if (pbs.Count == 0) return;
            float shoot = (float) Math.Max(5, cfg.NeoHuvShootRange), ram = (float) Math.Max(2, cfg.NeoHuvRamRange);
            var th = CityBots.NeoThreats(ch, pbs.Select(x => x.Cid).ToList(), shoot);
            var now = DateTime.UtcNow;
            foreach (var g in th.GroupBy(t => t.Number))
            {
                var t = g.OrderBy(x => x.Dist).First();
                if (t.PlayerDist < t.Dist) continue;                     // voce esta mais perto: o HUV vai em voce
                var b = pbs.FirstOrDefault(x => x.Cid == t.Cid);
                if (b == null || IsDown(b.Cid)) continue;
                int kind = KindOf(ch, t.Number);
                if (kind == 3 && t.Dist > ram) continue;                 // HUS: so batendo
                long key = (long) ch.CID * 65536 + t.Number;
                lock (huvNext)
                {
                    DateTime nx;
                    if (huvNext.TryGetValue(key, out nx) && now < nx) continue;
                    huvNext[key] = now.AddSeconds(kind == 3 ? 1.0 : 1.5 + Rng.NextDouble());
                }
                HuvShootsBot(c, ch, b, t.Number);
            }
        }

        static void HuvShootsBot(RiceClient c, Rice.Game.Character ch, Bots.BotProfile b, ushort num)
        {
            var cfg = RiceServer.Config;
            if (cfg.NeoHuvDamagePerLevel <= 0) return;
            int level = 45;
            lock (cache)
            {
                List<Spawn> l;
                if (spawns.TryGetValue(ch.CID, out l))
                {
                    var sp = l.FirstOrDefault(x => BitConverter.ToUInt16(x.Ent, 10) == num);
                    if (sp != null) level = Math.Max(1, (int) BitConverter.ToUInt16(sp.Ent, 4)) * (sp.Boss ? 3 : 1);
                }
            }
            var life = LifeOf(b);
            double arm = ArmorOf(b);
            double dmg = level * cfg.NeoHuvDamagePerLevel * (0.7 + Rng.NextDouble() * 0.6) * (2000.0 / (2000.0 + arm));
            bool died = false;
            lock (lives)
            {
                if (life.DownUntil != DateTime.MinValue) return;
                life.Cur -= dmg; life.LastHit = DateTime.UtcNow;
                if (life.Cur <= 0) { life.Cur = 0; life.DownUntil = DateTime.UtcNow.AddSeconds(Math.Max(3, cfg.NeoBotRespawnSeconds)); died = true; }
            }
            if (died)
            {
                Log.WriteLine("Neo City: {0} foi destruido pelos HUVs (volta em {1} s).", b.Name, cfg.NeoBotRespawnSeconds);
                try { Chat.SendSystemMessage(c, b.Name + " foi destruido pelos HUVs! Volta em " + cfg.NeoBotRespawnSeconds + " s."); } catch (Exception) { }
                try { Bots.Say(c, "party", b.Name, "Fui destruido! Ja volto."); } catch (Exception) { }
            }
        }

        // ---- tiro visivel dos bots: 555 VehicleFire (0x22b, DriftCity.exe 0x865330 / 0x51f300) -------------------------
        // 126 bytes: XiCarMove @0 (no tiro @6 = a arma), XiCarState @0x3a, u32 @0x6c, 3 floats @0x70, u16 @0x7c. O servidor
        // guarda o seu ultimo tiro e usa de molde para o tiro do bot (serial e posicao do bot, mirando no HUV).
        static readonly Dictionary<ulong, byte[]> lastFire = new Dictionary<ulong, byte[]>();
        static int fireLogs;
        [RicePacket(555, RiceServer.ServerType.Area | RiceServer.ServerType.Game, CheckedIn = true)]
        public static void VehicleFire(RicePacket packet)
        {
            var buf = packet.Buffer;
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || buf.Length < 0x7e) return;
            if (BitConverter.ToUInt16(buf, 0) != ch.Serial) return;
            // molde so o tiro de arma do carro (@6 = o seu carro); o da Mine (@6 = modelo do objeto) desenhava um HUV
            bool carShot = ch.Vehicle != null && BitConverter.ToUInt16(buf, 6) == (ushort) ch.Vehicle.CarType;
            if (!carShot) return;
            NeoDurability.OnShot(ch, BitConverter.ToUInt32(buf, 0x6c));      // durabilidade da arma (aplicada a cada minuto)
            lock (lastFire) lastFire[ch.CID] = (byte[]) buf.Clone();
            savedFire = (byte[]) buf.Clone();
            if ((DateTime.UtcNow - fireSaved).TotalSeconds > 30)
            {
                fireSaved = DateTime.UtcNow;
                try { System.IO.File.WriteAllText(FireFile, Convert.ToBase64String(buf, 0, Math.Min(buf.Length, 0x7e))); } catch (Exception) { }
            }
            if (fireLogs++ < 3)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0x6c; i + 4 <= 0x7c; i += 4) sb.AppendFormat(" @{0:X}={1:0.##}", i, BitConverter.ToSingle(buf, i));
                Log.WriteLine("Neo City: seu tiro (555): tipo {0}, u32 {1}, u16 {2}, pos ({3:0},{4:0},{5:0}), vel ({6:0.#},{7:0.#},{8:0.#}),{9}", BitConverter.ToUInt16(buf, 6), BitConverter.ToUInt32(buf, 0x6c), BitConverter.ToUInt16(buf, 0x7c),
                    BitConverter.ToSingle(buf, 0x18), BitConverter.ToSingle(buf, 0x1c), BitConverter.ToSingle(buf, 0x20), BitConverter.ToSingle(buf, 0x28), BitConverter.ToSingle(buf, 0x2c), BitConverter.ToSingle(buf, 0x30), sb);
            }
        }
        static void ShowShot(RiceClient ac, Rice.Game.Character ch, Bots.BotProfile b, CityBots.NeoTarget t)
        {
            byte[] tpl;
            lock (lastFire) { lastFire.TryGetValue(ch.CID, out tpl); if (tpl == null) tpl = savedFire; }
            if (tpl == null || t.Move == null || t.Move.Length < 0x6c) return;
            // tiro de arma do carro (Cannon/Railgun/Wave: no seu tiro @6 = o seu carro): o carro do bot (movimento e estado
            // dele), a arma DELE (@0x6c = numero da arma, o basepoints do item) e a direcao ate o HUV (@0x70). O jogo faz
            // o carro dele disparar. (A Mine vai como objeto - @6 = modelo dela - e nao serve de molde.)
            var f = (byte[]) tpl.Clone();
            Array.Copy(t.Move, 0, f, 0, 0x6c);
            BitConverter.GetBytes((ushort) BotSocial.SerialOf(b)).CopyTo(f, 0);
            int wid = WeaponId(b);
            if (wid > 0) BitConverter.GetBytes((uint) wid).CopyTo(f, 0x6c);
            ApplyBotState(f, b);                                        // a vida dele no estado do tiro tambem
            float bx = BitConverter.ToSingle(t.Move, 0x18), by = BitConverter.ToSingle(t.Move, 0x1c), bz = BitConverter.ToSingle(t.Move, 0x20);
            float dx = t.HX - bx, dy = t.HY - by, dz = t.HZ - bz, len = (float) Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.5f) return;
            float vx = BitConverter.ToSingle(tpl, 0x70), vy = BitConverter.ToSingle(tpl, 0x74);
            if (Math.Sqrt(vx * vx + vy * vy) > 50)
            {
                BitConverter.GetBytes(t.HX).CopyTo(f, 0x70); BitConverter.GetBytes(t.HY).CopyTo(f, 0x74); BitConverter.GetBytes(t.HZ).CopyTo(f, 0x78);
            }
            else
            {
                BitConverter.GetBytes(dx / len).CopyTo(f, 0x70); BitConverter.GetBytes(dy / len).CopyTo(f, 0x74); BitConverter.GetBytes(dz / len).CopyTo(f, 0x78);
            }
            var pk = new RicePacket(555);
            pk.Writer.Write(f, 0, 0x7e);
            ac.Send(pk);
        }
        // numero da arma da Neo City do bot (basepoints do item, ex.: 21130 = U Cannon 98); 0 = sem arma
        public static int WeaponId(Bots.BotProfile b)
        {
            foreach (var slot in new[] { "neo_weapon", "neo_weapon#2" })
            {
                string id; if (b == null || b.Parts == null || !b.Parts.TryGetValue(slot, out id)) continue;
                var e = Structures.Resources.ItemTable.Items.FirstOrDefault(x => x.ID == id) as Structures.Resources.ItemTableEntry;
                if (e != null && e.BasePoints > 20000) return (int) e.BasePoints;
            }
            return 0;
        }
        public static bool HasWeapon(Bots.BotProfile b) { return WeaponId(b) > 0; }
        static readonly Dictionary<ulong, Dictionary<ushort, int>> hpOf = new Dictionary<ulong, Dictionary<ushort, int>>();
        static readonly Dictionary<ulong, double> myDmg = new Dictionary<ulong, double>();
        // ---- armas da Neo City (res/neo_weapons.json, extraido do Init\WeaponList.xlt do jogo) -------------------
        class NeoWeapon { public string name; public int type; public double speed, acc, range, area, dmg, rnd, crit, burstGap, cool; public int burst, auto, guide; }
        static Dictionary<int, NeoWeapon> weapons;
        static Dictionary<int, NeoWeapon> Weapons()
        {
            if (weapons != null) return weapons;
            try
            {
                weapons = System.IO.File.Exists("res/neo_weapons.json")
                    ? Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<int, NeoWeapon>>(System.IO.File.ReadAllText("res/neo_weapons.json"))
                    : new Dictionary<int, NeoWeapon>();
                Log.WriteLine("Neo City: {0} armas na tabela (res/neo_weapons.json).", weapons.Count);
            }
            catch (Exception ex) { Log.WriteError("res/neo_weapons.json: " + ex.Message); weapons = new Dictionary<int, NeoWeapon>(); }
            return weapons;
        }
        // um disparo do bot com a melhor arma dele que alcanca o HUV: dano da arma + aleatorio, por bala da rajada,
        // acerto (arma teleguiada sempre acerta; as outras pela precisao) e critico (x2). cool = tempo ate o proximo.
        static bool RealShot(Bots.BotProfile b, float dist, out int dmg, out double cool)
        {
            dmg = 0; cool = 1.2;
            var tab = Weapons();
            NeoWeapon best = null;
            foreach (var slot in new[] { "neo_weapon", "neo_weapon#2" })
            {
                string id; if (b.Parts == null || !b.Parts.TryGetValue(slot, out id)) continue;
                var e = Structures.Resources.ItemTable.Items.FirstOrDefault(x => x.ID == id) as Structures.Resources.ItemTableEntry;
                NeoWeapon w; if (e == null || !tab.TryGetValue((int) e.BasePoints, out w)) continue;
                double reach = w.range > 0 ? w.range : w.area + 4;      // Wave/Mine: so de perto (area da explosao)
                if (dist > reach) continue;
                double dps = (w.dmg + w.rnd / 2) * Math.Max(1, w.burst) / Math.Max(0.4, w.cool);
                if (best == null || dps > (best.dmg + best.rnd / 2) * Math.Max(1, best.burst) / Math.Max(0.4, best.cool)) best = w;
            }
            if (best == null) return false;
            cool = best.cool;
            // o servidor anda a cada 0,4 s: armas mais rapidas que isso disparam varias rajadas por vez
            int volleys = (int) Math.Max(1, Math.Round(0.4 / Math.Max(0.05, best.cool)));
            for (int v = 0; v < volleys; v++)
                for (int k = 0; k < Math.Max(1, best.burst); k++)
                {
                    bool hit = best.guide == 1 || best.auto == 1 || Rng.NextDouble() * 100 < best.acc;
                    if (!hit) continue;
                    double d = best.dmg + Rng.NextDouble() * best.rnd;
                    if (Rng.NextDouble() * 100 < best.crit) d *= 2;
                    dmg += (int) Math.Round(d);
                }
            return true;
        }
        // dano calculado do tiro do bot: o dano medio dos SEUS tiros nos HUVs (o jogo calcula com a sua arma e a defesa do
        // HUV) x (nivel da arma do bot / nivel da sua arma) x NeoBotDamageFactor. Sem tiro seu ainda: NeoBotDamage x nivel/80.
        static int BotDamage(Rice.Game.Character ch, Bots.BotProfile b, double skill)
        {
            var cfg = RiceServer.Config;
            double av; lock (cache) myDmg.TryGetValue(ch.CID, out av);
            int bl = GearLevel(b, "neo_weapon"); if (bl <= 0) bl = GearLevel(b, "neo_weapon#2"); if (bl <= 0) bl = Math.Min(b.Level, 40);
            double rnd = 0.85 + Rng.NextDouble() * 0.3, sk = 0.9 + (skill - 1.0) * 0.2;
            if (av > 0)
            {
                int pl = MyWeaponLevel(ch); if (pl <= 0) pl = bl;
                return (int) Math.Max(1, Math.Round(av * Math.Max(0.3, Math.Min(2.0, bl / (double) pl)) * cfg.NeoBotDamageFactor * sk * rnd));
            }
            return (int) Math.Max(1, Math.Round(cfg.NeoBotDamage * Math.Max(0.5, Math.Min(1.4, bl / 80.0)) * sk * rnd));
        }
        // nivel da arma do seu ultimo tiro (u32 @0x6c do 555 = numero da arma = basepoints do item)
        static int MyWeaponLevel(Rice.Game.Character ch)
        {
            byte[] f; lock (lastFire) lastFire.TryGetValue(ch.CID, out f);
            if (f == null || f.Length < 0x70) return 0;
            uint wid = BitConverter.ToUInt32(f, 0x6c);
            var e = Structures.Resources.ItemTable.Items.OfType<Structures.Resources.ItemTableEntry>()
                        .FirstOrDefault(x => x.Category == "neo_weapon" && (uint) x.BasePoints == wid);
            int m; return e != null && int.TryParse(e.MinLevel, out m) ? m : 0;
        }
        static int EstimateHp(Rice.Game.Character ch, ushort num)
        {
            int level = 45; bool boss = false;
            List<Spawn> l;
            if (spawns.TryGetValue(ch.CID, out l))
            {
                var sp = l.FirstOrDefault(x => BitConverter.ToUInt16(x.Ent, 10) == num);
                if (sp != null) { level = Math.Max(1, (int) BitConverter.ToUInt16(sp.Ent, 4)); boss = sp.Boss; }
            }
            return (int) Math.Max(500, RiceServer.Config.NeoHuvHpPerLevel * level * (boss ? 5 : 1));
        }
        // numeros de HUV ja destruidos (os bots largam deles)
        public static HashSet<ushort> DeadNumbers(Rice.Game.Character ch)
        {
            var r = new HashSet<ushort>();
            if (ch == null) return r;
            lock (cache)
            {
                HashSet<ushort> dd, kk;
                if (dead.TryGetValue(ch.CID, out dd)) r.UnionWith(dd);
                if (killed.TryGetValue(ch.CID, out kk)) r.UnionWith(kk);
            }
            return r;
        }

        // o ultimo tiro fica salvo (neo_fire.json): os bots atiram mesmo antes de voce atirar na missao
        static readonly string FireFile = Paths.Map("neo_fire.json");
        static byte[] savedFire = LoadFire();
        static DateTime fireSaved = DateTime.MinValue;
        static byte[] LoadFire()
        {
            try
            {
                var f = System.IO.File.Exists(FireFile) ? Convert.FromBase64String(System.IO.File.ReadAllText(FireFile).Trim().Trim('"')) : null;
                return f != null && f.Length >= 0x7e && BitConverter.ToUInt16(f, 6) < 10000 ? f : null;   // molde antigo da Mine: descarta
            }
            catch (Exception) { return null; }
        }

        // ---- kits de recuperacao: o seu vale para a party; os bots usam os que voce deu --------------------------------
        // Personal Recovery Kit (healthupnow): quando voce usa, os bots da party tambem ganham a vida do kit. Na troca o
        // bot aceita kits; ele usa um quando alguem da party (bot) ficar abaixo de NeoBotKitPercent (75%) e avisa quantos
        // sobraram. O kit dele cura a party inteira (os bots).
        public static void PartyHeal(RiceClient c, Rice.Game.Character ch, double value, string who)
        {
            if (ch == null || value <= 0 || ch.Area == null || !(IsNeoArea(ch.Area.ID) || ch.InNeoBase)) return;
            int n = 0;
            foreach (var pb in BotSocial.PartyBots(ch))
            {
                var l = LifeOf(pb);
                lock (lives) { if (l.DownUntil != DateTime.MinValue) continue; l.Cur = Math.Min(l.Max, l.Cur + value); }
                n++;
            }
            if (n > 0) Log.WriteLine("Neo City: kit de {0}: +{1} de vida para {2} bot(s) da party.", who, value, n);
        }
        static readonly Dictionary<long, DateTime> kitNext = new Dictionary<long, DateTime>();
        static void BotKits(RiceClient gc, Rice.Game.Character ch)
        {
            var cfg = RiceServer.Config;
            var party = BotSocial.PartyBots(ch);
            if (party.Count == 0) return;
            double worst = 1.0;
            foreach (var pb in party)
            {
                if (IsDown(pb.Cid)) continue;
                var l = LifeOf(pb);
                worst = Math.Min(worst, l.Cur / Math.Max(1, l.Max));
            }
            if (worst >= cfg.NeoBotKitPercent / 100.0) return;
            var now = DateTime.UtcNow;
            foreach (var pb in party)
            {
                if (pb.NeoKits == null || pb.NeoKits.Count == 0 || IsDown(pb.Cid)) continue;
                DateTime nx;
                lock (kitNext) { if (kitNext.TryGetValue(-1, out nx) && now < nx) return; kitNext[-1] = now.AddSeconds(Math.Max(2, cfg.NeoBotKitSeconds)); }
                var kv = pb.NeoKits.First(x => x.Value > 0);
                var e = Structures.Resources.ItemTable.Items.FirstOrDefault(x => x.ID == kv.Key) as Structures.Resources.UseItemTableEntry;
                double v; if (e == null || !double.TryParse(e.StatModifier, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) v = 200;
                pb.NeoKits[kv.Key] = kv.Value - 1;
                int left = pb.NeoKits.Values.Sum();
                if (pb.NeoKits[kv.Key] <= 0) pb.NeoKits.Remove(kv.Key);
                try { Bots.Save(); } catch (Exception) { }
                PartyHeal(gc, ch, v, pb.Name);
                string nm = e != null ? e.Name : "Recovery Kit";
                try { Bots.Say(gc, "party", pb.Name, "Usei " + nm + " (+" + v + " de vida para a party). Restam " + left + "."); } catch (Exception) { }
                return;
            }
        }

        // 206 de um HUV que um bot acertou por ultimo (e voce nao destruiu): parte da recompensa para voce
        static void BotKill(RiceClient c, Rice.Game.Character ch, HashSet<ushort> vals, ushort dnum)
        {
            var cfg = RiceServer.Config;
            ushort num = 0; long botCid = 0; int level = 60; bool boss = false;
            lock (cache)
            {
                Dictionary<ushort, Tuple<long, DateTime>> bh;
                if (!botHits.TryGetValue(ch.CID, out bh)) return;
                // 206: @2 = numero do destruido. O HUB da missao e o numero 1 - o mesmo do seu serial (@0), por isso antes
                // ele nunca era reconhecido como destruido por um bot
                Tuple<long, DateTime> t;
                if (dnum >= 1 && bh.TryGetValue(dnum, out t) && (DateTime.UtcNow - t.Item2).TotalSeconds < 15) { num = dnum; botCid = t.Item1; }
                else
                    foreach (var v in vals)
                        if (v >= 0x200 && bh.TryGetValue(v, out t) && (DateTime.UtcNow - t.Item2).TotalSeconds < 15) { num = v; botCid = t.Item1; break; }
                if (num == 0) return;
                bh.Remove(num);
                HashSet<ushort> k;
                if (!killed.TryGetValue(ch.CID, out k)) killed[ch.CID] = k = new HashSet<ushort>();
                if (!k.Add(num)) return;                               // voce ja tinha destruido (ja pagou)
                List<Spawn> l;
                if (spawns.TryGetValue(ch.CID, out l))
                {
                    var sp = l.FirstOrDefault(x => BitConverter.ToUInt16(x.Ent, 10) == num);
                    if (sp != null) { level = Math.Max(1, (int) BitConverter.ToUInt16(sp.Ent, 4)); boss = sp.Boss; }
                }
            }
            var b = Bots.ByCid(botCid);
            double mult = (boss ? cfg.NeoBossRewardMultiplier : 1.0) * Math.Max(0, cfg.NeoBotKillShare);
            uint exp = (uint) Math.Max(0, Math.Round(level * cfg.NeoHuvExpPerLevel * mult));
            uint mito = (uint) Math.Max(0, Math.Round(level * cfg.NeoHuvMitoPerLevel * mult));
            if (exp > 0) ch.GrantExperience(exp);
            if (mito > 0) ch.GrantMito(mito);
            lock (cache) { var r = GetRun(ch.CID); r.Exp += exp; r.Mito += mito; r.BotKills++; }
            string party = "";
            try { party = PartyReward(ch, level, boss ? cfg.NeoBossRewardMultiplier : 1.0, botCid); } catch (Exception ex) { Log.WriteError("Neo City (party): " + ex.Message); }
            try { Chat.SendSystemMessage(c, (b != null ? b.Name : "Um bot") + " destruiu " + (boss ? "o chefe" : "um HUV") + ": voce ganhou +" + exp + " exp e +" + mito + " mito." + party); } catch (Exception) { }
            Log.WriteLine("Neo City: {0} destruiu o HUV {1:X}{2}: voce ganhou +{3} exp, +{4} mito.", b != null ? b.Name : "um bot", num, boss ? " (chefe)" : "", exp, mito);
            if (cfg.NeoBotKillsCountForYou) try { CountForMission(c, ch, num); } catch (Exception ex) { Log.WriteError("Neo City (missao): " + ex.Message); }
            try { ch.FlushNotices(c); } catch (Exception) { }
        }

        // HUV destruido (por voce ou por um bot): os bots da party tambem ganham. Quem destruiu ganha o premio inteiro do
        // HUV; os outros, NeoBotKillShare dele (o mesmo que voce ganha quando e um bot que destroi). Sobe de nivel e salva.
        static string PartyReward(Rice.Game.Character ch, int level, double mult, long killerCid)
        {
            var cfg = RiceServer.Config;
            var pbs = BotSocial.PartyBots(ch).ToList();
            if (pbs.Count == 0) return "";
            double share = Math.Max(0, cfg.NeoBotKillShare);
            long fe = (long) Math.Round(level * cfg.NeoHuvExpPerLevel * mult), fm = (long) Math.Round(level * cfg.NeoHuvMitoPerLevel * mult);
            long se = (long) Math.Round(fe * share), sm = (long) Math.Round(fm * share);
            var ups = new List<string>();
            foreach (var pb in pbs)
            {
                bool k = pb.Cid == killerCid;
                long e = k ? fe : se, m = k ? fm : sm;
                if (pb.Mito < 0) pb.Mito = 0;
                pb.Mito += m;
                if (Bots.AddExp(pb, e) > 0) ups.Add(pb.Name + " subiu para o nivel " + pb.Level);
            }
            try { Bots.Save(); } catch (Exception) { }
            string txt = killerCid != 0
                ? " Party: " + (Bots.ByCid(killerCid) != null ? Bots.ByCid(killerCid).Name : "o bot") + " +" + fe + " exp/+" + fm + " mito" + (pbs.Count > 1 ? ", os outros +" + se + " exp/+" + sm + " mito" : "") + "."
                : " Party: +" + se + " exp/+" + sm + " mito para cada bot.";
            if (ups.Count > 0) txt += " " + string.Join(", ", ups) + "!";
            Log.WriteLine("Neo City: party de {0} ganhou pelo HUV:{1}", ch.Name, txt);
            return txt;
        }

        // item que cai do HUV: arma, armadura, Amplifier ou Fuse da Neo City do grau do seu nivel
        static readonly Random Rng = new Random();
        static void Drop(RiceClient c, Rice.Game.Character ch, bool boss)
        {
            var cfg = RiceServer.Config;
            if (Rng.NextDouble() >= (boss ? cfg.NeoDropBossChance : cfg.NeoDropChance)) return;
            int times = VisualShop.DropCount(ch);                     // Item Drink da Drift Shop: itens em dobro
            for (int t = 0; t < times; t++)
            {
                var it = PickNeo(ch.Level, boss);
                if (it == null) return;
                Rice.Game.Item got;
                if (!ch.GrantItem(it.ID, 1, out got)) return;
                Log.WriteLine("Neo City: {0} ganhou {1} do HUV{2}.", ch.Name, it.Name, boss ? " chefe" : "");
                try { Chat.SendSystemMessage(c, (t > 0 ? "[Item Drink] " : "") + "O HUV deixou cair: " + it.Name + "!"); } catch (Exception) { }
                try { Misc.GetItemFx(c, got); } catch (Exception) { }
            }
            ch.FlushModInfo(c);
        }
        static Structures.Resources.ItemTableEntry PickNeo(int level, bool boss)
        {
            int lv = Math.Min(level, 108);
            var pool = Structures.Resources.ItemTable.Items.OfType<Structures.Resources.ItemTableEntry>()
                .Where(x => x.Category != null && x.Category.StartsWith("neo_") && x.ID != null && !x.ID.EndsWith("9999") && !x.ID.EndsWith("99998")
                            && (x.Category == "neo_weapon" || x.Category == "neo_armor" || x.Category == "neo_coil" || x.Category == "neo_fuse"))
                .Where(x => { int m; return int.TryParse(x.MinLevel, out m) && m <= lv && m >= lv - (boss ? 6 : 12); }).ToList();
            if (pool.Count == 0) return null;
            return pool[Rng.Next(pool.Count)];
        }
        [RicePacket(207, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void HubTargeting(RicePacket packet) { Relay(packet, 207, 9, "mira do HUB (207)"); }
        [RicePacket(208, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Aggro(RicePacket packet) { Relay(packet, 208, 14, "aggro (208)"); }
        [RicePacket(3919, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void HusExplosion(RicePacket packet) { Relay(packet, 3920, 16, "explosao (3919)"); }
        [RicePacket(3907, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void TurretSwitch(RicePacket packet) { Log.WriteLine("Neo City: torre (3907): {0}", Hex(packet.Buffer)); }

        static readonly Dictionary<int, int> relayLog = new Dictionary<int, int>();
        static void Relay(RicePacket packet, ushort id, int n, string what)
        {
            int c; relayLog.TryGetValue(id, out c); relayLog[id] = c + 1;
            if (c < 15) Log.WriteLine("Neo City: {0}: {1}", what, Hex(packet.Buffer, 64));
            Echo(packet, id, n);
        }

        // ainda sem resposta: so registra (capitulo completo / recompensa do capitulo / outros)
        // 3802 NeoChapterComplete (u32 capitulo, u32 ?) -> 3803 NeoChapterCompleteAck (DriftCity.exe 0x52e960): u32 capitulo,
        // u32 n, u8 ?, 176 bytes + n x 16 (itens da recompensa). O jogo marca o capitulo como feito e mostra a janela do fim.
        // A exp/mito ja foi paga nas quests (3806), entao aqui vai sem itens extras.
        [RicePacket(3802, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChapterComplete(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("Neo City: capitulo completo (3802): {0}", Hex(buf));
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            uint chapter = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0u;
            Run r; lock (cache) { r = GetRun(ch.CID); runs.Remove(ch.CID); }
            int secs = (int) Math.Max(0, (DateTime.Now - r.Start).TotalSeconds);
            // nota (0 = D, 1 = C, 2 = B, 3 = A, 4 = S): comeca em S e cai 1 por quest falhada e 1 por vez que o carro foi
            // destruido; mais de 30 min tambem tira 1
            int grade = 4 - r.Fails - r.Deaths - (secs > 1800 ? 1 : 0);
            if (grade < 0) grade = 0;
            // premio do capitulo: 1 item da Neo City do seu nivel (melhor com nota alta)
            int itemIdx = 0; string itemName = null;
            if (RiceServer.Config.NeoChapterBonusItem)
            {
                var it = PickNeo(ch.Level, grade >= 3);
                Rice.Game.Item got;
                if (it != null && ch.GrantItem(it.ID, 1, out got))
                {
                    itemIdx = Math.Max(0, Structures.Resources.ItemTable.Items.FindIndex(x => x.ID == it.ID));
                    itemName = it.Name;
                    try { ch.FlushModInfo(c); } catch (Exception) { }
                }
            }
            // 3803 (DriftCity.exe 0x52e960 -> 0x71f2b0): u32 capitulo, u32 n jogadores, u8 ?, e o quadro:
            // u32 tempo (s), u32 quests falhadas, u32 vezes destruido, u32 nota, e 4 linhas de 44 bytes: u32 serial,
            // nivel, HUV kill, HUB kill, HUS kill, exp, mito, e 16 bytes do premio (u32 item da tabela + 12 bytes)
            var pk = new RicePacket(3803);
            pk.Writer.Write(chapter);
            pk.Writer.Write(1u);
            pk.Writer.Write((byte) 0);
            pk.Writer.Write((uint) secs); pk.Writer.Write((uint) r.Fails); pk.Writer.Write((uint) r.Deaths); pk.Writer.Write((uint) grade);
            for (int i = 0; i < 4; i++)
            {
                if (i == 0)
                {
                    pk.Writer.Write((uint) ch.Serial); pk.Writer.Write((uint) ch.Level);
                    pk.Writer.Write((uint) r.Huv); pk.Writer.Write((uint) r.Hub); pk.Writer.Write((uint) r.Hus);
                    pk.Writer.Write((uint) Math.Min(r.Exp, uint.MaxValue)); pk.Writer.Write((uint) Math.Min(r.Mito, uint.MaxValue));
                    pk.Writer.Write((uint) itemIdx); pk.Writer.Write(new byte[12]);
                }
                else pk.Writer.Write(new byte[44]);
            }
            c.Send(pk);
            Log.WriteLine("Neo City: capitulo {0} terminado: {1} s, {2} falha(s), {3} morte(s), nota {4}, {5}/{6}/{7} HUV/HUB/HUS, {8} exp, {9} mito{10}{11}.",
                chapter, secs, r.Fails, r.Deaths, "DCBAS"[grade], r.Huv, r.Hub, r.Hus, r.Exp, r.Mito, r.BotKills > 0 ? ", " + r.BotKills + " HUV(s) dos bots" : "", itemName != null ? ", premio " + itemName : "");
            if (itemName != null) try { Chat.SendSystemMessage(c, "Premio do capitulo: " + itemName + "!"); } catch (Exception) { }
        }
        // placar do capitulo (tela CHAPTER CLEARED)
        class Run { public DateTime Start = DateTime.Now; public int Fails, Deaths, Huv, Hub, Hus, BotKills; public long Exp, Mito; }
        static readonly Dictionary<ulong, Run> runs = new Dictionary<ulong, Run>();
        static Run GetRun(ulong cid) { Run r; if (!runs.TryGetValue(cid, out r)) runs[cid] = r = new Run(); return r; }
        [RicePacket(3816, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChapterReward(RicePacket packet) { Log.WriteLine("Neo City: recompensa do capitulo (3816): {0}", Hex(packet.Buffer)); }
        // 3826 (cidade, capitulo, u32): o jogo pede a mensagem de "carro destruido" -> 3827 NeoDyingMsgReq (mesmos 12 bytes)
        [RicePacket(3826, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Neo3826(RicePacket packet)
        {
            Log.WriteLine("Neo City: carro destruido (3826): {0}", Hex(packet.Buffer));
            lock (cache) GetRun(packet.Sender.Player.ActiveCharacter.CID).Deaths++;
            Echo(packet, 3827, 12);
        }

        // 3904 (u32 1, u16 numero do HUV): o jogo tira da lista um HUV destruido -> 3903 ScriptNeoHuvDelRes (vazio). Depois
        // ele mesmo pede um HUV novo (198) no lugar: e assim que os HUVs (e o chefe) voltam.
        // 3902 (vazio): o jogo pede para tirar os HUVs "de roteiro" da etapa que acabou (os que aparecem so naquela
        // parte da missao) -> 3903 ScriptNeoHuvDelRes (vazio; o jogo apaga eles)
        [RicePacket(3902, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ScriptHuvDel(RicePacket packet) { packet.Sender.Send(new RicePacket(3903)); }

        [RicePacket(3913, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Neo3913(RicePacket packet) { }

        [RicePacket(3904, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void HuvDel(RicePacket packet) { packet.Sender.Send(new RicePacket(3903)); }
        [RicePacket(3828, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Neo3828(RicePacket packet) { Log.WriteLine("Neo City: 3828: {0}", Hex(packet.Buffer)); }

        [RicePacket(3829, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChapterSelect(RicePacket packet)
        {
            // o jogo manda ao clicar nos capitulos/operacoes da lista (u32 indice, u32 0)
            if (packet.Buffer.Length >= 4) Log.WriteLine("Neo City: lista, item {0}.", BitConverter.ToUInt32(packet.Buffer, 0));
        }
    }
}
