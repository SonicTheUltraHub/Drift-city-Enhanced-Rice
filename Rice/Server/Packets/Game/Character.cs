using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    public static class Character
    {
        // A Atualizacao 84 subiu para o V9 carros que a loja mostra em outro V (lista antiga errada da loja).
        // Uma vez por corredor: o carro que esta exatamente nesse V errado volta ao V certo do carro.
        static readonly int[] WrongShopV = { 262,8, 21,9, 144,9, 149,9, 153,9, 168,9, 165,9, 191,9, 195,9, 198,9, 205,9, 232,9, 233,9, 248,9, 252,9, 255,9, 261,9, 258,9, 263,9, 270,9, 281,9, 273,9, 269,9, 291,9, 294,9, 298,9, 305,9, 308,9, 311,9, 314,9, 317,9, 327,9, 328,9, 337,9, 340,9, 348,9, 356,9, 359,9, 363,9, 370,9, 373,9, 374,9, 377,9, 385,9, 391,9, 397,9, 400,9, 404,9, 406,9, 417,9, 424,9, 425,9, 420,9, 435,9 };

        static void UndoWrongGrades(Rice.Game.Character ch)
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Paths.SaveFile("garage_v_fix.txt"));
            string mark = "|" + ch.CID + "|";
            try { if (System.IO.File.Exists(path) && System.IO.File.ReadAllText(path).Contains(mark)) return; } catch (Exception) { }
            for (int i = 0; i + 1 < WrongShopV.Length; i += 2)
            {
                int type = WrongShopV[i], bad = WrongShopV[i + 1];
                Rice.Server.Structures.Resources.VehicleListEntry le;
                if (!Rice.Server.Structures.Resources.VehicleTable.Vehicles.TryGetValue(type, out le) || le == null || le.Level >= bad) continue;
                foreach (var gv in ch.Garage.Where(v => (int) v.CarType == type && (int) v.Grade == bad).ToList())
                {
                    ch.SetCarGrade(gv.CarID, le.Level);
                    Log.WriteLine("Garagem: {0} V{1} -> V{2} (volta ao V certo da loja).", le.UiName, bad, le.Level);
                }
            }
            try { System.IO.File.AppendAllText(path, mark); } catch (Exception) { }
        }

        [RicePacket(123, RiceServer.ServerType.Game)]
        public static void LoadCharacter(RicePacket packet)
        {
            string characterName = packet.Reader.ReadUnicode();
            packet.Sender.Player.ActiveCharacter = null;

            foreach(var c in packet.Sender.Player.Characters)
            {
                if (c.Name == characterName)
                    packet.Sender.Player.ActiveCharacter = c;
            }

            if(packet.Sender.Player.ActiveCharacter == null)
            {
                Log.WriteLine("Rejecting {0} for invalid user-character combination.", packet.Sender.Player.User.Name);
                packet.Sender.Error("you_tried.avi");
                return;
            }

            var character = packet.Sender.Player.ActiveCharacter;

            if (!character.EnsureStarterCar())
            {
                packet.Sender.Error("Corredor sem carro e nao foi possivel criar um. Veja o console.");
                return;
            }

            character.EnsureMinimumLevel();
            character.RefuelAll();
            // docks antigos (bonus em %) ou sem pontos: sorteia os pontos da letra
            foreach (var dk in character.Inventory.Where(i => i.itemEntry != null && i.itemEntry.Category != null
                                                              && i.itemEntry.Category.StartsWith("dock_")).ToList())
                Rice.Game.PartCrafting.FixDock(character, dk);
            Rice.Game.Rewards.MigrateLevelDocks(character);
            Rice.Game.Rewards.UpdateDockRows(character);
            NeoDurability.Prepare(character);              // pecas da Neo City sem durabilidade: comecam cheias   // opcao das fileiras (Turbo Start / Drift Booster) ja certa ao entrar
            UndoWrongGrades(character);

            var ack = new RicePacket(124);

            var ackStruct = new Structures.LoadCharAck
            {
                ServerId = Rice.Server.Core.RiceServer.Config.LoadCharServerId,
                ServerStartTime = (uint) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds,
                CharInfo = character.GetInfo(),
                nCarSize = (uint)character.Garage.Count,
                CarInfo = character.Garage.Select(v => v.GetInfo()).ToList()
            };

            ack.Writer.Write(ackStruct);
            packet.Sender.Send(ack);
            Log.WriteLine("Sent LoadCharAck");

            var stat = new RicePacket(760);
            stat.Writer.Write(character.GetStatUpdate());
            packet.Sender.Send(stat);
            // corredor novo no tutorial (posicao 0 e nivel 1): sem licencas ainda; o resto (perfil upado, nivel 2+) recebe
            if (character.PosState != 0 || character.Level > 1 || !character.Garage.Any(v => v.IsTraining)) Licenses.SendLogin(packet.Sender, character);
            try { Alchemy.SendInfo(packet.Sender, character); } catch (Exception ex) { Log.WriteError("Producao (login): " + ex.Message); }
        }

        [RicePacket(400, RiceServer.ServerType.Game)]
        public static void ItemList(RicePacket packet)
        {
            byte[] testItem = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x69, 0x91, 0x00, 0x00, 0x00, 0x00, 0x80, 0xBF, 
    0x00, 0x00, 0x00, 0x00, 0xD9, 0x04, 0x00, 0x00, 0x50, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
            var ack = new RicePacket(401);
            ack.Writer.Write(262144); // ListUpdate (262144 = First packet from list queue, 262145 = sequential)
            ack.Writer.Write(1); // ItemNum
            ack.Writer.Write(testItem); // Null Item (96 bytes per XiStrMyItem)
            packet.Sender.Send(ack);
        }

        [RicePacket(801, RiceServer.ServerType.Game)]
        public static void PlayerInfoReq(RicePacket packet)
        {
            var reqCnt = packet.Reader.ReadUInt32();
            // Bots na rua: nome, nivel e crew em cima do carro (809 PlayerInfoRes, XiPlayerInfo 0xd8 cada).
            var buf = packet.Buffer;
            var botInfos = new List<byte[]>();
            for (int i = 0; i < Math.Min(reqCnt, 40u) && 4 + 4 * i + 2 <= buf.Length; i++)
            {
                var b = BotSocial.BySerial(BitConverter.ToUInt16(buf, (int) (4 + 4 * i)));
                if (b != null) botInfos.Add(CityBots.PlayerInfo(b));
            }
            if (botInfos.Count > 0)
            {
                var res = new RicePacket(809);
                res.Writer.Write((uint) botInfos.Count);
                foreach (var bi in botInfos) res.Writer.Write(bi);
                packet.Sender.Send(res);
                // licencas montadas dos bots (aparecem no carro)
                for (int i = 0; i < Math.Min(reqCnt, 40u) && 4 + 4 * i + 2 <= buf.Length; i++)
                {
                    var bb = BotSocial.BySerial(BitConverter.ToUInt16(buf, (int) (4 + 4 * i)));
                    if (bb != null) Licenses.SendBotMounted(packet.Sender, bb);
                }
                return;
            }
            var serial = packet.Reader.ReadUInt16(); // No known scenarios where the requested info count is > 1
            // Followed by the session age of the player we are requesting info for. nplutowhy.avi
 
            foreach(var p in RiceServer.GetPlayers())
            {
                if(p.ActiveCharacter != null && p.ActiveCharacter.Serial == serial)
                {
                    var character = p.ActiveCharacter;
                    var playerInfo = new Structures.PlayerInfo()
                    {
                        Name = character.Name,
                        Serial = character.Serial,
                        Age = 0
                    };
                    var res = new RicePacket(802);
                    res.Writer.Write(1);
                    res.Writer.Write(playerInfo);
                    packet.Sender.Send(res);
                    break;
                }
            }
        }
    }
}
