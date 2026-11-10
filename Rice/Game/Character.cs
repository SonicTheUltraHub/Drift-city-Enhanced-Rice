using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Rice.Server.Core;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;
using Models = Rice.Server.Database.Models;

namespace Rice.Game
{
    public class Character
    {
        public User User;
        public ulong UID;
        public ulong CID;

        public string Name;
        public long Mito;
        public int Avatar;
        public int Level;
        public long Experience;
        public int City;
        public int LastAreaId = -1;
        public bool InNeoBase;    // na base ROAD da Neo City (o jogo usa uma area de cidade com a cidade 6/0)
        // Ultima posicao conhecida do carro (pacote de movimento), usada pelo trafego da cidade.
        public float LastX, LastY;
        public bool HasPosition;
        public Vector4 Position;
        public int PosState;
        public int CurrentCarID;
        public int GarageLevel;
        // carros comprados nesta visita a loja (o jogo cria um modelo por compra e trava quando acaba o espaco dele)
        public int ShopBuys;
        public long TID;

        public List<Item> Inventory;
        public List<Vehicle> Garage;

        private List<ItemModInfo> PendingItemMods;
        // Avisos para o chat (recompensas etc.); saem em FlushNotices.
        public List<string> PendingNotices = new List<string>();

        public Area Area;
        public Quest Quest;
        public Vehicle Vehicle => Garage.FirstOrDefault(veh => veh.CarID == CurrentCarID);
        public ushort Serial;

        private Character(Models.Character dbCharacter)
        {
            CID = (ulong) dbCharacter.ID;
            UID = (ulong) dbCharacter.UID;
            Name = dbCharacter.Name;
            Mito = dbCharacter.Mito;
            Avatar = dbCharacter.Avatar;
            Level = dbCharacter.Level;
            Experience = dbCharacter.Experience;
            City = dbCharacter.City;
            Position = new Vector4(dbCharacter.PosX, dbCharacter.PosY, dbCharacter.PosZ);
            PosState = dbCharacter.PosState;
            CurrentCarID = dbCharacter.CurrentCarID;
            GarageLevel = dbCharacter.GarageLevel;
            TID = dbCharacter.TID;

            // Itens e carros por consulta explicita (nao depende de lazy loading).
            long dbId = dbCharacter.ID;
            using (var rc = Database.GetContext())
            {
                Inventory = rc.Items.Where(i => i.CID == dbId).ToList().Select(Item.FromDB).ToList();
                Garage = rc.Vehicles.Where(v => v.CID == dbId).ToList().Select(Vehicle.FromDB).ToList();
            }
            PendingItemMods = new List<ItemModInfo>();
            Log.WriteDebug($"char init, {Inventory.Count} items in inv, {Garage.Count} vehicles");
        }

        public ExpInfo GetExpInfo()
        {
            return ExpInfo.FromLevelExp(Level, Experience);
        }

        public bool SelectCar(int carId)
        {
            CurrentCarID = carId;
            using (var rc = Database.GetContext())
            {
                var character = rc.Characters.Find((long)CID);
                character.CurrentCarID = carId;
                rc.SaveChanges();
                return true;
            }
        }

        // drink = vale o EXP drink da Drift Shop (recompensa de quest: false)
        public bool GrantExperience(long exp, bool drink = true)
        {
            if (exp > 0 && drink) { try { exp = (long) Math.Round(exp * Rice.Server.Packets.Game.VisualShop.ExpMult(this)); } catch (Exception) { } }
            if (exp > 0) { double mult = 1.0; try { mult = Rice.Server.Packets.Game.CrewLadder.RewardMult(this); } catch (Exception) { } if (mult != 1.0) exp = (long) (exp * mult); }
            int oldLevel = Level;
            Experience += exp;
            if (exp > 0) { try { Rice.Server.Packets.Game.PartyGrowth.OnPlayerProgress(this); } catch (Exception) { } }
            while (Level < RiceServer.ExpSumTable.Length - 1 && Experience > RiceServer.ExpSumTable[Level])
                Level++;

            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long) CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for GrantMito");
                    return false;
                }

