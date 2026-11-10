using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server.Core;

namespace Rice.Server.Packets.Lobby
{
    public static class User
    {
        // Envia ao cliente a lista de corredores (slots) da conta.
        private static void SendCharacterList(RiceClient client)
        {
            var characters = client.Player.Characters;

            var ack = new RicePacket(61);

            ack.Writer.Write(0); // Permissions
            ack.Writer.Write(characters.Count); // Character Count

            ack.Writer.WriteUnicodeStatic(client.Player.User.Name, 18); // Username
            ack.Writer.Write((long)0);
            ack.Writer.Write((long)0);
            ack.Writer.Write((long)0);
            ack.Writer.Write(0);

            foreach (var character in characters)
            {
                // Carro atual do corredor (se nao houver, usa o padrao antigo).
                var car = character.Vehicle;
                int carId = car != null ? car.CarID : 1;
                int carType = car != null ? (int)car.CarType : 0x50;
                int carColor = car != null ? (int)car.Color : 0;

                ack.Writer.WriteUnicodeStatic(character.Name, 21); // Name
                ack.Writer.Write(character.CID); // ID
                ack.Writer.Write(character.Avatar); // Avatar
                ack.Writer.Write(character.Level); // Level
                ack.Writer.Write(carId); // Car ID
                ack.Writer.Write(carType); // Car type
                ack.Writer.Write(carColor); // Car color
                ack.Writer.Write(DateTime.Now - TimeSpan.FromDays(365)); // Creation Date
                // crew atual (crews.json): aparece na licenca da selecao de corredor
                var crew = Rice.Server.Packets.Game.Crew.Of((long) character.CID);
                ack.Writer.Write(crew != null ? crew.Tid : character.TID); // Crew ID
                ack.Writer.Write(crew != null ? crew.Mark : 0L); // Crew Image
                ack.Writer.WriteUnicodeStatic(crew != null ? crew.Name : "", 13); // Crew Name
                ack.Writer.Write((short)(crew == null ? 0 : crew.Master == (long) character.CID ? 1 : 3)); // Crew rank
                ack.Writer.Write((short)0); // Guild (0 = OMD, 1 = ROO)
            }

            client.Send(ack);
        }

        [RicePacket(60, RiceServer.ServerType.Lobby)]
        public static void UserInfo(RicePacket packet)
        {
            Log.WriteLine("UserInfo request.");

            var settings = new RicePacket(30);
            settings.Writer.Write(Static.BuildGameSettings());
            packet.Sender.Send(settings);

            Rice.Game.Character.EnsureUppedLoadout();
            Rice.Game.Character.LogDatabaseDump(packet.Sender.Player.User.UID);
            packet.Sender.Player.Characters = Rice.Game.Character.Retrieve(packet.Sender.Player.User.UID);

            SendCharacterList(packet.Sender);

            Log.WriteLine("Sent character list ({0} characters).", packet.Sender.Player.Characters.Count);
        }

        [RicePacket(80, RiceServer.ServerType.Lobby)]
        public static void CheckCharacterName(RicePacket packet)
        {
            string characterName = packet.Reader.ReadUnicode();

            var ack = new RicePacket(81);
            ack.Writer.WriteUnicodeStatic(characterName, 21);
            ack.Writer.Write(Rice.Game.Character.IsNameUsable(characterName) ? 1 : 0); // Availability. 1 = Available, 0 = Unavailable.
            packet.Sender.Send(ack);
        }

        [RicePacket(82, RiceServer.ServerType.Lobby)]
        public static void CreateCharacter(RicePacket packet)
        {
            string characterName = packet.Reader.ReadUnicodeStatic(21);
            ushort avatar = packet.Reader.ReadUInt16();

            // O cliente envia o carro/cor escolhidos; o servidor valida (so carro V1 vale).
            int carType = 0, color = 0;
            var stream = packet.Reader.BaseStream;
            if (stream.Length - stream.Position >= 8)
            {
                carType = packet.Reader.ReadInt32();
                color = packet.Reader.ReadInt32();
            }

            var player = packet.Sender.Player;
            Log.WriteLine("Criar corredor '{0}': avatar {1}, carro pedido {2}, cor {3:X}.", characterName, avatar, carType, color);

            try
            {
                if (player.Characters.Count >= RiceServer.Config.MaxPlayerProfiles)
                {
                    Log.WriteError("Limite de corredores atingido (" + RiceServer.Config.MaxPlayerProfiles + ").");
                    return;
                }

                if (!Rice.Game.Character.IsNameUsable(characterName))
                {
                    Log.WriteError("Nome indisponivel: " + characterName);
                    return;
                }

                if (!Rice.Game.Character.Create(player.User.UID, characterName, avatar, carType, color))
                {
                    packet.Sender.Error("Nao foi possivel criar o corredor. Veja o console do servidor.");
                    return;
                }

                Rice.Game.Character character = Rice.Game.Character.Retrieve(characterName);
                player.Characters.Add(character);
                try { Rice.Server.Packets.Game.BotSocial.AddStarterFriends((long) character.CID); }
                catch (Exception ex) { Log.WriteError("Amigos iniciais: " + ex.Message); }

                var ack = new RicePacket(83);
                ack.Writer.WriteUnicodeStatic(characterName, 21);
                ack.Writer.Write(character.CID); //High hopes there, NPLUTO
                ack.Writer.Write(character.CurrentCarID); //???
                packet.Sender.Send(ack);

                if (RiceServer.Config.RefreshListAfterCreate)
                    SendCharacterList(packet.Sender);

                Log.WriteLine("Corredor '{0}' criado e enviado ao cliente (CID {1}).", characterName, character.CID);
            }
            catch (Exception ex)
            {
                Log.WriteError("CreateCharacter falhou: " + ex);
                packet.Sender.Error("Erro ao criar o corredor. Veja o console do servidor.");

                // Evita deixar um corredor "fantasma" ocupando o nome.
                try
                {
                    var leftover = player.Characters.FirstOrDefault(c => c.Name == characterName);
                    if (leftover != null)
                        player.Characters.Remove(leftover);
                    Rice.Game.Character.Delete(characterName);
                }
                catch (Exception) { }
            }
        }

        [RicePacket(50, RiceServer.ServerType.Lobby)]
        public static void DeleteCharacter(RicePacket packet)
        {
            string characterName = packet.Reader.ReadUnicode();
            var player = packet.Sender.Player;

            var character = player.Characters.FirstOrDefault(c => c.Name == characterName);
            if (character == null)
            {
                Log.WriteError("Pedido para excluir corredor inexistente: " + characterName);
                return;
            }

            if (RiceServer.Config.ProtectUppedProfile &&
                string.Equals(characterName, RiceServer.Config.UppedProfileName, StringComparison.OrdinalIgnoreCase))
            {
                Log.WriteError("O corredor '" + characterName + "' esta protegido e nao pode ser excluido.");
                return;
            }

            if (Rice.Game.Character.Delete(characterName))
            {
                player.Characters.Remove(character);
                var ack = new RicePacket(84);
                ack.Writer.WriteUnicodeStatic(characterName, 21);
                packet.Sender.Send(ack);
            }
        }
    }
}
