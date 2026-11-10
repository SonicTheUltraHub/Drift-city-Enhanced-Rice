using System;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Battle Zone (lista de salas) e Crew Center no modo offline.
    // Formatos do cliente 2016 (ItemInfo de 96 bytes = layout 2016), conforme a documentacao do emulador openDC.
    public static class OfflineLobby
    {
        static readonly System.Collections.Generic.Dictionary<RiceClient, uint> channels =
            new System.Collections.Generic.Dictionary<RiceClient, uint>();

        // Canal PvP (secao da Battle Zone) escolhido pelo jogador no lobby.
        public static uint CurrentChannel(RiceClient c)
        {
            uint ch;
            lock (channels) return channels.TryGetValue(c, out ch) ? ch : 0u;
        }

        private static int Now()
        {
            return (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // 440 RegisterRoomObserver (u32 canal PvP, filtro, pagina, tamanho da pagina)
        // -> 441 RegisterRoomObserverAck (i32 1, canal, filtro, pagina, u32 Real Match ligado, u32 hora, + 12 bytes)
        // -> 464 RoomListAck (u32 pagina, u32 ultima pagina, u32 quantidade, i32 hora, salas...): sem salas no offline.
        [RicePacket(440, RiceServer.ServerType.Game)]
        public static void RegisterRoomObserver(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("RegisterRoomObserver ({0} bytes): {1}", buf.Length, BitConverter.ToString(buf));
            uint channel = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0;
            uint filter = buf.Length >= 8 ? BitConverter.ToUInt32(buf, 4) : 3;
            uint page = buf.Length >= 12 ? BitConverter.ToUInt32(buf, 8) : 0;
            lock (channels) channels[packet.Sender] = channel;
            try { if (packet.Sender.Player != null) CityBots.LearnPost(packet.Sender.Player.ActiveCharacter); } catch (Exception) { }

            var ack = new RicePacket(441);
            ack.Writer.Write(1);
            ack.Writer.Write(channel);
            ack.Writer.Write(filter);
            ack.Writer.Write(page);
            ack.Writer.Write(0);        // Real Match desligado
            ack.Writer.Write(0);        // hora do Real Match
            ack.Writer.Write(0);        // cauda de 12 bytes (campeonato: nenhum)
            ack.Writer.Write(0);
            ack.Writer.Write(Now());
            packet.Sender.Send(ack);

            var rooms = Battle.ChallengeEntries(channel);   // salas de desafio dos bots (BotChatter)
            var list = new RicePacket(464);
            list.Writer.Write(0);       // pagina
            list.Writer.Write(0);       // ultima pagina
            list.Writer.Write(rooms.Count);   // quantidade de salas
            list.Writer.Write(Now());
            foreach (var r in rooms) list.Writer.Write(r);
            packet.Sender.Send(list);
        }

        [RicePacket(442, RiceServer.ServerType.Game)]
        public static void UnregisterRoomObserver(RicePacket packet)
        {
            Log.WriteLine("UnregisterRoomObserver ({0} bytes): {1}", packet.Buffer.Length, BitConverter.ToString(packet.Buffer));
        }

        // 840 MyTeamInfo (u32 act) -> 841 (u32 act, i64 cid, i32 rank, XiStrTeamInfo 664 bytes, u16 age, 8 bytes).
        // Sem crew: rank -1, TeamId -1, TeamMarkId -1 e o resto zerado.
        [RicePacket(840, RiceServer.ServerType.Game)]
        public static void MyTeamInfo(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("MyTeamInfo ({0} bytes): {1}", buf.Length, BitConverter.ToString(buf));
            uint act = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0;
            Crew.SendMyInfo(packet.Sender, act);
        }
    }
}
