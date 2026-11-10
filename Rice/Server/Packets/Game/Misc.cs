using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server.Core;
using Rice.Game;

namespace Rice.Server.Packets.Game
{
    public static class Misc
    {
        // 3725 (0xE8D): o DriftCity.exe (funcao 0x788750 -> 0x867b50) soma dois valores float do carro (campos +0x9e4 e
        // +0xb84 do jogador) e manda so esse float (4 bytes) quando ele muda, sem esperar resposta (nao tem handler de
        // resposta no cliente). Pelo valor (ex.: 1310) e o total do carro reportado ao servidor. Antes caia como
        // "unknown packet"; agora so guardamos o ultimo valor (aparece no log uma vez quando muda).
        public static readonly Dictionary<ulong, float> ReportedCarTotal = new Dictionary<ulong, float>();
        [RicePacket(3725, RiceServer.ServerType.Game)]
        public static void CarTotalReport(RicePacket packet)
        {
            if (packet.Buffer == null || packet.Buffer.Length < 4) return;
            float v = BitConverter.ToSingle(packet.Buffer, 0);
            ulong key = (ulong) packet.Sender.GetHashCode();
            float old;
            lock (ReportedCarTotal)
            {
                if (ReportedCarTotal.TryGetValue(key, out old) && Math.Abs(old - v) < 0.01f) return;
                ReportedCarTotal[key] = v;
            }
            Log.WriteLine("Carro: total reportado pelo jogo (3725) = {0}", v);
        }

        [RicePacket(784, RiceServer.ServerType.Game)]
        public static void GetDateTime(RicePacket packet)
        {
            /*
            var ack = new RicePacket(785);
            var timestamp = (Int32)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            ack.Writer.Write(packet.Reader.ReadUInt32());
            ack.Writer.Write(packet.Reader.ReadUInt32());
            ack.Writer.Write(packet.Reader.ReadSingle());
            ack.Writer.Write(Environment.TickCount);
            ack.Writer.Write(timestamp);
            ack.Writer.Write(1);
            ack.Writer.Write((short)DateTime.Now.Year);
            ack.Writer.Write((short)DateTime.Now.Month);
            ack.Writer.Write((short)DateTime.Now.Day);
            ack.Writer.Write((short)DateTime.Now.DayOfWeek);
            ack.Writer.Write((byte)DateTime.Now.Hour);
            ack.Writer.Write((byte)DateTime.Now.Minute);
            ack.Writer.Write((byte)DateTime.Now.Second);
            ack.Writer.Write((byte)0);
            packet.Sender.Send(ack);
            */
        }
        [RicePacket(120, RiceServer.ServerType.Game)]
        public static void CheckInGame(RicePacket packet)
        {
            Log.WriteLine("CheckInGame");
            var version = packet.Reader.ReadUInt32();
            var ticket = packet.Reader.ReadUInt32();
            var username = packet.Reader.ReadUnicodeStatic(32);
            // Followed by int m_IsPcBang, 21 bytes of weird shit we don't know about, and yet another 21 byte chunk of weird shit we don't know about.

            var serverTicket = RiceServer.GetTicket(ticket);

            if (serverTicket == null || !serverTicket.ValidateOrigin(packet.Sender, username))
            {
                Log.WriteLine("Ticket is non-existent or invalid for current user.");
                Log.WriteLine("ticket: {0}, packet sender: {1}, username: {2}", serverTicket.Identifier, packet.Sender.GetRemoteIP(), username);
                packet.Sender.Error("water u even doin");
                return;
            }

            packet.Sender.Player = serverTicket.GetOwner();
            packet.Sender.Player.GameClient = packet.Sender;
            BotSocial.RerollOnline(packet.Sender.Player.ActiveCharacter);   // cada entrada: outros bots online / na rua

            var ack = new RicePacket(121);
            ack.Writer.Write(1L);
            packet.Sender.Send(ack);
        }

