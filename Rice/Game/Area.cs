using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Server.Core;

namespace Rice.Game
{
    public class Area
    {
        public int ID;
        private List<Player> players;

        public Area(int id)
        {
            ID = id;
            players = new List<Player>();
        }

        public void AddPlayer(Player player)
        {
            if (players.Contains(player))
            {
                Log.WriteLine("Weird: Player already in area.");
                RemovePlayer(player);
            }
            players.Add(player);
            var ach = player.ActiveCharacter;
            // base ROAD (lobby da Neo City): o jogo entra numa area de cidade (1) com a cidade 6 (saindo de um capitulo)
            // ou 0 (entrando no jogo). Guardado na entrada: o trafego mudava a cidade para Moon Palace pela posicao.
            bool wasNeo = Rice.Server.Packets.Game.NeoCity.IsNeoArea(ach.LastAreaId);
            ach.InNeoBase = ID >= 1 && ID < 84 && (ach.City == 6 || ach.City == 0 || wasNeo);
            if (ach.InNeoBase && ach.City != 6) ach.City = 6;
            ach.Area = this;
            Log.WriteLine("Added player {0} to area {1}{2}.", ach.Name, ID, ach.InNeoBase ? " (base ROAD da Neo City)" : "");
            player.ActiveCharacter.ShopBuys = 0;   // saiu da loja: zera a conta de compras seguidas
            Rice.Server.Packets.Game.BotSocial.OnAreaJoin(player);
        }

        public void RemovePlayer(Player player)
        {
            players.Remove(player);
            try { Rice.Server.Packets.Game.Trade.Abort(player.ActiveCharacter); } catch (Exception) { }
            try { Rice.Server.Packets.Game.CityBots.NoteLeave(player.ActiveCharacter); } catch (Exception) { }
            player.ActiveCharacter.LastAreaId = ID;
            player.ActiveCharacter.Area = null;
            Log.WriteLine("Removed player {0} from area {1}.", player.ActiveCharacter.Name, ID);
        }

        public Player[] GetPlayers()
        {
            return players.ToArray();
        }

        public int GetPlayerCount()
        {
            return players.Count;
        }

        public void BroadcastMovement(Player player, byte[] movement) // byte[] temporarily
        {
            var move = new RicePacket(0x21D); // 114 total length
            move.Writer.Write(player.ActiveCharacter.Serial);
            move.Writer.Write(movement);

            foreach (Player areaPlayer in players)
            {
                if (areaPlayer != player)
                    areaPlayer.AreaClient.Send(move);
            }
        }

        public void Broadcast(RicePacket packet, RiceClient exclude = null)
        {
            foreach (var player in GetPlayers())
            {
                var client = player.AreaClient;

                if (exclude == null || client != exclude)
                    client.Send(packet);
            }
        }
    }
}
