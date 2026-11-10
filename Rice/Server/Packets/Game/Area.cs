using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Server.Core;
using Rice.Server.Structures;

namespace Rice.Server.Packets.Game
{
    public static class Area
    {
        [RicePacket(782, RiceServer.ServerType.Game)]
        public static void FirstPosition(RicePacket packet)
        {
            var ack = new RicePacket(0x30F);
            ack.Writer.Write(0L);
            ack.Writer.Write(0L);
            ack.Writer.Write(0L);
            ack.Writer.Write(3);
            packet.Sender.Send(ack);
        }

        [RicePacket(788, RiceServer.ServerType.Game)]
        public static void MyCityPosition(RicePacket packet)
        {
            Log.WriteLine("MyCityPosition: " + BitConverter.ToString(packet.Buffer));

            int channelId = packet.Reader.ReadInt32();

            var ack = new RicePacket(783);
            var charInfo = packet.Sender.Player.ActiveCharacter.GetInfo();

            // Corredor novo (ou posicao na origem 0,0, que e so o ponto de partida do jogo):
            // aparece na Station 1 (config StartPos*) com estado 0 = introducao.
            // Corredor que ja jogou: volta para a ultima posicao salva, sem repetir a introducao.
            var cfg = RiceServer.Config;
            bool hasSavedPosition = !(Math.Abs(charInfo.Position.X) < 1f && Math.Abs(charInfo.Position.Y) < 1f);

            int city = hasSavedPosition ? charInfo.City : cfg.StartCity;
            Vector4 spawn = hasSavedPosition
                ? charInfo.Position
                : new Vector4(cfg.StartPosX, cfg.StartPosY, cfg.StartPosZ);
            // Estado 0 = o jogo toca a introducao do Director Green (corredor novo). So para quem nunca jogou:
            // perfil upado, quem ja passou do nivel inicial ou ja tem missao concluida vai direto (estado 2 = na cidade).
            var me = packet.Sender.Player.ActiveCharacter;
            bool veteran = me.IsUpped || me.Level > cfg.StartLevel || hasSavedPosition
                           || Rice.Game.Quest.Retrieve(me.CID).Any(q => q.State >= 2);
            int posState = veteran ? 2 : 0;

            Log.WriteLine("Spawn de {0}: cidade {1}, pos ({2}, {3}, {4}), estado {5} ({6}).",
                packet.Sender.Player.ActiveCharacter.Name, city, spawn.X, spawn.Y, spawn.Z, posState,
                hasSavedPosition ? "volta para a ultima posicao" : "primeira vez: Station 1 / introducao");

            ack.Writer.Write(city);
            ack.Writer.Write(channelId);

            ack.Writer.Write(spawn); // Position
            ack.Writer.Write(posState); // PositionState, 0 for MoonPalace introduction

            ack.Writer.Write((byte)0);
            packet.Sender.Send(ack);
        }

        [RicePacket(162, RiceServer.ServerType.Game)]
        public static void SaveCarPos(RicePacket packet)
        {
            var channelId = packet.Reader.ReadInt32();
            var position = packet.Reader.ReadVector4();
            int cityId = packet.Reader.ReadInt32();
            int posState = packet.Reader.ReadInt32();

            Log.WriteLine("SaveCarPos: canal {0}, pos ({1}, {2}, {3}), rotacao {4}, cidade {5}, estado {6}",
                channelId, position.X, position.Y, position.Z, position.Rotation, cityId, posState);

            packet.Sender.Player.ActiveCharacter.SaveCarPos(channelId, position, cityId, posState);
            try { CityBots.NotePost(packet.Sender.Player.ActiveCharacter, cityId, position.X, position.Y, position.Z, position.Rotation); } catch (Exception) { }
        }

        [RicePacket(300, RiceServer.ServerType.Game)]
        public static void JoinArea(RicePacket packet)
        {
            var ack = new RicePacket(301);
            ack.Writer.Write(packet.Reader.ReadUInt32());
            ack.Writer.Write(1);
            packet.Sender.Send(ack);
        }

        [RicePacket(780, RiceServer.ServerType.Game)]
        public static void AreaList(RicePacket packet)
        {
            // client calls 2 functions (not using any packet data), returns  137 * (*(_DWORD *)(pktBuf + 2) - 1) + 143;
            var ack = new RicePacket(781);
            ack.Writer.Write(1);
            ack.Writer.Write(new byte[137]);
            packet.Sender.Send(ack);
        }

        [RicePacket(3200, RiceServer.ServerType.Game)]
        public static void CityLeave(RicePacket packet)
        {
            var ack = new RicePacket(3201);
            ack.Writer.Write(1); // if ( *(_DWORD *)(pktBuf + 2) == 1 )
            ack.Writer.Write(packet.Reader.ReadBytes(514)); // apparently these fuckers want their own 514 bytes back
            packet.Sender.Send(ack);
        }
    }
}
