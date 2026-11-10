using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Game;
using Rice.Server.Core;

namespace Rice.Server.Packets.Area
{
    class Area
    {
        [RicePacket(562, RiceServer.ServerType.Area)]
        public static void EnterArea(RicePacket packet)
        {
            //TODO: Associate instance with gameserver player based on provided serial, verify, handle
            var serial = packet.Reader.ReadUInt16();
            var name = packet.Reader.ReadUnicodeStatic(21);
            var area = packet.Reader.ReadInt32();
            var group = packet.Reader.ReadInt32();
            var localtime = packet.Reader.ReadInt32();

            if (packet.Sender.Player == null)
            {
                var serverSerial = RiceServer.GetSerial(serial);

                if (serverSerial == null || serverSerial.GetOwner().ActiveCharacter.Name != name)
                {
                    Log.WriteLine("Serial non-existent or invalid for current user.");
                    packet.Sender.Error("water u even doin");
                    return;
                }

                packet.Sender.Player = serverSerial.GetOwner();
                packet.Sender.Player.ActiveCharacter.Serial = serial;
                packet.Sender.Player.AreaClient = packet.Sender;
            }

            RiceServer.GetArea(area).AddPlayer(packet.Sender.Player);

            var ack = new RicePacket(563);
            ack.Writer.Write(area);
            ack.Writer.Write(1); //Result
            ack.Writer.Write(localtime);
            ack.Writer.Write(Environment.TickCount);
            ack.Writer.Write(new byte[6]); // The rest of this is null
            packet.Sender.Send(ack);
        }

        [RicePacket(564, RiceServer.ServerType.Area, CheckedIn = true)]
        public static void ExitArea(RicePacket packet)
        {
            var area = packet.Reader.ReadInt32();
            RiceServer.GetArea(area).RemovePlayer(packet.Sender.Player);
        }

        [RicePacket(0x21D, RiceServer.ServerType.Area, CheckedIn = true)]
        public static void MoveVehicle(RicePacket packet)
        {
            packet.Reader.ReadUInt16(); // Skip serial, we'll use our local copy of it.
            byte[] movement = packet.Reader.ReadBytes(112);

            var buf = packet.Buffer;
            var mover = packet.Sender.Player.ActiveCharacter;
            // movimentos de HUV/objetos (numero do objeto em +4, ou outro serial) nao sao a posicao do jogador
            // o seu carro: o seu serial e (sem numero de objeto, ou com o tipo do seu carro: na perseguicao o jogo poe um)
            bool own = buf.Length >= 8 && (mover.Serial == 0 || BitConverter.ToUInt16(buf, 0) == mover.Serial)
                       && (BitConverter.ToUInt16(buf, 4) == 0
                           || (mover.Vehicle != null && BitConverter.ToUInt16(buf, 6) == (ushort) mover.Vehicle.CarType));
            if (own && buf.Length >= 0x20)
            {
                mover.LastX = BitConverter.ToSingle(buf, 0x18);
                mover.LastY = BitConverter.ToSingle(buf, 0x1c);
                mover.HasPosition = true;
            }
            Rice.Server.Packets.Game.CityBots.OnPlayerMove(mover, buf);
            if (own) Rice.Server.Packets.Game.NeoDurability.OnMove(mover, buf);    // dano tomado na Neo City: gasta a armadura
            if (mover.Area != null)
                mover.Area.BroadcastMovement(packet.Sender.Player, movement);
        }

        // 0x23D RequestCar (u16 n, n x (u16 serial, u16 tipo)): o cliente pede um carro que ve e nao conhece
        // -> 0x23E ResponseCar (XiCarMove) para cada bot.
        [RicePacket(0x23D, RiceServer.ServerType.Area, CheckedIn = true)]
        public static void RequestCar(RicePacket packet)
        {
            var buf = packet.Buffer;
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || buf.Length < 2) return;
            int n = BitConverter.ToUInt16(buf, 0);
            for (int i = 0; i < n && 2 + 4 * i + 2 <= buf.Length; i++)
            {
                uint serial = BitConverter.ToUInt16(buf, 2 + 4 * i);
                var mv = Rice.Server.Packets.Game.CityBots.CarMoveOf(ch, serial);
                if (mv == null) continue;
                var ack = new RicePacket(0x23E);
                ack.Writer.Write(mv);
                packet.Sender.Send(ack);
            }
        }

        // 0x220 JumpFreeTrafficVehicle / 0x223 MoveFreeTrafficVehicle (u16 id do carro do trafego primeiro): voce
        // (ou um HUV) bateu num carro do trafego e ele saiu da faixa. O servidor para de mandar esse carro por um tempo
        // (senao o jogo o puxa de volta para a faixa: carros sumindo e aparecendo) e o recoloca longe de voce.
        [RicePacket(0x220, RiceServer.ServerType.Area, CheckedIn = true)]
        public static void JumpFreeTraffic(RicePacket packet) { TrafficHit(packet); }

        [RicePacket(0x223, RiceServer.ServerType.Area, CheckedIn = true)]
        public static void MoveFreeTraffic(RicePacket packet) { TrafficHit(packet); }

        static void TrafficHit(RicePacket packet)
        {
            var buf = packet.Buffer;
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || buf.Length < 2) return;
            Rice.Game.CityTraffic.Hit(ch, BitConverter.ToUInt16(buf, 0));
        }

        [RicePacket(0x2AA, RiceServer.ServerType.Area)]
        public static void AreaStatus(RicePacket packet)
        {
            var ack = new RicePacket(0x2AB);

            // from ZoneServer
            for (int i = 0; i < 100; i++)
            {
                var area = RiceServer.GetArea(i);
                ack.Writer.Write(area != null ? area.GetPlayerCount() : 0);
            }
        }
    }
}
