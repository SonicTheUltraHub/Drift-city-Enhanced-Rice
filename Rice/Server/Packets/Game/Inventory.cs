using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server.Core;
using Rice.Game;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    public static class Inventory
    {
        [RicePacket(400, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ItemList(RicePacket packet)
        {
            // unsigned long CurCarID (which car the item's currently equipped on, if any) (uncertain about CurCarID/LastCarID, check with Coaster later)
            // unsigned short State
            // unsigned short Slot
            // long StackNum
            // unsigned long LastCarID (which car the item was last equipped on, or the car ID associated with the car if the item's a key)
            // unsigned long AssistA
            // unsigned long AssistB
            // unsigned long AssistC
            // unsigned long AssistD
            // unsigned long AssistE
            // unsigned long AssistF
            // unsigned long AssistG
            // unsigned long AssistH
            // unsigned long AssistI
            // unsigned long AssistJ
            // unsigned long Box
            // unsigned long Belonging
            // long Upgrade
            // long UpgradePoint
            // unsigned long ExpireTick
            // float ItemHealth (only applicable to NEO parts, -1 everywhere else)
            // unsigned long unk2 (?, always 0)
            // unsigned long TableIdx
            // unsigned long InvenIdx
            // long Random (random seed, only applies to certain item types)
            var inventory = packet.Sender.Player.ActiveCharacter.Inventory;
            var ack = new RicePacket(401);
            ack.Writer.Write(262144); // ListUpdate (262144 = First packet from list queue, 262145 = sequential)
            ack.Writer.Write(inventory.Count); // ItemNum
            foreach (var item in inventory)
            {
                ack.Writer.Write(item.GetInfo());
            }
            packet.Sender.Send(ack);
        }

        // 428 CombiItem. Pedido: [0] int tipo, [4] [8] [12] = indices (invIdx) das 3 pecas,
        // [16] = indice da Seed Part (0 = nenhuma). A resposta do jogo (429) ainda e desconhecida:
        // por enquanto so atualizamos o inventario e avisamos no chat.
        [RicePacket(428, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void CombiItem(RicePacket packet)
        {
            var buf = packet.Buffer;
            var character = packet.Sender.Player.ActiveCharacter;
            Log.WriteLine("CombiItem ({0} bytes): {1}", buf.Length, BitConverter.ToString(buf));

            if (buf.Length < 20)
                return;

            int[] parts = { BitConverter.ToInt32(buf, 4), BitConverter.ToInt32(buf, 8), BitConverter.ToInt32(buf, 12) };
            int seedPart = BitConverter.ToInt32(buf, 16);

            if (seedPart != 0)
            {
                // Combine com Seed Part: o servidor sorteia o dock.
                Item dock;
                string dockMsg;
                if (!PartCrafting.CombineWithSeed(character, parts, seedPart, out dock, out dockMsg))
                {
                    Log.WriteError("CombiItem (seed) recusado: " + dockMsg);
                    Chat.SendSystemMessage(packet.Sender, "Nao foi possivel combinar: " + dockMsg + ".");
                    return;
                }
                var sack = new RicePacket(429);
                sack.Writer.Write(BitConverter.ToUInt32(buf, 0));
                sack.Writer.Write(dock != null ? dock.invIdx : 0u);
                packet.Sender.Send(sack);
                Chat.SendSystemMessage(packet.Sender, "Combinado com Seed! " + dockMsg);
                character.FlushModInfo(packet.Sender);
                return;
            }

            Item result;
            string reason;
            if (!character.CombineParts(parts, out result, out reason))
            {
                Log.WriteError("CombiItem recusado: " + reason);
                Chat.SendSystemMessage(packet.Sender, "Nao foi possivel combinar: " + reason + ".");
                return;
            }

            Log.WriteLine("CombiItem: 3 pecas combinadas em {0} ({1}).", result.itemEntry.Name, result.ID);
            // 429 CombiItemAck (u32 tipo, u32 InvenIdx da peca nova): o jogo mostra o resultado
            var cack = new RicePacket(429);
            cack.Writer.Write(BitConverter.ToUInt32(buf, 0));
            cack.Writer.Write(result.invIdx);
            packet.Sender.Send(cack);
            Chat.SendSystemMessage(packet.Sender, "Combinado: 3 pecas viraram " + result.itemEntry.Name + ".");
            character.FlushModInfo(packet.Sender);
        }

        // 426 ModifyItem: ainda nao conhecemos o formato. Registramos os bytes e, se os numeros do pacote apontarem
        // para uma peca + Converting Solution (+ Enhancement Formula) do inventario, aplicamos o Convert.
        [RicePacket(426, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ModifyItem(RicePacket packet)
        {
            var buf = packet.Buffer;
            var character = packet.Sender.Player.ActiveCharacter;
            Log.WriteLine("ModifyItem ({0} bytes): {1}", buf.Length, BitConverter.ToString(buf));

            string msg;
            bool done = PartCrafting.TryConvertFromPacket(character, buf, out msg);
            // 427 ModifyItemAck (2016, 52 bytes): u32 peca, as 10 opcoes (assist) da peca, u32 modo (2 = convert normal),
            // u32 0. O eco de 12 bytes que ia antes fazia o jogo ler alem do pacote e fechar (crash).
            // Vai ANTES da lista de itens.
            uint partIdx = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0;
            var part = character.Inventory.FirstOrDefault(i => i.invIdx == partIdx);
            if (part != null)
            {
                var ack = new RicePacket(427);
                ack.Writer.Write(partIdx);
                ack.Writer.Write(part.assistA); ack.Writer.Write(part.assistB); ack.Writer.Write(part.assistC);
                ack.Writer.Write(part.assistD); ack.Writer.Write(part.assistE); ack.Writer.Write(part.assistF);
                ack.Writer.Write(part.assistG); ack.Writer.Write(part.assistH); ack.Writer.Write(part.assistI);
                ack.Writer.Write(part.assistJ);
                ack.Writer.Write(2u);
                ack.Writer.Write(0u);
                packet.Sender.Send(ack);
            }
            character.FlushModInfo(packet.Sender);
            if (done) Chat.SendSystemMessage(packet.Sender, msg ?? "Convert aplicado.");
            else if (part == null) Chat.SendSystemMessage(packet.Sender, "Convert: peca nao encontrada.");
        }

        // Confirmacao: eco do pedido (palpite, igual ao que Equip/UnEquip/Drop fazem). Desligue em PartShopSendAcks se der problema.
        private static void SendEchoAck(RicePacket packet, int ackId)
        {
            if (!RiceServer.Config.PartShopSendAcks)
                return;
            var ack = new RicePacket((ushort) ackId);
            ack.Writer.Write(packet.Buffer);
            packet.Sender.Send(ack);
        }

        // 417 ItemUse: [0] invIdx do item (palpite). Por enquanto so o Booster Protector (turbosave).
        [RicePacket(417, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ItemUse(RicePacket packet)
        {
            var buf = packet.Buffer;
            var character = packet.Sender.Player.ActiveCharacter;
            Log.WriteLine("ItemUse ({0} bytes): {1}", buf.Length, BitConverter.ToString(buf));
            if (buf.Length < 4)
                return;

            int invIdx = BitConverter.ToInt32(buf, 0);
            var item = character.Inventory.FirstOrDefault(i => i.invIdx == (uint) invIdx);
            if (item == null)
            {
                Chat.SendSystemMessage(packet.Sender, "Uso de item: item nao encontrado (veja o console).");
                return;
            }

            // chave de carro (duplo clique): vira caixa de carro (para trocar com bot)
            if (item.itemEntry.Category == "car")
            {
                var car = character.Garage.FirstOrDefault(v => v.CarID == item.curCarID);
                if (car != null) { CarBox.Pack(packet.Sender, character, car); return; }
            }
            if (VisualShop.OpenBox(packet.Sender, character, item, invIdx)) return;
            if (item.itemEntry.Category == "giftbox_car" && Gotcha.OpenCarBox(packet.Sender, character, item, invIdx))
                return;
            if (item.itemEntry.Category == "MitoCarUpgrade")
            {
                CarUpgrade(packet.Sender, character, item, invIdx);
                return;
            }
            // Production Skill Book: aprende a receita (aparece acesa na janela de producao)
            if (Alchemy.UseBook(packet.Sender, character, item, invIdx)) return;
            // Component (Speed/Accel./Dura./Booster): com 10 iguais vira a peca H incompleta (Crude ... Part)
            if (item.itemEntry.Category == "piece")
            {
                long have = character.Inventory.Where(i => i.ID == item.ID && i.state != 1).Sum(i => (long) Math.Max(1u, i.stackNum));
                string made = have >= 10 ? Rice.Game.Rewards.CombinePieces(character, item.ID) : null;
                if (made != null)
                {
                    character.FlushModInfo(packet.Sender);
                    Chat.SendSystemMessage(packet.Sender, "10 " + item.itemEntry.Name + " viraram 1 " + made + "! Complete com Mittron Enhancer no Modify do Part Shop.");
                }
                else Chat.SendSystemMessage(packet.Sender, item.itemEntry.Name + ": voce tem " + have + "/10. Junte 10 para virar uma peca H incompleta.");
                return;
            }
            // Repair Kits da Neo City (durabilidade das pecas da Neo)
            if (NeoDurability.UseKit(packet.Sender, character, item, invIdx)) return;
            int useKind;
            if (UseKinds.TryGetValue(item.itemEntry.Category ?? "", out useKind))
            {
                UseSimple(packet.Sender, character, item, invIdx, useKind);
                return;
            }
            else
            {
                Chat.SendSystemMessage(packet.Sender, "Uso de '" + item.itemEntry.Category + "' ainda nao e suportado.");
            }
        }

        // Itens de uso direto: 418 ItemUseAck = InvenIdx, qtd, tipo (codigo do cliente 2016), valor, tempo (s), 0.
        // O cliente aplica o efeito com o valor e o tempo (antes ia tempo 0: o Booster Protector nao protegia nada).
        // Codigos do openDC (items.py USE_KINDS).
        static readonly Dictionary<string, int> UseKinds = new Dictionary<string, int>
        {
            {"fuel", 3}, {"fuelsave", 5}, {"turbosave", 6}, {"damage", 7},
            {"accelpointup", 8}, {"crashpointup", 9}, {"speedpointup", 0xa},
            {"repair", 0x19}, {"full_repair", 0x26}, {"new_repair", 0x4f}, {"Ultra_repair", 0x39},
            {"buff_item", 0x27}, {"cartrans", 0x28}, {"auto_drive", 0x29}, {"bacchus_f", 0x4c}, {"bacchus_d", 0x4d},
            {"shield", 0x52}, {"booster_chage", 0x5f}, {"minimaprader", 0x5e}, {"healthupnow", 0x58},
            {"party_healthupnow", 0x59}, {"fullchance_upgrade", 0x69},
        };

        static readonly HashSet<System.Threading.Timer> BuffTimers = new HashSet<System.Threading.Timer>();

        static uint UseNum(string s)
        {
            double v;
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0
                ? (uint) Math.Round(v) : 0u;
        }

        static void UseSimple(RiceClient c, Rice.Game.Character ch, Rice.Game.Item item, int invIdx, int kind)
        {
            var ue = item.itemEntry as UseItemTableEntry;
            uint value = ue != null ? UseNum(ue.StatModifier) : 0u;
            uint time = ue != null ? UseNum(ue.Duration) : 0u;
            string cat = item.itemEntry.Category;
            string extra = "";
            if (cat == "fuel" && ch.Vehicle != null)
            {
                // Mittron Fuel: valor em mL (5L = 5000)
                var car = ch.Vehicle;
                float add = value / 1000f;
                ch.UpdateFuel(car.CarID, car.Mitron + add);
                extra = string.Format(" (+{0:0} L, tanque {1:0.0} L)", add, car.Mitron);
            }
            else if (cat == "accelpointup" || cat == "crashpointup" || cat == "speedpointup")
            {
                int k = cat == "speedpointup" ? 0 : cat == "accelpointup" ? 1 : 2;
                ch.AddStatBuff(k, (int) value, time);
                extra = string.Format(" (+{0} por {1} s)", value, time);
                // quando acabar, manda os atributos de novo
                var cc = c; var chh = ch;
                System.Threading.Timer tm = null;
                tm = new System.Threading.Timer(_ =>
                {
                    try { var st = new RicePacket(760); st.Writer.Write(chh.GetStatUpdate()); cc.Send(st); } catch (Exception) { }
                    lock (BuffTimers) BuffTimers.Remove(tm);
                }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                lock (BuffTimers) BuffTimers.Add(tm);
                tm.Change((int) Math.Min(int.MaxValue, (time + 1) * 1000L), System.Threading.Timeout.Infinite);
                var now = new RicePacket(760); now.Writer.Write(ch.GetStatUpdate()); c.Send(now);
            }
            if (cat == "healthupnow")
                try { NeoCity.PartyHeal(c, ch, value, ch.Name); } catch (Exception) { }   // Neo City: o seu kit cura a party
            Rice.Game.Item used;
            ch.DropItem(invIdx, out used, 1, true);
            var uack = new RicePacket(418);
            uack.Writer.Write((uint) invIdx); uack.Writer.Write(1u); uack.Writer.Write((uint) kind);
            uack.Writer.Write(value); uack.Writer.Write(time); uack.Writer.Write(0u);
            c.Send(uack);
            ch.FlushModInfo(c);
            Chat.SendSystemMessage(c, "Usou " + item.itemEntry.Name + extra + ".");
            Log.WriteLine("ItemUse: {0} usou {1} ({2}, tipo {3}, valor {4}, tempo {5} s).", ch.Name, item.ID, cat, kind, value, time);
        }

        // V2..V9 Car Upgrade (i_d_mu_vN_*): o carro que voce esta usando sobe de V(N-1) para VN, se o modelo tiver esse V
        // e voce tiver o nivel do V (CarTierLevels). O item e gasto so quando da certo.
        static void CarUpgrade(RiceClient c, Rice.Game.Character ch, Rice.Game.Item item, int invIdx)
        {
            var m = System.Text.RegularExpressions.Regex.Match(item.ID ?? "", @"_v(\d)_");
            int target = m.Success ? int.Parse(m.Groups[1].Value) : 0;
            var car = ch.Vehicle;
            if (target < 2 || target > 9 || car == null) { Chat.SendSystemMessage(c, "Upgrade de carro: escolha um carro primeiro."); return; }
            if (car.IsTraining) { Chat.SendSystemMessage(c, "O carro de treino nao pode ser upado."); return; }
            if (car.Grade >= target) { Chat.SendSystemMessage(c, "Seu carro ja e V" + car.Grade + "."); return; }
            if (car.Grade != target - 1) { Chat.SendSystemMessage(c, item.itemEntry.Name + " e para carro V" + (target - 1) + " (o seu e V" + car.Grade + ")."); return; }
            var tiers = RiceServer.Config.CarTierLevels;
            int need = tiers != null && tiers.Length >= target ? tiers[target - 1] : 0;
            if (ch.Level < need) { Chat.SendSystemMessage(c, "V" + target + " pede nivel " + need + "."); return; }
            try { VehicleTable.GetVehicleUpgrade((int) car.CarType, target); }
            catch (Exception) { Chat.SendSystemMessage(c, "Esse modelo nao tem V" + target + "."); return; }
            var nv = ch.SetCarGrade(car.CarID, target);
            if (nv == null) { Chat.SendSystemMessage(c, "Upgrade de carro: falhou (veja o console)."); return; }
            Rice.Game.Item used;
            ch.DropItem(invIdx, out used, 1, true);
            var ack = new RicePacket(90);
            ack.Writer.Write(nv.GetInfo());
            c.Send(ack);
            var stat = new RicePacket(760);
            stat.Writer.Write(ch.GetStatUpdate());
            c.Send(stat);
            ch.FlushModInfo(c);
            Chat.SendSystemMessage(c, "Seu carro subiu para V" + target + "! Saia e entre na garagem (ou troque de carro) para ver tudo atualizado.");
            Log.WriteLine("Upgrade de carro: {0} carro {1} (tipo {2}) V{3} -> V{4}.", ch.Name, car.CarID, car.CarType, target - 1, target);
            try { Licenses.Count(ch, "GC_Carupgrade"); } catch (Exception lex) { Log.WriteError("Licencas (upgrade): " + lex.Message); }
        }

        // 413 UpgradeItem / 430 UpgradeItemDouble = Modify. Formato ainda nao confirmado: grava o pacote e procura peca + Enhancer.
        // Sempre responde com a lista de itens (402) para o Part Shop nao travar esperando resposta.
        [RicePacket(413, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void UpgradeItem(RicePacket packet)
        {
            HandleModify(packet, "UpgradeItem");
        }

        [RicePacket(430, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void UpgradeItemDouble(RicePacket packet)
        {
            HandleModify(packet, "UpgradeItemDouble");
        }

        private static void HandleModify(RicePacket packet, string name)
        {
            var buf = packet.Buffer;
            var character = packet.Sender.Player.ActiveCharacter;
            Log.WriteLine("{0} ({1} bytes): {2}", name, buf.Length, BitConverter.ToString(buf));

            string msg;
            int fee, result;
            bool ok = PartCrafting.TryModifyFromPacket(character, buf, out msg, out fee, out result);

            // Resposta (formato do cliente 2016): u32 taxa, u32 resultado, eco do pedido (peca, enhancer, quantidade
            // = 12 bytes; o Double manda 16), + u32 0. A resposta vai ANTES da lista de itens, senao a loja trava.
            bool dbl = name != "UpgradeItem";
            var ack = new RicePacket((ushort) (dbl ? 431 : 414));
            ack.Writer.Write(fee);
            ack.Writer.Write(result);
            if (dbl) ack.Writer.Write(result);
            var echo = new byte[dbl ? 16 : 12];
            Array.Copy(buf, echo, Math.Min(buf.Length, echo.Length));
            ack.Writer.Write(echo);
            ack.Writer.Write(0);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
            Chat.SendSystemMessage(packet.Sender, ok ? msg : "Modify: nao entendi o pedido (veja o console).");
        }

        [RicePacket(403, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void DropItem(RicePacket packet)
        {
            var tableIdx = packet.Reader.ReadInt32();
            var invIdx = packet.Reader.ReadInt32();
            var count = packet.Reader.ReadInt32();

            var character = packet.Sender.Player.ActiveCharacter;

            var toDrop = character.Inventory.FirstOrDefault(i => i.invIdx == invIdx && i.tblIdx == tableIdx);
            if (toDrop == null || count < 1)
            {
                packet.Sender.Error("What item?");
                Log.WriteError("What item?");
                return;
            }

            Log.WriteDebug($"{tableIdx} {invIdx} {count} item {toDrop?.ID ?? "NONE"}");
            bool dropped = character.DropItem((int) toDrop.invIdx, out toDrop, (uint) count);
            if (!dropped)
            {
                packet.Sender.Error("Failed to drop item.");
                Log.WriteError("Failed to drop item.");
                return;
            }

            //DropItemAck
            var ack = new RicePacket(404);
            ack.Writer.Write(tableIdx);
            ack.Writer.Write(invIdx);
            ack.Writer.Write(count);
            packet.Sender.Send(ack);

            Log.WriteDebug($"todrop stack {toDrop.stackNum}");

            character.FlushModInfo(packet.Sender);
        }

        [RicePacket(409, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void EquipItem(RicePacket packet)
        {
            var invIdx = packet.Reader.ReadUInt32();
            var destSlotIdx = packet.Reader.ReadUInt32();
            var carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;

            Log.WriteDebug($"equip item {invIdx} slot {destSlotIdx} car {carId}");

            bool equipped = character.EquipItem((int)invIdx, (short)destSlotIdx, (int)carId);
            if (!equipped)
            {
                packet.Sender.Error("could not equip item");
                Log.WriteError($"Could not equip item {invIdx}");
                return;
            }

            var ack = new RicePacket(410);
            ack.Writer.Write(invIdx);
            ack.Writer.Write(destSlotIdx);
            ack.Writer.Write(carId);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }

        [RicePacket(411, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void UnEquipItem(RicePacket packet)
        {
            var invIdx = packet.Reader.ReadUInt32();
            var carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;

            Log.WriteDebug($"unequip item {invIdx} car {carId}");

            bool unEquipped = character.UnEquipItem((int)invIdx, (int)carId);
            if (!unEquipped)
            {
                packet.Sender.Error("could not unequip item");
                Log.WriteError($"Could not unequip item {invIdx}");
                return;
            }

            var ack = new RicePacket(412);
            ack.Writer.Write(invIdx);
            ack.Writer.Write(carId);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }
    }
}