        [RicePacket(125, RiceServer.ServerType.Game)]
        public static void JoinChannel(RicePacket packet)
        {
            var serial = RiceServer.CreateSerial(packet.Sender);
            if (packet.Sender.Player != null && packet.Sender.Player.ActiveCharacter != null)
                packet.Sender.Player.ActiveCharacter.Serial = serial.Identifier;   // usado pela Battle Zone

            var ack = new RicePacket(126);
            ack.Writer.WriteUnicodeStatic("speeding", 10);
            ack.Writer.WriteUnicodeStatic(packet.Sender.Player.ActiveCharacter.Name, 16);
            ack.Writer.Write(serial.Identifier);
            ack.Writer.Write((ushort)123); // Age
            packet.Sender.Send(ack);
            Weather.SendTo(packet.Sender);
            Bots.GreetOnJoin(packet.Sender);
        }

        [RicePacket(4, RiceServer.ServerType.Game)]
        public static void Latency(RicePacket packet)
        {
            // TODO: log SkidRush and check this

            float time = packet.Reader.ReadSingle();
            //Log.WriteLine("Latency: {0}", time);

            var ack = new RicePacket(4); // no idea if this is right, pls check Cmd_Latency on client
            ack.Writer.Write(time);
            packet.Sender.Send(ack);
        }
        
        [RicePacket(3916, RiceServer.ServerType.Game)]
        public static void LatencyRelated(RicePacket packet)
        {
            // TODO: log SkidRush and check this
            // possibly Cmd_PartyPing (client's Cmd_PartyPing handler is 3915)

            float time = packet.Reader.ReadSingle();
            //Log.WriteLine("Latency??: {0}", BitConverter.ToString(packet.Buffer));
        }

        [RicePacket(3917, RiceServer.ServerType.Game)]
        public static void UnknownSync(RicePacket packet)
        {
            // hide sync packets for now
        }

        // Mostra os bytes de um pacote (hex + leitura como int/float de 4 em 4 bytes).
        private static string DescribeBuffer(byte[] buf)
        {
            var sb = new StringBuilder();
            sb.Append(BitConverter.ToString(buf));
            for (int i = 0; i + 4 <= buf.Length; i += 4)
                sb.AppendFormat("\n      @{0}: int {1}  float {2}", i, BitConverter.ToInt32(buf, i), BitConverter.ToSingle(buf, i));
            return sb.ToString();
        }

        private static readonly Dictionary<int, KeyValuePair<DateTime, float>> lastFuelReport =
            new Dictionary<int, KeyValuePair<DateTime, float>>();

        // Enviado pelo cliente a cada ~60s: [0] = id do carro, [12] = combustivel atual (float).
        [RicePacket(721, RiceServer.ServerType.Game)]
        public static void DriveInfoUpdate(RicePacket packet)
        {
            var buf = packet.Buffer;
            var character = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (buf.Length < 16 || character == null)
                return;

            int carId = BitConverter.ToInt32(buf, 0);
            float fuel = BitConverter.ToSingle(buf, 12);
            character.UpdateFuel(carId, fuel);

            var vehicle = character.Garage.FirstOrDefault(g => g.CarID == carId);
            string extra = "";
            lock (lastFuelReport)
            {
                KeyValuePair<DateTime, float> prev;
                if (vehicle != null && lastFuelReport.TryGetValue(carId, out prev) && fuel < prev.Value)
                {
                    double minutes = (DateTime.Now - prev.Key).TotalMinutes;
                    if (minutes > 0.05)
                    {
                        double perMin = (prev.Value - fuel) / minutes;
                        double fullTankMin = Rice.Game.Vehicle.ScaledCapacity(vehicle.VehicleUpgradeEntry) / perMin;
                        extra = string.Format(" | consumo {0:0.0} L/min, tanque cheio dura ~{1:0} min", perMin, fullTankMin);
                    }
                }
                lastFuelReport[carId] = new KeyValuePair<DateTime, float>(DateTime.Now, fuel);
            }

            Log.WriteLine("Combustivel do carro {0}: {1:0.0} L{2}", carId, fuel, extra);
        }

