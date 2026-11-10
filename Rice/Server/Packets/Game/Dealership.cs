using Rice.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Game;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    public static class Dealership
    {
        // Drift Shop > Tuning: subir o V do carro.
        //  91 UpgradeCar_Thread (DriftCity.exe 0x846510, 0x13 bytes): u32 carro, u32 (valor do jogador), u32, u8, u32
        //  92 UpgradeCarAck (0x523f30): u32 carro, u32 V novo, u32 CC gasto, u32 mito gasto, u32 (0) - o jogo troca o
        //     V do carro, refaz os atributos e desconta o CC/mito
        // Preco: o "upgrademito" do V novo (tabela VehicleUpgrade); nivel minimo do V igual ao da concessionaria.
        [RicePacket(91, RiceServer.ServerType.Game)]
        public static void UpgradeCar(RicePacket packet)
        {
            var b = packet.Buffer ?? new byte[0];
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || b.Length < 4) return;
            int carId = (int) BitConverter.ToUInt32(b, 0);
            var car = ch.Garage.FirstOrDefault(v => v.CarID == carId);
            Log.WriteLine("Tuning: {0} quer subir o V do carro {1} ({2}).", ch.Name, carId, BitConverter.ToString(b));
            Action<uint, uint> reply = (grade, mito) =>
            {
                var ack = new RicePacket(92);
                ack.Writer.Write((uint) carId);
                ack.Writer.Write(grade);
                ack.Writer.Write(0u);       // CC
                ack.Writer.Write(mito);
                ack.Writer.Write(0u);
                packet.Sender.Send(ack);
            };
            if (car == null) { Chat.SendSystemMessage(packet.Sender, "Carro nao encontrado na garagem."); return; }
            int next = (int) car.Grade + 1;
            VehicleUpgradeEntry up = null;
            try
            {
                if (VehicleTable.VehicleUpgrades.ContainsKey((int) car.CarType) && next <= VehicleTable.VehicleUpgrades[(int) car.CarType].Count)
                    up = VehicleTable.GetVehicleUpgrade((int) car.CarType, next);
            }
            catch (Exception) { up = null; }
            if (up == null)
            {
                Chat.SendSystemMessage(packet.Sender, "Esse carro ja esta no V maximo.");
                reply(car.Grade, 0); return;
            }
            int need = Rice.Game.Character.GetRequiredLevelForTier(next);
            if (ch.Level < need)
            {
                Chat.SendSystemMessage(packet.Sender, "Precisa do nivel " + need + " para o V" + next + ".");
                reply(car.Grade, 0); return;
            }
            uint cost = (uint) Math.Max(0, up.UpgradeCost);
            if (cost > 0 && !ch.SpendMito(cost))
            {
                Chat.SendSystemMessage(packet.Sender, "Mito insuficiente: o V" + next + " custa " + cost.ToString("N0") + " mito.");
                reply(car.Grade, 0); return;
            }
            var nv = ch.SetCarGrade(carId, next);
            if (nv == null) { reply(car.Grade, 0); return; }
            reply((uint) next, cost);
            Log.WriteLine("Tuning: {0} subiu o carro {1} para {2} (V{3}) por {4} mito.", ch.Name, carId, up.Name, next, cost);
            Chat.SendSystemMessage(packet.Sender, "Carro agora e " + up.Name + " (-" + cost.ToString("N0") + " mito).");
        }

        [RicePacket(85, RiceServer.ServerType.Game)]
        public static void BuyCar(RicePacket packet)
        {
            Log.WriteDebug($"buycar {BitConverter.ToString(packet.Buffer)}");
            string charName = packet.Reader.ReadUnicodeStatic(21); // what the fug npludo
            uint carId = packet.Reader.ReadUInt32();
            uint color = packet.Reader.ReadUInt32();

            Log.WriteDebug($"char {charName} buying car {carId} color {color}");
            if (!VehicleTable.Vehicles.ContainsKey((int) carId))
            {
                packet.Sender.Error("fuckity fuck off");
                Log.WriteError("Attempted to purchase vehicle not in table");
                return;
            }

            var vehEntry = VehicleTable.Vehicles[(int) carId];
            // carro de cupom (Panda, Tokio...): precisa do item do cupom, que e gasto na compra
            string couponId = RiceServer.Config.CouponsEnabled ? Coupons.CouponItemFor((int) carId) : null;
            Item coupon = null;
            if (couponId != null)
            {
                var me = packet.Sender.Player.ActiveCharacter;
                coupon = me.Inventory.FirstOrDefault(i => i.ID == couponId && i.state == 0 && i.stackNum > 0);
                if (coupon == null && !RiceServer.Config.DealershipUnlockAll)
                {
                    Log.WriteLine("Carro de cupom {0}: {1} sem o cupom {2}.", carId, me.Name, couponId);
                    Chat.SendSystemMessage(packet.Sender, "Esse carro precisa do cupom dele (Coupon Book). Complete a meta e pegue o cupom.");
                    return;
                }
            }
            if (vehEntry.HasCondition() && !RiceServer.Config.DealershipUnlockAll && coupon == null)
            {
                Log.WriteError("Attempted to purchase car that has not-null sell condition, not implemented");
                return;
            }

            // o carro vem no V que a loja mostra (os carros novos ficam no V9 da loja); sem V na loja, o V da tabela
            int grade = Bots.ShopGrade((int) carId);
            if (grade < 1 || grade > VehicleTable.VehicleUpgrades[(int) carId].Count) grade = vehEntry.Level;

            // Carro so pode ser comprado a partir do nivel do tier (V1..V9).
            var buyer = packet.Sender.Player.ActiveCharacter;
            int requiredLevel = Rice.Game.Character.GetRequiredLevelForTier(grade);
            if (vehEntry.IsSpecialCategory())
                requiredLevel = Math.Max(requiredLevel, RiceServer.Config.SpecialCarsMinLevel);
            if (buyer.Level < requiredLevel)
            {
                Log.WriteError($"Level {buyer.Level} too low for car {carId} (tier V{vehEntry.Level} needs level {requiredLevel})");
                Chat.SendSystemMessage(packet.Sender, "Nivel insuficiente: este carro exige nivel " + requiredLevel + ".");
                return;
            }

            // muitas compras seguidas na mesma visita: o jogo fica sem espaco para modelos ("don't have Model") e fecha
            int maxBuys = RiceServer.Config.ShopMaxBuysPerVisit;
            if (maxBuys > 0 && buyer.ShopBuys >= maxBuys)
            {
                Log.WriteLine("Loja: {0} ja comprou {1} carros nesta visita; pedindo para sair e voltar.", buyer.Name, buyer.ShopBuys);
                Chat.SendSystemMessage(packet.Sender, "Voce ja comprou " + buyer.ShopBuys + " carros nesta visita. Saia da loja e entre de novo para comprar mais (o jogo trava com muitos seguidos).");
                return;
            }

            var vehUpgradeEntry = VehicleTable.VehicleUpgrades[(int) carId][grade - 1];
            int price = vehUpgradeEntry.Price;

            var character = packet.Sender.Player.ActiveCharacter;

            bool tookMoney = character.SpendMito((ulong) price);
            if (!tookMoney)
            {
                Log.WriteError($"Failed to spend mito for vehicle {carId}");
                return;
            }
            if (coupon != null)
            {
                Item usedCoupon;
                character.DropItem((int) coupon.invIdx, out usedCoupon, 1, true);
                Log.WriteLine("Carro de cupom {0}: cupom {1} usado por {2}.", carId, couponId, character.Name);
            }

            Vehicle givenVehicle;
            Item givenKey;
            bool gaveVehicle = character.GrantVehicle((int) carId, out givenVehicle, out givenKey, (int) color, grade);
            if (!gaveVehicle)
            {
                Log.WriteError($"Failed to grant vehicle {carId}");
                return;
            }

            character.ShopBuys++;
            bool selectedCar = character.SelectCar(givenVehicle.CarID);
            if (!selectedCar)
            {
                Log.WriteError($"Failed to select vehicle {carId}");
                return;
            }

            var ack = new RicePacket(86);
            ack.Writer.Write(givenVehicle.GetInfo());
            ack.Writer.Write(price);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }

        // Compra por comando de chat ("!carro 141" ou "!carro shadow"): serve para carros que a loja do cliente nao lista
        // (gacha/evento/cupom). Respeita nivel do tier e o nivel das categorias Xmas/BT/ST/MINI (SpecialCarsMinLevel).
        public static string BuyByCommand(Rice.Game.Character buyer, string query)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0)
                return "Uso: !carro <id ou parte do nome>";

            int carId;
            if (!int.TryParse(query, out carId))
            {
                var found = VehicleTable.Vehicles
                    .Where(kv => VehicleTable.VehicleUpgrades.ContainsKey(kv.Key) && kv.Value.UiName != null
                                 && kv.Value.UiName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(kv => kv.Key.ToString() + "=" + kv.Value.UiName).Take(8).ToList();
                if (found.Count == 0)
                    return "Nenhum carro com esse nome.";
                if (found.Count > 1)
                    return "Varios carros: " + string.Join(", ", found) + ". Use !carro <id>.";
                carId = int.Parse(found[0].Split('=')[0]);
            }

            if (!VehicleTable.Vehicles.ContainsKey(carId) || !VehicleTable.VehicleUpgrades.ContainsKey(carId))
                return "Carro " + carId + " nao existe.";

            if (Bots.Unsupported(carId))
                return "Esse carro o jogo nao suporta (nao aparece na loja e pode fechar o jogo). Escolha outro.";
            var vehEntry = VehicleTable.Vehicles[carId];
            if (buyer.Garage.Any(v => v.CarType == (uint) carId))
                return "Esse carro ja esta na garagem.";

            var upgrades = VehicleTable.VehicleUpgrades[carId];
            int grade = Bots.ShopGrade(carId);
            if (grade < 1 || grade > upgrades.Count) grade = vehEntry.Level;
            int requiredLevel = Rice.Game.Character.GetRequiredLevelForTier(grade);
            if (vehEntry.IsSpecialCategory())
                requiredLevel = Math.Max(requiredLevel, RiceServer.Config.SpecialCarsMinLevel);
            if (buyer.Level < requiredLevel)
                return "Nivel insuficiente: exige nivel " + requiredLevel + ".";

            if (grade < 1 || grade > upgrades.Count)
                return "Grau invalido para esse carro.";
            int price = upgrades[grade - 1].Price;
            if (!buyer.SpendMito((ulong) price))
                return "Mito insuficiente (" + price + ").";

            Vehicle veh;
            Item key;
            if (!buyer.GrantVehicle(carId, out veh, out key, 0, grade))
            {
                buyer.GrantMito((ulong) price, false);
                return "Falha ao entregar o carro.";
            }
            Log.WriteLine("Carro {0} ({1}) comprado por comando por {2} (preco {3}).", carId, vehEntry.UiName, buyer.Name, price);
            return "Comprado: " + vehEntry.UiName + " por " + price + " mito. Saia e entre no jogo para ver na garagem.";
        }

        [RicePacket(87, RiceServer.ServerType.Game)]
        public static void SellCar(RicePacket packet)
        {
            string charName = packet.Reader.ReadUnicodeStatic(21); // what the fug npludo
            uint carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            if (carId == character.CurrentCarID)
            {
                packet.Sender.Error("dude you're driving that");
                return;
            }

            var vehicle = character.Garage.FirstOrDefault(v => v.CarID == carId);
            if (vehicle == null)
            {
                packet.Sender.Error("fuck you man this aint your car");
                Log.WriteError($"Client attempted to select invalid vehicle id {carId}");
                return;
            }

            var price = vehicle.VehicleUpgradeEntry.SellPrice;

            bool tookVehicle = character.RemoveVehicle((int) carId);
            if (!tookVehicle)
            {
                Log.WriteError($"Failed to take vehicle {carId} for SellCar");
                return;
            }

            bool gaveMoney = character.GrantMito((ulong) price, false);
            if (!gaveMoney)
            {
                packet.Sender.Error("took your car but you aint gettin any mito sry");
                Log.WriteError($"Failed to give mito for vehicle {carId} for SellCar");
                return;
            }

            var ack = new RicePacket(88);
            ack.Writer.Write(carId);
            ack.Writer.Write((int)price);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }

        [RicePacket(89, RiceServer.ServerType.Game)]
        public static void SelectCar(RicePacket packet)
        {
            uint carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;

            var vehicle = character.Garage.FirstOrDefault(v => v.CarID == carId);
            if (vehicle == null)
            {
                packet.Sender.Error("fuck you man this aint your car");
                Log.WriteError($"Client attempted to select invalid vehicle id {carId}");
                return;
            }

            character.SelectCar((int) carId);

            var ack = new RicePacket(90);
            ack.Writer.Write(vehicle.GetInfo());
            packet.Sender.Send(ack);

            var stat = new RicePacket(760);
            stat.Writer.Write(character.GetStatUpdate());
            packet.Sender.Send(stat);
        }
    }
}
