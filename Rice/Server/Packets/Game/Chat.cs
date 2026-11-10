using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Game;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    public static class Chat
    {
        // Mensagem de sistema no chat do jogador (mesmo formato do chat normal: tipo, remetente, texto).
        public static void SendSystemMessage(RiceClient client, string message)
        {
            var ack = new RicePacket(147);
            ack.Writer.WriteUnicodeStatic("channel", 10);
            ack.Writer.WriteUnicodeStatic(Lang.T("Sistema"), 18);
            ack.Writer.WriteUnicode(Lang.T(message));
            client.Send(ack);
        }

        [RicePacket(146, RiceServer.ServerType.Game)]
        public static void ChatMessage(RicePacket packet)
        {
            string type = packet.Reader.ReadUnicodeStatic(10);
            bool green = packet.Reader.ReadUInt32() == 0xFF00FF00; // ignore this, use packet.Sender.Player.User.Status
            string message = packet.Reader.ReadUnicodePrefixed();

            string sender = packet.Sender.Player.ActiveCharacter.Name;

#if DEBUG
            if (message.ToLower().StartsWith("gibe_plos "))
            {
                string[] split = message.Split(' ');
                if (split.Length == 2)
                {
                    Item item;
                    if (packet.Sender.Player.ActiveCharacter.GrantItem(split[1], 1, out item))
                    {
                        packet.Sender.Error($"Gave 1x {item.itemEntry.Name}");
                        Log.WriteLine($"Gave {sender} 1x {item.itemEntry.Name}");

                        packet.Sender.Player.ActiveCharacter.FlushModInfo(packet.Sender);
                        return;
                    }

                    packet.Sender.Error($"Failed to give item");
                    return;
                }
            }
#endif

            Log.WriteLine("Chat recebido ({0}): {1}", type, message);

            // Comandos do modo offline, com "/" ou "!" na frente (nao vao para o chat dos outros).
            string trimmed = message.Trim();
            if (trimmed.StartsWith("/") || trimmed.StartsWith("!"))
            {
                string body = trimmed.Substring(1).Trim();
                string lower = body.ToLowerInvariant();
                var me = packet.Sender.Player.ActiveCharacter;

                if (lower == "tirar" || lower == "tirar todos" || lower == "kit")
                {
                    int removed = 0;
                    if (lower == "tirar todos")
                    {
                        foreach (var v in me.Garage.ToList())
                            removed += me.RemoveScorchedParts(v.CarID);
                    }
                    else
                    {
                        removed = me.RemoveScorchedParts(me.CurrentCarID);
                    }
                    Log.WriteLine("Comando {0}: {1} peca(s) Sorched removida(s) de {2}.", lower, removed, sender);
                    SendSystemMessage(packet.Sender, removed > 0
                        ? "Removi " + removed + " peca(s) Sorched. Abra o inventario de novo se nao atualizar."
                        : "Nenhuma peca Sorched equipada no carro atual. Use !tirar todos para todos os carros.");
                    me.FlushModInfo(packet.Sender);
                    return;
                }

                if (lower.StartsWith("carro"))
                {
                    string result = Dealership.BuyByCommand(me, body.Substring(5));
                    SendSystemMessage(packet.Sender, result);
                    me.FlushModInfo(packet.Sender);
                    return;
                }

                if (lower.TrimStart('!') == "durabilidade" || lower.TrimStart('!') == "durability")
                {
                    SendSystemMessage(packet.Sender, NeoDurability.Report(me));
                    me.FlushModInfo(packet.Sender);
                    return;
                }
                if (lower.TrimStart('!') == "escada")
                {
                    SendSystemMessage(packet.Sender, RiceServer.Config.CrewLadder ? CrewLadder.Status(me)
                        : "O mod Escada de crews esta desligado. Para ligar: Menu.bat > 8) Mods.");
                    return;
                }
                if (Bots.HandleCommand(packet.Sender, lower, body))
                    return;
                if (BotSocial.HandleCommand(packet.Sender, lower, body))
                    return;
                if (CarBox.HandleCommand(packet.Sender, lower, body))
                    return;
                if (VisualShop.HandleCommand(packet.Sender, lower, body))
                    return;
                if (Auction.HandleCommand(packet.Sender, lower, body))
                    return;
                if (UnderCity.HandleCommand(packet.Sender, lower, body))
                    return;
            }

            Console.WriteLine("({0}) <{1}> {2}", type, sender, message);
            try { BotChatter.OnPlayerChat(packet.Sender, type, message); }   // "bora", "sim", "quero"... respondem aos bots
            catch (Exception ex) { Log.WriteError("Resposta aos bots: " + ex.Message); }

            var ack = new RicePacket(147);
            ack.Writer.WriteUnicodeStatic(type, 10);
            ack.Writer.WriteUnicodeStatic(sender, 18);
            ack.Writer.WriteUnicode(message);

            switch (type)
            {
                case "room":
                    RiceServer.Game.Broadcast(ack); // TODO: broadcast only to users in same area
                    break;

                case "channel":
                    RiceServer.Game.Broadcast(ack);
                    break;

                case "party":
                    RiceServer.Game.Broadcast(ack); // TODO: broadcast only to users in same party
                    break;

                case "team":
                    RiceServer.Game.Broadcast(ack); // TODO: broadcast only to users in same crew
                    Bots.OnPlayerTeamChat(packet.Sender, message);
                    break;

                default:
                    Log.WriteError("Undefined chat message type.");
                    break;
            }
        }
    }
}