        // Botao "Fill up" do posto. Pedido: [0] = id do carro, [4] = preco em mito, [12] = litros (float).
        // O pedido com 0 litros e so a abertura da tela do posto.
        [RicePacket(723, RiceServer.ServerType.Game)]
        public static void FuelChargeReq(RicePacket packet)
        {
            var buf = packet.Buffer;
            var character = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (buf.Length < 20 || character == null)
                return;

            int carId = BitConverter.ToInt32(buf, 0);
            int cost = BitConverter.ToInt32(buf, 4);
            float liters = BitConverter.ToSingle(buf, 12);

            if (liters <= 0.001f)
                return;

            Log.WriteLine("FuelChargeReq: carro {0}, {1} L, custo {2} mito.", carId, liters, cost);

            float newFuel;
            if (!character.Refuel(carId, liters, cost, out newFuel))
            {
                Log.WriteError($"Abastecimento recusado: carro {carId}, custo {cost} mito, mito do corredor {character.Mito}.");
                return;
            }

            Log.WriteLine("Abastecido: carro {0} com {1:0.0} L (mito restante: {2}).", carId, newFuel, character.Mito);

            // Resposta: eco do pedido, mas com os litros como numero INTEIRO em [12]
            // (o jogo enchia o tanque ate o maximo ao receber o float ali).
            var ackBytes = (byte[]) buf.Clone();
            BitConverter.GetBytes((int) Math.Round(liters)).CopyTo(ackBytes, 12);
            var ack = new RicePacket(724);
            ack.Writer.Write(ackBytes);
            packet.Sender.Send(ack);
        }

        // ---- Combo da party (chega na conexao de area; formato do openDC) ----
        // 566 EnterGroup (i32 grupo = party) -> 578 (grupo, combos da party); 567 ExitGroup;
        // 572 GroupCombo (i32 serial, i32 grupo, tipo, tempo, f32, i32, u32 meu total, u32 combo da party @0x1c)
        //   -> o mesmo de volta com a contagem da party; 575 GroupComboReset (serial, grupo) zera.
        // Os bots da party tambem fazem combos: cada um soma +1 com chance PartyBotComboChance.
        static readonly Dictionary<int, uint> partyCombo = new Dictionary<int, uint>();
        static readonly Random comboRng = new Random();

        [RicePacket(572, RiceServer.ServerType.Game | RiceServer.ServerType.Area)]
        public static void GroupCombo(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 0x1c) { Log.WriteLine("GroupCombo ({0} bytes): {1}", p.Length, DescribeBuffer(p)); return; }
            int group = BitConverter.ToInt32(p, 4);
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            var cfg = RiceServer.Config;
            uint before, count;
            int bots = ch != null ? BotSocial.PartyBots(ch).Count : 0;
            lock (partyCombo)
            {
                uint n;
                partyCombo.TryGetValue(group, out n);
                before = n;
                n += 1;
                for (int i = 0; i < bots; i++) if (comboRng.NextDouble() < cfg.PartyBotComboChance) n++;
                partyCombo[group] = count = n;
            }
            var ack = new RicePacket(572);
            ack.Writer.Write(p, 0, 0x1c);
            ack.Writer.Write(count);
            packet.Sender.Send(ack);
            // recompensa da party a cada PartyComboRewardEvery combos
            int every = Math.Max(1, cfg.PartyComboRewardEvery);
            if (ch != null && cfg.PartyComboRewards && cfg.PartyComboRewardMito > 0 && count / every > before / every)
            {
                long mito = cfg.PartyComboRewardMito * (count / every);
                ch.GrantMito((ulong) mito);
                var gc = packet.Sender.Player.GameClient;
                if (gc != null) Chat.SendSystemMessage(gc, string.Format("Combo da party {0}! +{1} mito.", count, mito));
                BotSocial.ShareRewards(ch, 0, mito);
            }
        }

        // 740 TrafficItemEvent: o cliente envia a cada marco de combo (20, 40, 60, 80, 100...).
        // [0] int 1, [4] int tick, [8] float = marco do combo, [12] int, [16] int, [20] int.
        // Recompensa: ComboRewardMito x (marco / ComboRewardEvery). O combo reinicia no proprio jogo.
        // 741 TrafficItemEventAck (u32 quantidade, u32 TableIdx do item): animacao "Get Item" com a voz. Usado no combo
        // e nos itens que caem ao bater no HUV e no Rush.
        public static void GetItemFx(RiceClient c, Rice.Game.Item it, uint count = 1)
        {
            if (c == null || it == null || !RiceServer.Config.ComboGetItemAnimation) return;
            try
            {
                var ack = new RicePacket(741);
                ack.Writer.Write(Math.Max(1u, count));
                ack.Writer.Write(it.GetInfo().TableIdx);
                c.Send(ack);
            }
            catch (Exception ex) { Log.WriteError("Get Item: " + ex.Message); }
        }

