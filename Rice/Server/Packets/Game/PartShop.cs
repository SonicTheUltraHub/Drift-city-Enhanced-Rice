using Rice.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Game;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    public static class PartShop
    {
        [RicePacket(405, RiceServer.ServerType.Game)]
        public static void BuyItem(RicePacket packet)
        {
            int itemID = packet.Reader.ReadInt32();
            uint count = packet.Reader.ReadUInt32();
            uint shopID = packet.Reader.ReadUInt32(); // possibly (possibly fucking not theres no way that's 4 billion)

            Log.WriteDebug($"id {itemID} count {count} shop {shopID}");

            if (itemID < 0 || itemID >= ItemTable.Items.Count)
            {
                packet.Sender.Error("that id can't be right");
                return;
            }

            var item = ItemTable.Items[itemID];
            int itemBuyPrice;
            bool gotPrice = int.TryParse(item.BuyValue, out itemBuyPrice);
            if (item.BuyValue == "n/a" || !gotPrice)
            {
                packet.Sender.Error("price pls");
                return;
            }

            Log.WriteDebug($"item {item.Name} {itemBuyPrice}");

            var character = packet.Sender.Player.ActiveCharacter;

            bool tookMoney = character.SpendMito((ulong) itemBuyPrice * count);
            if (!tookMoney)
            {
                Log.WriteError($"BuyItem: mito insuficiente para {item.Name} (precisa de {itemBuyPrice * count}, tem {character.Mito})");
                return;
            }

            Item resultItem;

            bool gaveItem = character.GrantItem(item.ID, count, out resultItem);
            if (!gaveItem)
            {
                Log.WriteError($"BuyItem: nao foi possivel entregar {item.ID}");
                return;
            }

            string qtext;
            if (Rice.Game.PartCrafting.RollQuality(character, resultItem, out qtext) >= 0 && qtext != null) Chat.SendSystemMessage(packet.Sender, qtext);

            var ack = new RicePacket(406);
            ack.Writer.Write(itemID);
            ack.Writer.Write(count);
            ack.Writer.Write((uint) (itemBuyPrice * count));
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }

        [RicePacket(407, RiceServer.ServerType.Game)]
        public static void SellItem(RicePacket packet)
        {
            uint itemID = packet.Reader.ReadUInt32();
            uint count = packet.Reader.ReadUInt32();
            uint invenID = packet.Reader.ReadUInt32();
            
            if (itemID > ItemTable.Items.Count)
            {
                packet.Sender.Error("that id can't be right");
                return;
            }

            var item = ItemTable.Items[(int)itemID];
            int itemSellPrice;
            bool gotPrice = int.TryParse(item.SellValue, out itemSellPrice);
            if (item.SellValue == "n/a" || !gotPrice)
            {
                packet.Sender.Error("price pls");
                return;
            }

            var character = packet.Sender.Player.ActiveCharacter;

            // venda: preco da tabela + SellBonus por unidade; pecas/docks com qualidade (pontos sorteados) e upadas valem mais
            var cfgS = RiceServer.Config;
            var before = character.Inventory.FirstOrDefault(i => i.invIdx == invenID);
            long unit = itemSellPrice + Math.Max(0, cfgS.SellBonus);
            // preco fixo de venda (Mittron Enhancer S/H, Seed Parts...): esse e o valor final
            // pecas de desempenho (Speed/Accel/Crash/Boost): preco normal do jogo, sem bonus
            string pcat = item.Category ?? "";
            if (pcat == "speed" || pcat == "accel" || pcat == "crash" || pcat == "boost")
                unit = itemSellPrice;
            int fixedPrice;
            if (cfgS.SellPriceOverride != null && item.ID != null && cfgS.SellPriceOverride.TryGetValue(item.ID, out fixedPrice))
                unit = fixedPrice;
            if (before != null)
            {
                unit += (long) Math.Max(0, before.upgradePoint) * Math.Max(0, cfgS.SellQualityMito);
                unit += (long) Math.Max(0, before.upgrade) * Math.Max(0, cfgS.SellUpgradeMito);
            }

            Item toSell;
            bool tookItem = character.DropItem((int)invenID, out toSell, count);
            if (!tookItem)
                return;

            long mitoToGive = unit * count;
            bool gaveMoney = character.GrantMito((ulong)mitoToGive, false);
            if (!gaveMoney)
            {
                packet.Sender.Error("Well that's embarrassing. Took your item but no mito for you, sorry");
                return;
            }

            var ack = new RicePacket(408);
            ack.Writer.Write(itemID);
            ack.Writer.Write(count);
            ack.Writer.Write((uint)mitoToGive);
            ack.Writer.Write(invenID);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }
    }
}