                ch.Experience = Experience;
                ch.Level = Level; // salva o nivel (antes so o XP era salvo)
                rc.SaveChanges();
            }

            if (Level > oldLevel)
                Rewards.OnLevelUp(this, oldLevel, Level);
            return true;
        }

        // Tira do carro as pecas "Sorched" (id terminado em _b) sem precisar do Heat Treatment Kit. Devolve quantas saiu.
        public int RemoveScorchedParts(int carId)
        {
            var list = Inventory.Where(i => i.curCarID == (uint) carId && i.state == 1 && i.ID != null && i.ID.EndsWith("_b")
                                            && i.itemEntry is ItemTableEntry).ToList();
            int n = 0;
            foreach (var it in list)
            {
                if (UnEquipItem((int) it.invIdx, carId))
                    n++;
            }
            return n;
        }

        // Envia ao chat os avisos acumulados (recompensas de nivel/combo).
        public void FlushNotices(RiceClient client)
        {
            if (PendingNotices.Count == 0)
                return;
            var notices = PendingNotices.ToArray();
            PendingNotices.Clear();
            foreach (var n in notices)
                Rice.Server.Packets.Game.Chat.SendSystemMessage(client, n);
        }

        // mais um andar na garagem (premio do chefe de crew)
        public void AddGarageSlot()
        {
            GarageLevel++;
            using (var rc = Database.GetContext())
            {
                var dc = rc.Characters.Find((long) CID);
                if (dc == null) return;
                dc.GarageLevel = GarageLevel;
                rc.SaveChanges();
            }
        }

        // reward = premio (missao, corrida, HUV...): com o Razor na sua crew vale em dobro. Venda/leilao/reembolso: false.
        public bool GrantMito(ulong mito, bool reward = true, bool drink = true)
        {
            // MITO drink da Drift Shop: so em premio jogando (nao em venda/leilao nem em recompensa de quest)
            if (reward && drink) { try { mito = (ulong) Math.Round(mito * Rice.Server.Packets.Game.VisualShop.MitoMult(this)); } catch (Exception) { } }
            if (reward) { double mult = 1.0; try { mult = Rice.Server.Packets.Game.CrewLadder.RewardMult(this); } catch (Exception) { } if (mult != 1.0) mito = (ulong) (mito * mult); }
            // membro de crew (outro corredor seu): o lider da crew ganha a metade disso
            if (reward) { try { Rice.Server.Packets.Game.CrewLadder.LeaderShare((long) CID, (long) mito, "premio", Name); } catch (Exception) { } }
            Mito += (long)mito;

            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long)CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for GrantMito");
                    return false;
                }

                ch.Mito = Mito;
                rc.SaveChanges();
            }
            return true;
        }

        public bool SpendMito(ulong mito)
        {
            long mitoAfter = Mito - (long)mito;
            if (mitoAfter < 0)
                return false;

            Mito = mitoAfter;
            
            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long)CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for SpendMito");
                    return false;
                }

                ch.Mito = Mito;
                rc.SaveChanges();
            }
            return true;
        }

        public bool GrantItem(string ID, uint StackNum, out Item resultItem)
        {
            Log.WriteLine($"Attempting to grant CID {CID} item {ID} (count {StackNum})");
            
            // Itens empilhaveis (protetor de booster, enhancer...) somam no mesmo slot ate o maximo da pilha.
            var entry = ItemTable.Items.FirstOrDefault(i => i.ID == ID);
            if (entry != null && entry.IsStackable() && StackNum > 0)
            {
                uint max;
                try { max = entry.GetMaxStack(); }
                catch (Exception) { max = 99; }
                var existing = Inventory.FirstOrDefault(i => i.ID == ID && i.state == 0 && i.assistA == 0
                                                             && i.stackNum + StackNum <= max);
                if (existing != null)
                {
                    using (var rc = Database.GetContext())
                    {
                        var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long) CID && i.InvIdx == (int) existing.invIdx);
                        if (dbItem != null)
                        {
                            dbItem.StackNum = (int) (existing.stackNum + StackNum);
                            rc.SaveChanges();
                            existing.stackNum += StackNum;
                            resultItem = existing;
                            addItemMod(existing);
                            return true;
                        }
                    }
                }
            }

            var item = Item.CreateEntry(CID, ID, StackNum);
            resultItem = item;

            if (item == null)
                return false;
            
            Inventory.Add(item); // should be auto-added to inv in db at CreateEntry
            addItemMod(item);
            // dock novo: ja vem com os pontos sorteados pela letra
            if (item.itemEntry != null && item.itemEntry.Category != null && item.itemEntry.Category.StartsWith("dock_"))
                PartCrafting.FixDock(this, item);
            // option part nova: sorteia 0 ate OptionQualityRange pontos a mais
            if (item.itemEntry != null && item.itemEntry.Category != null && item.itemEntry.Category.StartsWith("op_"))
                PartCrafting.RollOptionQuality(this, item);
            return true;
        }

        public bool EquipItem(int invIdx, short destSlot, int carId)
        {
            var invItem = Inventory.SingleOrDefault(i => i.invIdx == invIdx);
            if (invItem == null)
            {
                Log.WriteError($"Attempted to equip non-existant item at invIdx {invIdx}");
                return false;
            }

            if (Garage.SingleOrDefault(v => v.CarID == carId) == null)
            {
                Log.WriteError($"Couldn't find car id {carId} to equip item at invIdx {invIdx}");
                return false;
            }

            var itemCat = invItem.itemEntry.Category;
            if (!ItemTable.SlotMap.ContainsKey(itemCat) ||
                !ItemTable.SlotMap[itemCat].Contains(destSlot))
            {
                Log.WriteError($"Attempted to equip invIdx {invIdx} to {destSlot} which is invalid for category {itemCat}");
                return false;
            }

            using (var rc = Database.GetContext())
            {
                var itemsInSlot = rc.Items.Count(i => i.CID == (long) CID && i.State == 1 && i.Slot == destSlot && i.CurCarID == carId);
                if (itemsInSlot >= 3)
                {
                    Log.WriteError(
                        $"Attempted to equip invIdx {invIdx} to {destSlot} which already has {itemsInSlot} items");
                    return false;
                }

                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long) CID && i.InvIdx == invIdx);
                if (dbItem == null)
                {
                    Log.WriteError($"Failed to find item invIdx {invIdx} for Character.EquipItem");
                    return false;
                }

                // Option Part / Docking: troca a que ja estava no quadrado dessa peca (uma por peca)
                bool topPart = itemCat.StartsWith("op_") || itemCat.StartsWith("dock_");
                if (topPart)
                {
                    string kind = itemCat.StartsWith("op_") ? "op_" : "dock_";
                    foreach (var old in Inventory.Where(i => i != invItem && i.curCarID == (uint) carId && i.state == 1 && i.slot == (ushort) destSlot
                                                             && i.itemEntry != null && i.itemEntry.Category.StartsWith(kind)).ToList())
                    {
                        var dbOld = rc.Items.SingleOrDefault(x => x.CID == (long) CID && x.InvIdx == old.invIdx);
                        if (dbOld == null) continue;
                        dbOld.Slot = 0; dbOld.CurCarID = 0; dbOld.State = 0;
                        old.slot = 0; old.curCarID = 0; old.state = 0;
                        addItemMod(old, true);
                    }
                }

                dbItem.Slot = destSlot;
                invItem.slot = (ushort) destSlot;

                dbItem.CurCarID = carId;
                invItem.curCarID = (uint) carId;

                dbItem.State = 1;
                invItem.state = 1;

                rc.SaveChanges();
                addItemMod(invItem, true);
            }

            return true;
        }

        public bool UnEquipItem(int invIdx, int carId)
        {
            var invItem = Inventory.SingleOrDefault(i => i.invIdx == invIdx);
            if (invItem == null)
            {
                Log.WriteError($"Attempted to unequip non-existant item at invIdx {invIdx}");
                return false;
            }

            if (Garage.SingleOrDefault(v => v.CarID == carId) == null)
            {
                Log.WriteError($"Couldn't find car id {carId} to unequip item at invIdx {invIdx}");
                return false;
            }

            using (var rc = Database.GetContext())
            {
                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long)CID && i.InvIdx == invIdx && i.CurCarID == carId);
                if (dbItem == null)
                {
                    Log.WriteError($"Failed to find item invIdx {invIdx} carId {carId} for Character.UnEquipItem");
                    return false;
                }

                var slotBefore = dbItem.Slot;
                dbItem.Slot = 0;
                invItem.slot = 0;

                dbItem.CurCarID = 0;
                invItem.curCarID = 0;

                dbItem.State = 0;
                invItem.state = 0;

                // a peca saiu: a Option Part / Docking que estava no quadrado dela sai junto
                string cat = invItem.itemEntry != null ? invItem.itemEntry.Category : "";
                if (cat == "speed" || cat == "accel" || cat == "crash" || cat == "boost")
                {
                    foreach (var on in Inventory.Where(i => i != invItem && i.curCarID == (uint) carId && i.state == 1 && i.itemEntry != null
                                                            && (i.itemEntry.Category.StartsWith("op_") || i.itemEntry.Category.StartsWith("dock_"))).ToList())
                    {
                        var dbOn = rc.Items.SingleOrDefault(x => x.CID == (long) CID && x.InvIdx == on.invIdx);
                        if (dbOn == null || dbOn.Slot != slotBefore) continue;
                        dbOn.Slot = 0; dbOn.CurCarID = 0; dbOn.State = 0;
                        on.slot = 0; on.curCarID = 0; on.state = 0;
                        addItemMod(on, true);
                    }
                }

                rc.SaveChanges();
                addItemMod(invItem, true);
            }

            return true;
        }

        public bool GrantVehicle(int carSort, out Vehicle resultVehicle, out Item resultKeyItem, int color = 0, int grade = 1)
        {
            Log.WriteLine($"Attempting to grant CID {CID} car {carSort}");

            var vehicle = Vehicle.CreateEntry(CID, carSort, color, grade);
            resultVehicle = vehicle;
            if (vehicle == null)
            {
                resultKeyItem = null;
                return false;
            }

            var vehicleListEntry = VehicleTable.Vehicles[carSort];
            var tableKeyItem = ItemTable.Items.FirstOrDefault(i => i.Category == "car" && i.ID == vehicleListEntry.GetKeyId());

            if (tableKeyItem == null)
            {
                Log.WriteError("Failed to find key in item table");
                // TODO: roll back vehicle creation :|
                resultKeyItem = null;
                return false;
            }

            var keyItem = Item.CreateEntry(CID, tableKeyItem.ID, 1, carId: vehicle.CarID);
            resultKeyItem = keyItem;
            if (keyItem == null)
            {
                Log.WriteError("Failed to create key");
                return false;
            }

            Garage.Add(vehicle);
            Inventory.Add(keyItem);
            addItemMod(keyItem);
            return true;
        }

        public bool RemoveVehicle(int carId)
        {
            var vehicle = Garage.SingleOrDefault(v => v.CarID == carId);
            if (vehicle == null)
            {
                Log.WriteError($"Failed to find vehicle for RemoveVehicle({carId})");
                return false;
            }

            var key = Inventory.SingleOrDefault(i => i.curCarID == vehicle.CarID);
            bool noKeyOk = vehicle.IsTraining;           // carro de treino do tutorial nao tem chave
            if (key == null && !noKeyOk)
            {
                Log.WriteError($"Failed to find vehicle key for RemoveVehicle({carId})");
                return false;
            }

            using (var rc = Database.GetContext())
            {
                var dbVehicle = rc.Vehicles.SingleOrDefault(v => v.CID == (long) CID && v.CarID == vehicle.CarID);
                var dbKey = rc.Items.SingleOrDefault(i => i.CID == (long) CID && i.CurCarID == vehicle.CarID);

                if (dbVehicle == null || (dbKey == null && !noKeyOk))
                {
                    Log.WriteError($"Failed to find db counterparts of vehicle/key for RemoveVehicle({carId})");
                    return false;
                }

                rc.Vehicles.Remove(dbVehicle);
                if (dbKey != null) rc.Items.Remove(dbKey);
                rc.SaveChanges();

                Garage.Remove(vehicle);
                if (key != null)
                {
                    Inventory.Remove(key);
                    key.stackNum = 0;
                    addItemMod(key);
                }
            }
            return true;
        }

        // Proxima peca da mesma familia (ex.: i_s_000013 Riddle N -> i_s_000014 Myth S). Null se nao houver.
        private static string GetNextPartId(string id, string category)
        {
            var match = System.Text.RegularExpressions.Regex.Match(id ?? "", @"^(.*_)(\d+)$");
            if (!match.Success)
                return null;

            string digits = match.Groups[2].Value;
            string nextId = match.Groups[1].Value + (int.Parse(digits) + 1).ToString().PadLeft(digits.Length, '0');

            var current = ItemTable.Items.FirstOrDefault(i => i.ID == id) as ItemTableEntry;
            var next = ItemTable.Items.FirstOrDefault(i => i.ID == nextId) as ItemTableEntry;
            if (current == null || next == null || next.Category != category)
                return null;

            // Precisa ser exatamente o proximo grau: N -> S -> H -> U.
            string[] order = { "N", "S", "H", "U" };
            int a = Array.IndexOf(order, current.Grade);
            int b = Array.IndexOf(order, next.Grade);
            return (a >= 0 && b == a + 1) ? nextId : null;
        }

        // Regra 1 do "Combine": 3 pecas iguais (fora do carro) viram 1 peca de nivel acima.
        public bool CombineParts(int[] invIdxs, out Item result, out string reason)
        {
            result = null;
            reason = null;

            if (invIdxs == null || invIdxs.Length != 3 || invIdxs.Distinct().Count() != 3)
            {
                reason = "escolha 3 pecas diferentes";
                return false;
            }

            var items = invIdxs.Select(idx => Inventory.FirstOrDefault(x => x.invIdx == idx)).ToList();
            if (items.Any(i => i == null))
            {
                reason = "peca nao encontrada no inventario";
                return false;
            }

            var first = items[0];
            if (items.Any(i => i.ID != first.ID))
            {
                reason = "as 3 pecas precisam ser iguais";
                return false;
            }

            if (items.Any(i => i.state != 0 || i.stackNum < 1 || !i.itemEntry.IsSellable()))
            {
                reason = "desequipe as pecas antes de combinar";
                return false;
            }

            string category = first.itemEntry.Category;
            if (category != "speed" && category != "accel" && category != "boost" && category != "crash")
            {
                reason = "so pecas de performance podem ser combinadas";
                return false;
            }

            string nextId = GetNextPartId(first.ID, category);
            if (nextId == null)
            {
                reason = "esta peca ja esta no nivel maximo";
                return false;
            }

            foreach (var item in items)
            {
                Item dropped;
                if (!DropItem((int) item.invIdx, out dropped, 1))
                {
                    reason = "falha ao consumir as pecas";
                    return false;
                }
            }

            return GrantItem(nextId, 1, out result);
        }

        public bool DropItem(int invIdx, out Item resultItem, uint count = 1, bool force = false)
        {
            using (var rc = Database.GetContext())
            {
                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long)CID && i.InvIdx == invIdx && i.StackNum >= count);
                var invItem = Inventory.SingleOrDefault(i => i.invIdx == invIdx);

                if (dbItem == null || invItem == null || (!force && !invItem.itemEntry.IsSellable()))
                {
                    Log.WriteError($"Failed to find droppable item invIdx {invIdx} count >= {count} owned by {CID}");
                    resultItem = null;
                    return false;
                }

                bool destroyItem = count == dbItem.StackNum && count == invItem.stackNum;
                if (destroyItem)
                {
                    rc.Items.Remove(dbItem);
                    Inventory.Remove(invItem);
                    invItem.stackNum = 0;
                }
                else
                {
                    dbItem.StackNum -= (int)count;
                    invItem.stackNum -= count;
                }
                rc.SaveChanges();
                resultItem = invItem;
                addItemMod(invItem);
                return true;
            }
        }

        // Grava no banco os campos que o Part Shop altera (opcoes/assist e pontos) e avisa o cliente.
        // muda o V de um carro da garagem (banco + objeto em memoria)
        public Vehicle SetCarGrade(int carId, int grade)
        {
            using (var rc = Database.GetContext())
            {
                var dbv = rc.Vehicles.FirstOrDefault(v => v.CID == (long) CID && v.CarID == carId);
                if (dbv == null) return null;
                dbv.Grade = grade;
                rc.SaveChanges();
                var nv = Vehicle.FromDB(dbv);
                int i = Garage.FindIndex(v => v.CarID == carId);
                if (i >= 0) Garage[i] = nv; else Garage.Add(nv);
                return nv;
            }
        }

        public bool SaveItemState(Item item)
        {
            using (var rc = Database.GetContext())
            {
                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long)CID && i.InvIdx == (int)item.invIdx);
                if (dbItem == null)
                {
                    Log.WriteError($"SaveItemState: item invIdx {item.invIdx} nao encontrado.");
                    return false;
                }
                dbItem.AssistA = (int)item.assistA;
                dbItem.AssistB = (int)item.assistB;
                dbItem.AssistC = (int)item.assistC;
                dbItem.AssistD = (int)item.assistD;
                dbItem.AssistE = (int)item.assistE;
                dbItem.AssistF = (int)item.assistF;
                dbItem.AssistG = (int)item.assistG;
                dbItem.AssistH = (int)item.assistH;
                dbItem.AssistI = (int)item.assistI;
                dbItem.AssistJ = (int)item.assistJ;
                dbItem.UpgradeLevel = item.upgrade;
                dbItem.UpgradePoints = item.upgradePoint;
                dbItem.State = (short) item.state;
                rc.SaveChanges();
            }
            addItemMod(item);
            return true;
        }

        // Avisa o cliente de um item novo/alterado (vai no proximo FlushModInfo).
        public void NotifyItem(Item item, bool newItem = false)
        {
            if (newItem && !Inventory.Contains(item)) Inventory.Add(item);
            addItemMod(item);
        }

        private void addItemMod(Item item, bool moved = false)
        {
            var itemInfo = item.GetInfo();
            var modInfo = new ItemModInfo
            {
                Item = itemInfo,
                State = moved ? 3 : itemInfo.StackNum == 0 ? 2 : 0
            };
            PendingItemMods.Add(modInfo);
        }

        public void FlushModInfo(RiceClient client)
        {
            Rewards.UpdateDockRows(this, client);
            // peca nova da Neo City ja vem com a durabilidade cheia (antes so no proximo minuto: aparecia 1/max)
            try { Rice.Server.Packets.Game.NeoDurability.Prepare(this); } catch (Exception) { }
            var mods = PendingItemMods.ToArray();
            PendingItemMods.Clear();

            //ItemModList
            var packet = new RicePacket(402);
            packet.Writer.Write(mods.Length);
            foreach (var itemMod in mods)
                packet.Writer.Write(itemMod);
            client.Send(packet);

            var stat = new RicePacket(760);
            stat.Writer.Write(GetStatUpdate());
            client.Send(stat);
        } 

        public CharInfo GetInfo()
        {
            return new CharInfo
            {
                Avatar = (ushort)Avatar,
                Name = Name,
                CID = CID,
                City = City,
                Position = Position,
                PosState = PosState,
                CurrentCarID = CurrentCarID,
                PType = (byte)'A',
                TeamJoinDate = DateTime.Now,
                TeamLeaveDate = DateTime.Now,
                TeamCloseDate = DateTime.Now,
                ExpInfo = GetExpInfo(),
                HancoinGarage = IsUpped ? Math.Max(GarageLevel, RiceServer.Config.UppedGarageExpansion) : GarageLevel,
                HancoinInven = IsUpped ? RiceServer.Config.UppedInvenExpansion : 0,
                Level = (ushort)Level,
                TeamId = CrewTeamId(),
                TeamMarkId = CrewMark(),
                TeamName = CrewName(),
                TeamRank = CrewRank(),
                MitoMoney = Mito,
                Flags = -1
            };
        }

        // Crew do modo offline (crews.json).
        private Rice.Server.Packets.Game.Crew.CrewData MyCrew() { return Rice.Server.Packets.Game.Crew.Of((long) CID); }
        private long CrewTeamId() { var c = MyCrew(); return c != null ? c.Tid : -1; }
        private long CrewMark() { var c = MyCrew(); return c != null ? c.Mark : -1; }
        private string CrewName() { var c = MyCrew(); return c != null ? c.Name : ""; }
        private int CrewRank() { var c = MyCrew(); return c == null ? -1 : (c.Master == (long) CID ? 1 : 3); }

        public static Character Retrieve(string charname)
        {
            Character character;

            using (var rc = Database.GetContext())
                character = new Character(rc.Characters.SingleOrDefault(c => c.Name == charname));

            return character;
        }

        public static bool IsNameUsable(string name)
        {
            using (var rc = Database.GetContext())
                return rc.Characters.Count(c => c.Name == name) == 0;
        }

        // ------------------------------------------------------------------
        //  Carros por nivel (tier V1..V9 = campo "lvl" do VehicleList.json)
        // ------------------------------------------------------------------

        public static int GetRequiredLevelForTier(int tier)
        {
            var levels = RiceServer.Config.CarTierLevels;
            if (levels == null || levels.Length == 0 || tier < 1)
                return 0;

            if (tier > levels.Length)
                tier = levels.Length;

            return levels[tier - 1];
        }

        private static int ResolveStarterCar(int requested)
        {
            // tutorial do jogo: o assistente de criacao pede o carro de treino
            if (Vehicle.IsTrainingType(requested) && VehicleTable.Vehicles.ContainsKey(requested))
                return requested;

            int[] candidates = { requested, RiceServer.Config.StartCarID };

            foreach (int id in candidates)
            {
                VehicleListEntry entry;
                // carros V1 da loja ou um dos carros iniciais do assistente (Sellable 10001, 20001...)
                if (VehicleTable.Vehicles.TryGetValue(id, out entry) && entry.Level == 1 && !entry.HasCondition()
                    && (entry.InDealership() || (entry.Sellable > 10000 && entry.Sellable % 10000 == 1)))
                    return id;
            }

            // Ultimo recurso: qualquer carro V1 vendavel e sem condicao.
            foreach (var kv in VehicleTable.Vehicles.OrderBy(v => v.Key))
            {
                if (kv.Value.Level == 1 && kv.Value.InDealership() && !kv.Value.HasCondition())
                    return kv.Key;
            }

            return -1;
        }

        // Cria o carro inicial V1 (com a chave) para um corredor e o define como carro atual.
        private static bool GiveStarterCar(ulong cid, int requestedCar, int color, out Vehicle vehicle, out Item keyItem)
        {
            vehicle = null;
            keyItem = null;

            int starterCar = ResolveStarterCar(requestedCar);
            if (starterCar < 0)
            {
                Log.WriteError("Nenhum carro inicial V1 valido foi encontrado em VehicleList.json.");
                return false;
            }

            var entry = VehicleTable.Vehicles[starterCar];
            vehicle = Vehicle.CreateEntry(cid, starterCar, color, entry.Level);
            var tableKeyItem = ItemTable.Items.FirstOrDefault(i => i.Category == "car" && i.ID == entry.GetKeyId());

            if (vehicle != null && tableKeyItem != null)
                keyItem = Item.CreateEntry(cid, tableKeyItem.ID, 1, carId: vehicle.CarID);

            // o carro de treino do tutorial nao tem chave no jogo
            if (vehicle == null || (keyItem == null && !Vehicle.IsTrainingType(starterCar)))
                return false;

            using (var rc = Database.GetContext())
            {
                long id = (long) cid;
                var ch = rc.Characters.Find(id);
                if (ch != null)
                {
                    ch.CurrentCarID = vehicle.CarID;
                    rc.SaveChanges();
                }
            }

            return true;
        }

        // Repara corredores sem carro (ex.: criados por versoes antigas): da o carro inicial.
        public bool EnsureStarterCar()
        {
            if (Vehicle != null)
                return true;

            if (Garage.Count > 0)
            {
                // Tem carros, mas o "carro atual" aponta para um que nao existe.
                CurrentCarID = Garage[0].CarID;
                SelectCar(CurrentCarID);
                return true;
            }

            Log.WriteLine("Corredor '{0}' esta sem carro; criando o carro inicial.", Name);

            Vehicle newVehicle;
            Item newKey;
            if (!GiveStarterCar(CID, 0, 0, out newVehicle, out newKey))
                return false;

            Garage.Add(newVehicle);
            Inventory.Add(newKey);
            CurrentCarID = newVehicle.CarID;
            return true;
        }

        // Corredores abaixo do nivel inicial (ex.: criados no nivel 0) sobem para o nivel inicial.
        public void EnsureMinimumLevel()
        {
            int min = RiceServer.Config.StartLevel;
            if (Level >= min)
                return;

            Level = min;
            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long) CID);
                if (ch != null)
                {
                    ch.Level = min;
                    rc.SaveChanges();
                }
            }
        }

        // Carro comprado de outro jogador/bot: vem com o combustivel e a quilometragem do anuncio.
        public void SetCarCondition(int carId, float fuel, float km)
        {
            var mem = Garage.FirstOrDefault(g => g.CarID == carId);
            if (mem == null)
                return;
            float capacity = Vehicle.ScaledCapacity(mem.VehicleUpgradeEntry);
            mem.Mitron = Math.Max(0f, Math.Min(fuel, capacity));
            mem.Kms = Math.Max(0f, km);
            long id = (long) CID;
            using (var rc = Database.GetContext())
            {
                var dbVehicle = rc.Vehicles.FirstOrDefault(v => v.CID == id && v.CarID == carId);
                if (dbVehicle != null)
                {
                    dbVehicle.Mitron = mem.Mitron;
                    dbVehicle.Kms = mem.Kms;
                    rc.SaveChanges();
                }
            }
        }

        // Guarda o combustivel informado pelo jogo (pacote DriveInfoUpdate).
        public void UpdateFuel(int carId, float fuel)
        {
            var mem = Garage.FirstOrDefault(g => g.CarID == carId);
            if (mem == null)
                return;

            float capacity = Vehicle.ScaledCapacity(mem.VehicleUpgradeEntry);
            if (fuel < 0f)
                fuel = 0f;
            if (fuel > capacity)
                fuel = capacity;

            if (Math.Abs(mem.Mitron - fuel) < 0.01f)
                return;

            mem.Mitron = fuel;

            long id = (long) CID;
            using (var rc = Database.GetContext())
            {
                var dbVehicle = rc.Vehicles.FirstOrDefault(v => v.CID == id && v.CarID == carId);
                if (dbVehicle != null)
                {
                    dbVehicle.Mitron = fuel;
                    rc.SaveChanges();
                }
            }
        }

        // Abastece 'liters' litros no carro, cobrando 'cost' mito. Falha se nao houver mito suficiente.
        public bool Refuel(int carId, float liters, int cost, out float newFuel)
        {
            newFuel = 0f;

            var mem = Garage.FirstOrDefault(g => g.CarID == carId);
            if (mem == null)
                return false;

            if (cost < 0)
                cost = 0;

            if (!SpendMito((ulong) cost))
                return false;

            float capacity = Vehicle.ScaledCapacity(mem.VehicleUpgradeEntry);
            newFuel = Math.Min(capacity, mem.Mitron + liters);
            UpdateFuel(carId, newFuel);
            return true;
        }

        // Enche o tanque de todos os carros do corredor.
        public void RefuelAll()
        {
            if (!RiceServer.Config.AutoRefuelOnEnter)
                return;

            long id = (long) CID;
            using (var rc = Database.GetContext())
            {
                foreach (var dbVehicle in rc.Vehicles.Where(v => v.CID == id).ToList())
                {
                    var mem = Garage.FirstOrDefault(g => g.CarID == dbVehicle.CarID);
                    if (mem == null)
                        continue;

                    float capacity = Vehicle.ScaledCapacity(mem.VehicleUpgradeEntry);
                    dbVehicle.Mitron = capacity;
                    mem.Mitron = capacity;
                }
                rc.SaveChanges();
            }
        }

        private static bool uppedLoadoutDone;

        public bool IsUpped
        {
            get { return string.Equals(Name, RiceServer.Config.UppedProfileName, StringComparison.OrdinalIgnoreCase); }
        }

        // Perfil upado: Shadow e BMW Patrol com pecas, garagem completa e inventario cheio.
        // Roda uma vez por execucao do servidor e so cria o que ainda nao existe.
        public static void EnsureUppedLoadout()
        {
            var cfg = RiceServer.Config;
            if (uppedLoadoutDone || string.IsNullOrEmpty(cfg.UppedProfileName))
                return;

            try
            {
                long cid;
                using (var rc = Database.GetContext())
                {
                    string upped = cfg.UppedProfileName;
                    var ch = rc.Characters.FirstOrDefault(c => c.Name == upped);
                    if (ch == null)
                        return;
                    cid = ch.ID;
                }

                // so monta UMA vez por corredor (antes era a cada abertura do servidor: tirava os carros que voce comprou,
                // mexia no inventario...). Marca em save/perfil_upado.txt. UppedRefreshEveryStart = true volta ao antigo.
                string mark = Paths.SaveFile("perfil_upado.txt");
                var doneCids = System.IO.File.Exists(mark) ? System.IO.File.ReadAllLines(mark).Select(l => l.Trim()).Where(l => l != "").ToList() : new List<string>();
                if (!cfg.UppedRefreshEveryStart && doneCids.Contains(cid.ToString()))
                {
                    uppedLoadoutDone = true;
                    return;
                }

                int mainCar = 0;
                if (cfg.UppedCarID > 0)
                    mainCar = EnsureCarWithParts(cid, cfg.UppedCarID, cfg.UppedCarGrade, true);

                int cars = 0;
                if (cfg.UppedOnlyOneCar && mainCar > 0)
                    KeepOnlyCar(cid, mainCar);
                else
                {
                    if (cfg.UppedPoliceCarID > 0 && cfg.UppedPoliceCarID != cfg.UppedCarID)
                        EnsureCarWithParts(cid, cfg.UppedPoliceCarID, 9, false);
                    cars = AddUppedGarageCars(cid);
                }
                if (mainCar > 0 && cfg.UppedCarAssists != null && cfg.UppedCarAssists.Length > 0)
                    Rewards.SetAdminDockRows(cid, mainCar, cfg.UppedCarAssists);

                int items = FillUppedInventory(cid);
                if (cfg.UppedAllQuests) CompleteMainQuests(cid);

                Log.WriteLine("Perfil upado pronto: {0} carro(s) novo(s) e {1} item(ns) novo(s) no inventario.", cars, items);
                uppedLoadoutDone = true;
                if (!doneCids.Contains(cid.ToString())) System.IO.File.AppendAllText(mark, cid + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Log.WriteError("EnsureUppedLoadout falhou: {0}", ex);
            }
        }

        // Perfil upado: todas as missoes principais (missiontype "main") como concluidas e recompensadas (estado 2).
        // Das duas finais de faccao fica so a OMD (a ROO e a outra escolha). As repetiveis (rq_) ficam livres.
        private static void CompleteMainQuests(long cid)
        {
            var quests = QuestTable.Quests;
            if (quests == null) return;
            int added = 0;
            using (var rc = Database.GetContext())
            {
                var have = new HashSet<int>(rc.QuestStates.Where(q => q.CID == cid).Select(q => q.QID));
                for (int i = 0; i < quests.Count; i++)
                {
                    var q = quests[i];
                    if (q == null || !string.Equals(q.MissionType, "main", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(q.Faction, "ROO", StringComparison.OrdinalIgnoreCase)) continue;
                    if (have.Contains(i))
                    {
                        var st = rc.QuestStates.FirstOrDefault(x => x.CID == cid && x.QID == i);
                        if (st != null && st.State != 2) { st.State = 2; added++; }
                        continue;
                    }
                    rc.QuestStates.Add(new Models.QuestState { CID = cid, QID = i, State = 2, Progress = 0, FailCount = 0 });
                    added++;
                }
                rc.SaveChanges();
            }
            if (added > 0) Log.WriteLine("Perfil upado: {0} missao(oes) principal(is) marcada(s) como concluida(s).", added);
        }

        // Conta admin com um carro so: tira os outros carros do perfil upado (com a chave e o que estava equipado neles)
        // e deixa o carro que ficou como o atual.
        private static void KeepOnlyCar(long cid, int keepCarId)
        {
            using (var rc = Database.GetContext())
            {
                var other = rc.Vehicles.Where(v => v.CID == cid && v.CarID != keepCarId).ToList();
                var ids = new HashSet<int>(other.Select(v => v.CarID));
                if (ids.Count > 0)
                {
                    var keys = new HashSet<string>(ItemTable.Items.Where(e => e.Category == "car").Select(e => e.ID));
                    foreach (var it in rc.Items.Where(i => i.CID == cid && i.CurCarID != 0).ToList())
                        if (ids.Contains(it.CurCarID) && (it.State == 1 || keys.Contains(it.ItemID))) rc.Items.Remove(it);
                    foreach (var v in other) rc.Vehicles.Remove(v);
                }
                var ch = rc.Characters.Find(cid);
                if (ch != null && ch.CurrentCarID != keepCarId) ch.CurrentCarID = keepCarId;
                rc.SaveChanges();
                if (ids.Count > 0) Log.WriteLine("Perfil upado: {0} carro(s) tirado(s); fica so o carro de policia.", ids.Count);
            }
        }

        // Garante um carro (com a chave) e as pecas equipadas nele. Devolve o CarID, ou 0 se falhar.
        private static int EnsureCarWithParts(long cid, int carType, int grade, bool makeCurrent)
        {
            var cfg = RiceServer.Config;

            if (!VehicleTable.Vehicles.ContainsKey(carType))
            {
                Log.WriteError("Carro invalido para o perfil upado: {0}", carType);
                return 0;
            }

            int carId = 0;
            using (var rc = Database.GetContext())
            {
                var existing = rc.Vehicles.FirstOrDefault(v => v.CID == cid && v.CarType == carType);
                if (existing != null)
                    carId = existing.CarID;
            }

            if (carId == 0)
            {
                var created = Vehicle.CreateEntry((ulong) cid, carType, 0, grade);
                if (created == null)
                {
                    Log.WriteError("Nao foi possivel criar o carro do perfil upado (id {0}).", carType);
                    return 0;
                }
                carId = created.CarID;

                var listEntry = VehicleTable.Vehicles[carType];
                var tableKey = ItemTable.Items.FirstOrDefault(i => i.Category == "car" && i.ID == listEntry.GetKeyId());
                if (tableKey != null)
                    Item.CreateEntry((ulong) cid, tableKey.ID, 1, carId: carId);

                if (makeCurrent)
                {
                    using (var rc = Database.GetContext())
                    {
                        var ch = rc.Characters.Find(cid);
                        if (ch != null)
                        {
                            ch.CurrentCarID = carId;
                            rc.SaveChanges();
                        }
                    }
                }

                Log.WriteLine("Perfil upado: carro {0} (grade {1}) adicionado.", carType, grade);
            }

            // Pecas: 1 por slot (2 slots por categoria).
            foreach (string partId in cfg.UppedPartIds ?? new string[0])
            {
                var partEntry = ItemTable.Items.FirstOrDefault(i => i.ID == partId);
                if (partEntry == null || !ItemTable.SlotMap.ContainsKey(partEntry.Category))
                {
                    Log.WriteError("Peca invalida no UppedPartIds: {0}", partId);
                    continue;
                }

                foreach (int slot in ItemTable.SlotMap[partEntry.Category])
                {
                    short slotId = (short) slot;
                    int equippedCarId = carId;
                    bool alreadyEquipped;
                    using (var rc = Database.GetContext())
                    {
                        alreadyEquipped = rc.Items.Any(i => i.CID == cid && i.State == 1 &&
                                                            i.Slot == slotId && i.CurCarID == equippedCarId);
                    }
                    if (alreadyEquipped)
                        continue;

                    var part = Item.CreateEntry((ulong) cid, partId, 1, carId: carId);
                    if (part == null)
                        continue;

                    int invIdx = (int) part.invIdx;
                    using (var rc = Database.GetContext())
                    {
                        var dbItem = rc.Items.SingleOrDefault(i => i.CID == cid && i.InvIdx == invIdx);
                        if (dbItem != null)
                        {
                            dbItem.Slot = slotId;
                            dbItem.State = 1;
                            dbItem.CurCarID = carId;
                            rc.SaveChanges();
                        }
                    }
                }
            }

            return carId;
        }

        // Coloca na garagem os carros que ainda faltam (com a chave de cada um). Devolve quantos foram criados.
        private static int AddUppedGarageCars(long cid)
        {
            var cfg = RiceServer.Config;
            bool all = string.Equals(cfg.UppedCarScope, "all", StringComparison.OrdinalIgnoreCase);
            var extras = new HashSet<int>(cfg.UppedExtraCarIDs ?? new int[0]);

            var keyById = new Dictionary<string, GenericItemEntry>();
            foreach (var entry in ItemTable.Items)
            {
                if (entry.Category == "car" && !keyById.ContainsKey(entry.ID))
                    keyById[entry.ID] = entry;
            }

            int created = 0;
            using (var rc = Database.GetContext())
            {
                var ownedTypes = new HashSet<int>(rc.Vehicles.Where(v => v.CID == cid).Select(v => v.CarType).ToList());
                int nextCarId = (rc.Vehicles.Where(v => v.CID == cid).Max(v => (int?) v.CarID) ?? 0) + 1;
                int nextInvIdx = (rc.Items.Where(i => i.CID == cid).Max(i => (int?) i.InvIdx) ?? 0) + 1;

                foreach (var kv in VehicleTable.Vehicles.OrderBy(v => v.Value.Level).ThenBy(v => v.Key))
                {
                    int carType = kv.Key;
                    var listEntry = kv.Value;

                    bool inShop = string.Equals(cfg.UppedCarScope, "dealership", StringComparison.OrdinalIgnoreCase)
                        && listEntry.InDealership() && !listEntry.HasCondition();
                    if (!(all || inShop || extras.Contains(carType)))
                        continue;
                    if (ownedTypes.Contains(carType))
                        continue;

                    GenericItemEntry key;
                    if (!keyById.TryGetValue(listEntry.GetKeyId(), out key))
                        continue;

                    // Maior grau valido para este carro (ate o 9).
                    VehicleUpgradeEntry upgrade = null;
                    int grade = 9;
                    for (; grade >= 1; grade--)
                    {
                        try
                        {
                            upgrade = VehicleTable.GetVehicleUpgrade(carType, grade);
                            break;
                        }
                        catch (Exception)
                        {
                        }
                    }
                    if (upgrade == null)
                        continue;

                    rc.Vehicles.Add(new Models.Vehicle
                    {
                        CID = cid,
                        CarID = nextCarId,
                        CarType = carType,
                        Grade = grade,
                        AuctionCount = 0,
                        Color = 0,
                        Mitron = Vehicle.ScaledCapacity(upgrade)
                    });

                    rc.Items.Add(new Models.Item
                    {
                        CID = cid,
                        ItemID = key.ID,
                        StackNum = 1,
                        ItemHealth = 1f,
                        InvIdx = nextInvIdx,
                        CurCarID = nextCarId,
                        LastCarID = nextCarId
                    });

                    nextCarId++;
                    nextInvIdx++;
                    created++;
                }

                if (created > 0)
                    rc.SaveChanges();
            }

            return created;
        }

        // Ids a ignorar: versoes de treino, fim de evento e pacotes coreanos.
        private static bool IsSkippedUppedItem(GenericItemEntry entry)
        {
            string id = entry.ID ?? "";
            string name = entry.Name ?? "";
            return id.EndsWith("_ex") || id.EndsWith("_end") || id.EndsWith("_kr") ||
                   name.IndexOf("Training", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Lista (em ordem de prioridade) dos itens que o perfil upado recebe.
        private static List<string> BuildUppedItemList(Dictionary<string, GenericItemEntry> byId)
        {
            var list = new List<string>();
            var seen = new HashSet<string>();
            Action<string> add = id =>
            {
                GenericItemEntry found;
                if (id != null && id.Length <= 32 && byId.TryGetValue(id, out found) &&
                    found.Category != "car" && seen.Add(id))
                    list.Add(id);
            };

            // 1) Materiais de craft/convert e itens citados no roteiro.
            string[] priority =
            {
                "i_n_Seed_N", "i_n_Seed_S", "i_n_Seed_H", "i_n_Seed_U",
                "i_n_00043", "i_n_00044", "i_n_00045", "i_n_00003",
                "i_n_Seedplus_N", "i_n_Seedplus_S", "i_n_Seedplus_H", "i_n_Seedplus_U",
                "i_n_00040", "i_n_00041", "i_n_00042",
                "i_n_00040_plus", "i_n_00041_plus", "i_n_00042_plus",
                "i_n_00116", "i_n_00117", "i_n_00118",
                "i_n_00116_plus", "i_n_00117_plus", "i_n_00118_plus",
                "i_n_00025", "i_n_00025_U",
                "i_n_00015", "i_n_00016",
                "i_n_00043", "i_n_00044", "i_n_00045",
                "i_n_00043_plus", "i_n_00044_plus", "i_n_00045_plus", "i_n_00045_deluxe",
                "i_n_00003", "i_n_00003_plus", "i_n_00003_deluxe", "i_n_double", "i_n_upgrade",
                "i_n_00004", "i_n_00005", "i_n_00006", "i_n_00099"
            };
            foreach (string id in priority)
                add(id);

            // 2) Consumiveis uteis, por categoria.
            string[] useCategories =
            {
                "fuel", "fuelsave", "turbosave", "heattreatment", "trans", "craft_up", "craft_up_plus",
                "upgrade", "double_upgrade", "accelpointup", "speedpointup", "crashpointup",
                "new_repair", "battle_quick", "mitrondust", "healthupnow"
            };
            foreach (string category in useCategories)
            {
                foreach (var entry in ItemTable.Items.Where(i => i.Category == category))
                {
                    if (!IsSkippedUppedItem(entry))
                        add(entry.ID);
                }
            }

            // 3) Docking parts: 2 de cada grau (Normal, Super, Hyper, Ultra) por tipo.
            string[] dockCategories = { "dock_S", "dock_A", "dock_B", "dock_C" };
            string[] dockGrades = { "ON", "OS", "OH", "OU" };
            foreach (string grade in dockGrades)
            {
                foreach (string category in dockCategories)
                {
                    foreach (var entry in ItemTable.Items.OfType<ItemTableEntry>()
                        .Where(i => i.Category == category && i.Grade == grade).Take(2))
                        add(entry.ID);
                }
            }

            // 4) Pecas de performance: as 2 melhores de cada grau por categoria.
            string[] partCategories = { "speed", "accel", "boost", "crash" };
            string[] partGrades = { "N", "S", "H", "U" };
            foreach (string grade in partGrades)
            {
                foreach (string category in partCategories)
                {
                    foreach (var entry in ItemTable.Items.OfType<ItemTableEntry>()
                        .Where(i => i.Category == category && i.Grade == grade && !IsSkippedUppedItem(i))
                        .OrderByDescending(i => i.BasePoints).ThenBy(i => i.ID.Length).Take(2))
                        add(entry.ID);
                }
            }

            return list;
        }

        // Enche o inventario do perfil upado (itens empilhaveis com 99). Devolve quantos itens novos foram criados.
        private static int FillUppedInventory(long cid)
        {
            var cfg = RiceServer.Config;
            int capacity = Math.Max(0, cfg.UppedInvenPages) * Math.Max(1, cfg.UppedInvenPageSize);
            int stackLimit = Math.Max(1, Math.Min(99, cfg.UppedStackSize));

            var byId = new Dictionary<string, GenericItemEntry>();
            foreach (var entry in ItemTable.Items)
            {
                if (entry.ID != null && !byId.ContainsKey(entry.ID))
                    byId[entry.ID] = entry;
            }

            var wanted = BuildUppedItemList(byId);
            int created = 0;
            int raised = 0;

            using (var rc = Database.GetContext())
            {
                var owned = rc.Items.Where(i => i.CID == cid).ToList();
                int nextInvIdx = (owned.Count == 0 ? 0 : owned.Max(i => i.InvIdx)) + 1;

                // Espacos ja usados (nao conta chaves de carro nem pecas equipadas).
                int used = owned.Count(i => i.State == 0 &&
                                            !(byId.ContainsKey(i.ItemID) && byId[i.ItemID].Category == "car"));

                foreach (string id in wanted)
                {
                    var entry = byId[id];
                    int stack = 1;
                    if (entry.IsStackable())
                    {
                        uint max;
                        try { max = entry.GetMaxStack(); }
                        catch (Exception) { max = 99; }
                        stack = (int) Math.Min((uint) stackLimit, Math.Max(1u, max));
                    }

                    var existing = owned.FirstOrDefault(i => i.ItemID == id && i.State == 0);
                    if (existing != null)
                    {
                        if (existing.StackNum < stack)
                        {
                            existing.StackNum = stack;
                            raised++;
                        }
                        continue;
                    }

                    if (used >= capacity)
                        continue;

                    var item = new Models.Item
                    {
                        CID = cid,
                        ItemID = id,
                        StackNum = stack,
                        ItemHealth = 1f,
                        InvIdx = nextInvIdx++,
                        CurCarID = 0,
                        LastCarID = 0
                    };
                    rc.Items.Add(item);
                    owned.Add(item);
                    used++;
                    created++;
                }

                if (created > 0 || raised > 0)
                    rc.SaveChanges();

                Log.WriteLine("Inventario do perfil upado: {0}/{1} espacos usados, {2} item(ns) novo(s), {3} pilha(s) ajustada(s).",
                    used, capacity, created, raised);
            }

            return created;
        }

        // Diagnostico: lista no console tudo que o banco tem de corredores.
        public static void LogDatabaseDump(ulong uid)
        {
            using (var rc = Database.GetContext())
            {
                Log.WriteDebug("Conta UID {0}. Corredores no banco:", uid);
                foreach (var c in rc.Characters.ToList())
                    Log.WriteDebug("  CID {0} '{1}' (dono UID {2}, nivel {3}, carro atual {4})", c.ID, c.Name, c.UID, c.Level, c.CurrentCarID);
            }
        }

        // Cria um perfil de jogador "novo": nivel inicial, sem XP, e 1 carro V1 gratis.
        public static bool Create(ulong uid, string name, ushort avatar, int carType = 0, int color = 0)
        {
            var cfg = RiceServer.Config;
            long newCid;

            using (var rc = Database.GetContext())
            {
                var model = new Models.Character
                {
                    UID = (long) uid,
                    Name = name,
                    Avatar = avatar,
                    Level = cfg.StartLevel,
                    Experience = 0,
                    Mito = cfg.StartMito,
                    City = 1,
                    TID = -1
                };
                rc.Characters.Add(model);

                try
                {
                    rc.SaveChanges();
                }
                catch (DataException ex)
                {
                    Log.WriteError(ex.ToString());
                    return false;
                }

                newCid = model.ID;
            }

            // Carro inicial + chave do carro.
            Vehicle vehicle;
            Item keyItem;
            if (!GiveStarterCar((ulong) newCid, carType, color, out vehicle, out keyItem))
            {
                Log.WriteError("Falha ao criar o carro inicial; desfazendo a criacao do perfil.");
                Delete(name);
                return false;
            }

            using (var rc = Database.GetContext())
            {
                Log.WriteLine("Verificacao: CID {0} tem {1} carro(s) e {2} item(ns) no banco.",
                    newCid, rc.Vehicles.Count(v => v.CID == newCid), rc.Items.Count(i => i.CID == newCid));
            }

            Log.WriteLine("Perfil '{0}' criado (nivel {1}, carro inicial {2}).", name, cfg.StartLevel, vehicle.CarType);
            return true;
        }

        public static bool Delete(string name)
        {
            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.SingleOrDefault(c => c.Name == name);
                if (ch == null)
                {
                    Log.WriteError($"Tried to delete non-existing character named {name}");
                    return false;
                }

                rc.Characters.Remove(ch);

                try
                {
                    rc.SaveChanges();
                }
                catch (DataException ex)
                {
                    Log.WriteError(ex.ToString());
                    return false;
                }
                return true;
            }
        }

        public bool SaveCarPos(int channelId, Vector4 pos, int cityId, int posState)
        {
            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long) CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for SaveCarPos");
                    return false;
                }

                ch.PosX = pos.X;
                ch.PosY = pos.Y;
                ch.PosZ = pos.Z;
                ch.City = cityId;
                bool hasPosition = pos.X != 0f || pos.Y != 0f || pos.Z != 0f;
                ch.PosState = (posState == 0 && hasPosition) ? 1 : posState; // ja jogou: sem introducao de novo
                rc.SaveChanges();

                // Tambem na memoria: sem isso, ao reentrar na cidade sem passar pelo lobby, a posicao era a antiga.
                Position = new Vector4(pos.X, pos.Y, pos.Z, pos.Rotation);
                City = cityId;
                PosState = ch.PosState;
            }
            return true;
        }

        public static List<Character> Retrieve(ulong uid)
        {
            long ownerId = (long) uid;
            string upped = RiceServer.Config.UppedProfileName;

            using (var rc = Database.GetContext())
            {
                var list = rc.Characters.Where(c => c.UID == ownerId).ToList()
                    .Select(ch => new Character(ch)).ToList();

                foreach (var c in list)
                    c.EnsureMinimumLevel();

                // O perfil upado sempre aparece no 1o slot; os demais por ordem de criacao.
                return list
                    .OrderBy(c => string.Equals(c.Name, upped, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(c => c.CID)
                    .ToList();
            }
        }

        public int[] StatProbe = new int[19];   // !!stat: valores de teste depois dos totais do StatUpdate

        // Enhancers de uso (Speed/Accel/Dura. Enhancer): +valor no atributo ate o tempo acabar. 0 speed, 1 accel, 2 crash, 3 boost
        private readonly int[] buffValue = new int[4];
        private readonly DateTime[] buffUntil = new DateTime[4];
        public void AddStatBuff(int stat, int value, uint seconds)
        {
            if (stat < 0 || stat > 3) return;
            lock (buffValue) { buffValue[stat] = value; buffUntil[stat] = DateTime.UtcNow.AddSeconds(seconds); }
        }
        int BuffOf(int stat)
        {
            lock (buffValue) return DateTime.UtcNow < buffUntil[stat] ? buffValue[stat] : 0;
        }

        public StatUpdate GetStatUpdate()
        {
            var vehicleEntry = Vehicle.VehicleUpgradeEntry;
            var equipped = Inventory
                .Where(item => item.itemEntry is ItemTableEntry && item.curCarID == Vehicle.CarID)
                .ToList();

            int equipAccel = 0;
            int equipDura = 0;
            int equipSpeed = 0;
            int equipBoost = 0;
            double assistKmh = 0;   // "Maximum Speed Increase" do Assist Option (speedup, km/h)

            Action<string, double> addStat = (category, value) =>
            {
                switch (category)
                {
                    case "accel": equipAccel += (int) Math.Round(value); break;
                    case "crash": equipDura += (int) Math.Round(value); break;
                    case "speed": equipSpeed += (int) Math.Round(value); break;
                    case "boost": equipBoost += (int) Math.Round(value); break;
                }
            };

            foreach (var it in equipped)
            {
                var item = (ItemTableEntry) it.itemEntry;
                if (item.Category.StartsWith("dock_"))
                {
                    // Dock: "Parts Point After Docking +X%" sobre a peca do mesmo tipo no mesmo slot.
                    string partCat = item.Category == "dock_S" ? "speed" : item.Category == "dock_A" ? "accel"
                        : item.Category == "dock_C" ? "crash" : item.Category == "dock_B" ? "boost" : null;
                    if (partCat == null)
                        continue;
                    if (RiceServer.Config.DockPointRoll)
                    {
                        // pontos sorteados pela letra, direto no tipo do dock (nao precisa de peca no quadrado)
                        Rice.Game.PartCrafting.FixDock(this, it);
                        addStat(partCat, it.upgradePoint);
                        // Convert no dock (opcoes A/B): Speed/Accel/Dura/Boost +N somam aqui; mph e booster o jogo aplica
                        foreach (uint da in new[] { it.assistA, it.assistB })
                        {
                            if (da == 0 || da > AssistTable.Assists.Count) continue;
                            var dopt = AssistTable.Assists[(int) da - 1];
                            double dv;
                            if (!double.TryParse(dopt.Stat, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out dv)) continue;
                            if (dopt.Function == "bonusspeed") addStat("speed", dv);
                            else if (dopt.Function == "bonusaccel") addStat("accel", dv);
                            else if (dopt.Function == "bonuscrash") addStat("crash", dv);
                            else if (dopt.Function == "bonusboost") addStat("boost", dv);
                        }
                        continue;
                    }
                    var host = equipped.FirstOrDefault(o => o.slot == it.slot && o.itemEntry.Category == partCat);
                    double percent = 0;
                    int idx = AssistTable.GetIndex(item.PartAssist ?? "");
                    if (it.upgradePoint > 0)
                        percent = it.upgradePoint;   // bonus sorteado no Combine com Seed
                    else if (idx > 0)
                        double.TryParse(AssistTable.Assists[idx - 1].Stat, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out percent);
                    if (host != null)
                        addStat(partCat, ((ItemTableEntry) host.itemEntry).BasePoints * percent / 100.0);
                    continue;
                }

                // Option Part (op_S/op_A/op_C/op_B) no quadrado de uma peca: os pontos dela somam no tipo
                string cat = item.Category == "op_S" ? "speed" : item.Category == "op_A" ? "accel"
                           : item.Category == "op_C" ? "crash" : item.Category == "op_B" ? "boost" : item.Category;
                addStat(cat, item.BasePoints + it.upgradePoint);   // upgradePoint = pontos do Modify

                // assist da propria option (ex.: Engine Speeder B = a_00032 speedup 2 km/h)
                int pa = AssistTable.GetIndex(item.PartAssist ?? "");
                if (pa > 0)
                {
                    var pas = AssistTable.Assists[pa - 1];
                    assistKmh += AssistKmh(pas);
                    // pecas de "Crew"/party (Party Engine Speeder, Party Crash Guard...): valem para voce tambem
                    double pv;
                    if (pas != null && double.TryParse(pas.Stat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pv))
                    {
                        if (pas.Function == "partyspeedup") assistKmh += pv;
                        else if (pas.Function == "partycrashup") addStat("crash", pv);
                    }
                }

                // Opcoes (Convert etc.) gravadas na peca: Speed +N, Acceleration +N...
                uint[] assists = { it.assistA, it.assistB, it.assistC, it.assistD, it.assistE,
                                   it.assistF, it.assistG, it.assistH, it.assistI, it.assistJ };
                foreach (uint a in assists)
                {
                    if (a == 0 || a > AssistTable.Assists.Count) continue;
                    var opt = AssistTable.Assists[(int) a - 1];
                    double v;
                    if (!double.TryParse(opt.Stat, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                    if (opt.Function == "speedup" || opt.Function == "partyspeedup") assistKmh += v;
                    else if (opt.Function == "partycrashup") addStat("crash", v);
                    else if (opt.Function == "bonusspeed") addStat("speed", v);
                    else if (opt.Function == "bonusaccel") addStat("accel", v);
                    else if (opt.Function == "bonuscrash") addStat("crash", v);
                    else if (opt.Function == "bonusboost") addStat("boost", v);
                }
            }

            // docks de nivel com a fileira completa: +DockRowBonus em cada atributo por fileira
            int rows = Rewards.CompleteDockRows(this, Vehicle.CarID).Count;
            if (rows > 0)
            {
                int rb = RiceServer.Config.DockRowBonus * rows;
                equipSpeed += rb; equipAccel += rb; equipDura += rb; equipBoost += rb;
            }

            equipSpeed += BuffOf(0); equipAccel += BuffOf(1); equipDura += BuffOf(2); equipBoost += BuffOf(3);

            // drift shop: bonus dos itens visuais no carro (speed, accel, boost, crash)
            var vb = Rice.Server.Packets.Game.VisualShop.BonusOn(this, Vehicle.CarID);
            equipSpeed += vb[0]; equipAccel += vb[1]; equipBoost += vb[2]; equipDura += vb[3];

            // o numero das pecas (ex.: Party Engine Speeder C "+3") vale como mph no Assist Option: o jogo guarda em km/h e
            // mostra convertido (3 km/h = 1,9 mph); com AssistSpeedAsMph o servidor manda 3 x 1,609 km/h = +3,0 mph
            if (RiceServer.Config.AssistSpeedAsMph) assistKmh *= 1.609344;

            // velocidade maxima extra pelos pontos de speed (SpeedBonusPerPoint mph por ponto, contando a partir de
            // SpeedBonusFromPoints): vai no campo "Maximum Speed Increase" (km/h). Ex.: 542 x 0,1 = +54 mph.
            {
                var cfg = RiceServer.Config;
                int totalSpeed = vehicleEntry.Speed + Level + equipSpeed;
                if (cfg.SpeedBonusPerPoint > 0 && totalSpeed > cfg.SpeedBonusFromPoints)
                    assistKmh += (totalSpeed - cfg.SpeedBonusFromPoints) * cfg.SpeedBonusPerPoint * 1.609344;
            }

            return new StatUpdate
            {
                BaseAcceleration = vehicleEntry.Acceleration,
                BaseDurability = vehicleEntry.Durability,
                BaseSpeed = vehicleEntry.Speed,
                BaseBoost = vehicleEntry.Boost,

                CharAcceleration = Level,
                CharDurability = Level,
                CharSpeed = Level,
                CharBoost = Level,

                EquipAcceleration = equipAccel,
                EquipDurability = equipDura,
                EquipSpeed = equipSpeed,
                EquipBoost = equipBoost,
                Extra = BuildExtra(assistKmh, vehicleEntry.Durability + equipDura + Level),
            };
        }

        static double AssistKmh(AssistTableEntry a)
        {
            double v;
            if (a == null || a.Function != "speedup") return 0;
            return double.TryParse(a.Stat, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        // Campos depois dos totais (achados com !!stat teste): 8-11 = pontos extras no Total (speed, dura, accel, boost),
        // 12 = Maximum Speed Increase (float, km/h; o painel mostra em mph). Com !!stat ligado, o teste vale por cima.
        int[] BuildExtra(double assistKmh, int totalDura)
        {
            var ex = (int[]) StatProbe.Clone();
            // campo 0 = HP maximo da Neo City (a barra "HP x / y" do lado das armas; o jogo enche a barra sozinho ate ele)
            if (ex[0] == 0 && RiceServer.Config.NeoHpEnabled)
                ex[0] = (int) Math.Max(1, Math.Round(Math.Max(0, totalDura) * RiceServer.Config.NeoHpPerDurability
                                                    + Level * RiceServer.Config.NeoHpPerLevel + RiceServer.Config.NeoHpBase));
            if (ex[12] == 0 && assistKmh > 0)
                ex[12] = BitConverter.ToInt32(BitConverter.GetBytes((float) assistKmh), 0);
            return ex;
        }
    }
}