        [RicePacket(740, RiceServer.ServerType.Game)]
        public static void TrafficItemEvent(RicePacket packet)
        {
            var buf = packet.Buffer;
            var character = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            var cfg = RiceServer.Config;
            if (buf.Length < 12 || character == null || cfg.ComboRewardEvery <= 0)
                return;

            int combo = (int) Math.Round(BitConverter.ToSingle(buf, 8));
            if (combo < cfg.ComboRewardEvery || combo % cfg.ComboRewardEvery != 0)
            {
                Log.WriteLine("TrafficItemEvent (marco {0}) sem recompensa: {1}", combo, DescribeBuffer(buf));
                return;
            }

            long multiplier = combo / cfg.ComboRewardEvery;
            long reward = cfg.ComboRewardScale ? cfg.ComboRewardMito * multiplier : cfg.ComboRewardMito;
            if (reward > 0) character.GrantMito((ulong) reward);
            uint exp = (uint) Math.Max(0, cfg.ComboRewardExp);   // exp fixa por marco de combo
            if (exp > 0) character.GrantExperience(exp);
            Log.WriteLine("Combo {0}: +{1} mito e +{2} exp para {3}.", combo, reward, exp, character.Name);
            Chat.SendSystemMessage(packet.Sender,
                string.Format("Combo {0}! +{1} mito e +{2} exp.", combo, reward, exp));

            if (cfg.ComboItemRewards)
            {
                var shown = Rewards.GrantComboItems(character, combo, (int) multiplier);
                character.FlushModInfo(packet.Sender);
                character.FlushNotices(packet.Sender);
                // 741 TrafficItemEventAck (u32 quantidade, u32 TableIdx do item): o jogo mostra o "Get Item" com a voz
                // (DriftCity.exe: com quantidade 0 ele mostra uma mensagem de erro; senao toca o som 20204 e a animacao)
                if (shown != null && cfg.ComboGetItemAnimation)
                {
                    bool dmg = shown.ID == "i_n_00093" || shown.ID == "i_n_00094" || shown.ID == "i_n_00095";
                    GetItemFx(packet.Sender, shown, (uint) (dmg ? Math.Max(1, cfg.ComboDamageCount) : 1));
                }
            }
        }

        // 1156 (0x484): o cliente pede os bonus de party (assists) depois de cada StatUpdate em party e repete sem
        // parar ate receber 761 PartyEnChantUpdate (XiStrEnChantBonus, 44 bytes no 2016). Sem bonus: zeros.
        static readonly Dictionary<RiceClient, DateTime> enchantSent = new Dictionary<RiceClient, DateTime>();

        [RicePacket(1156, RiceServer.ServerType.Game)]
        public static void PartyEnchantReq(RicePacket packet)
        {
            lock (enchantSent)
            {
                DateTime last;
                if (enchantSent.TryGetValue(packet.Sender, out last) && (DateTime.UtcNow - last).TotalMilliseconds < 500) return;
                enchantSent[packet.Sender] = DateTime.UtcNow;
            }
            var ack = new RicePacket(761);
            ack.Writer.Write(new byte[44]);
            packet.Sender.Send(ack);
        }

        [RicePacket(575, RiceServer.ServerType.Game | RiceServer.ServerType.Area)]
        public static void GroupComboReset(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length >= 8) lock (partyCombo) partyCombo[BitConverter.ToInt32(p, 4)] = 0;
        }

        [RicePacket(566, RiceServer.ServerType.Game | RiceServer.ServerType.Area)]
        public static void EnterGroup(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 4) return;
            int group = BitConverter.ToInt32(p, 0);
            uint n;
            lock (partyCombo) partyCombo.TryGetValue(group, out n);
            var ack = new RicePacket(578);
            ack.Writer.Write(group);
            ack.Writer.Write(n);
            packet.Sender.Send(ack);
        }

        [RicePacket(567, RiceServer.ServerType.Game | RiceServer.ServerType.Area)]
        public static void ExitGroup(RicePacket packet)
        {
            Log.WriteLine("ExitGroup ({0} bytes): {1}", packet.Buffer.Length, DescribeBuffer(packet.Buffer));
        }
    }
}
