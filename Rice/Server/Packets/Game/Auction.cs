using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Rice.Game;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Leilao (Auction House) vivo do modo offline. Anuncios em auction.json.
    //   - bots anunciam pecas e itens (estoque renovado sozinho);
    //   - voce pode comprar os anuncios dos bots e anunciar os seus; bots compram os seus com o tempo.
    // Pacotes (cliente 2016, layouts da documentacao do openDC):
    //   580 MyList -> 581 ; 588 List -> 589 ; 595 Search -> 596 ; 582 Register -> 402 + 583
    //   584 Cancel -> 585 + 402 ; 586 Complete -> 587 + 402 ; 592 Buy -> 402 + 593 ; 594 aviso ao vendedor
    public static class Auction
    {
        static readonly string FilePath = Paths.World("auction.json");
        const int OnSale = 1, Sold = 2, Expired = 3;
        const ushort LockedState = 4;
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static Timer timer;

        public class Listing
        {
            public uint Serial;
            public string ItemId = "";
            public uint Stack = 1;
            public uint Price;
            public int State = OnSale;
            public int RegDate;
            public long SellerCid;
            public string SellerName = "";
            public bool FromPlayer;
            public uint InvIdx;           // anuncio do jogador: o item dele (fica bloqueado, estado 4)
            public int Upgrade, UpgradePoint;
            public uint AssistA, AssistB, AssistC;
            public string BuyerName = "";
            public bool IsCar;            // anuncio de carro (aba de carros do leilao)
            public int CarType, Grade;
            public uint Color;
            public float Fuel, Km;        // combustivel (L) e quilometragem do carro anunciado
        }

        public class Store
        {
            public uint NextSerial = 1000;
            public List<Listing> Listings = new List<Listing>();
        }

        static Store store;
        static DateTime lastTick = DateTime.UtcNow;

        static Store Data()
        {
            lock (Sync)
            {
                if (store == null)
                {
                    try { store = File.Exists(FilePath) ? JsonConvert.DeserializeObject<Store>(File.ReadAllText(FilePath)) : null; }
                    catch (Exception ex) { Log.WriteError("auction.json invalido: " + ex.Message); }
                    if (store == null) store = new Store();
                    if (store.Listings == null) store.Listings = new List<Listing>();
                    // carros dos bots: preco da loja (V do carro na loja, mais 30% por V acima), com o desconto de km/combustivel
                    int rp = 0;
                    foreach (var l in store.Listings.Where(x => x.IsCar && !x.FromPlayer && RiceServer.Config.AuctionCarShopPrice))
                    {
                        double v = CarValue(l.CarType, l.Grade, l.Km, l.Fuel);
                        if (v <= 0) continue;
                        uint np = RoundPrice(v);
                        if (np != l.Price) { l.Price = np; rp++; }
                    }
                    if (rp > 0) Log.WriteLine("Leilao: {0} carro(s) com o preco novo da loja.", rp);
                    // pecas e itens dos bots: preco novo (loja x multiplicador, nivel, +N, qualidade, Convert; cupom bem caro)
                    int ri = 0;
                    foreach (var l in store.Listings.Where(x => !x.IsCar && !x.FromPlayer))
                    {
                        var e = Entry(l.ItemId);
                        if (e == null) continue;
                        double cv = CouponValue(l.ItemId);
                        string cat = e.Category ?? "";
                        bool conv = (cat.StartsWith("op_") || cat.StartsWith("dock_")) && l.AssistA != 0 && l.AssistB != 0;
                        double v = cv > 0 ? cv : ItemValue(e, l.Upgrade, l.UpgradePoint, conv) * Math.Max(1u, l.Stack);
                        if (v <= 0) continue;
                        uint np = RoundPrice(v * (1.0 + (l.Serial % 5) * 0.1));
                        if (np != l.Price) { l.Price = np; ri++; }
                    }
                    if (ri > 0) Log.WriteLine("Leilao: {0} item(ns) com o preco novo.", ri);
                }
                return store;
            }
        }

        static void Save()
        {
            lock (Sync)
            {
                try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(store, Formatting.Indented)); }
                catch (Exception ex) { Log.WriteError("Nao consegui gravar auction.json: " + ex.Message); }
            }
        }

        public static void Start()
        {
            lock (Sync)
            {
                int old = Data().Listings.RemoveAll(l => l.IsCar && !l.FromPlayer && l.Fuel <= 0 && l.Km <= 0);
                if (old > 0) Log.WriteLine("Leilao: {0} carro(s) antigos (sem km/combustivel) trocados.", old);
            }
            Tick(0);
            if (timer == null)
                timer = new Timer(_ =>
                {
                    try { Tick(1); }
                    catch (Exception ex) { Log.WriteError("Leilao: " + ex.Message); }
                }, null, 60000, 60000);
            Log.WriteLine("Leilao: {0} anuncio(s) ativos.", Data().Listings.Count(l => l.State == OnSale));
        }

        static int Now() { return (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

        // ---- tabela de itens ----------------------------------------------------------------------------------

        static int TableIdx(string id) { return ItemTable.Items.FindIndex(i => i.ID == id); }

        static GenericItemEntry Entry(string id) { return ItemTable.Items.FirstOrDefault(i => i.ID == id); }

        static uint ShopValue(GenericItemEntry e)
        {
            uint v;
            return e != null && uint.TryParse(e.BuyValue, out v) ? v : 0;
        }

        // valor de referencia no leilao: preco da loja x AuctionItemPriceMultiplier, mais por nivel da peca (pecas de nivel
        // alto valem mais), por +N, por ponto de qualidade e por Convert
        static double ItemValue(GenericItemEntry e, int upgrade, int quality, bool converted)
        {
            var cfg = RiceServer.Config;
            double v = ShopValue(e) * cfg.AuctionItemPriceMultiplier;
            v *= 1 + cfg.AuctionLevelBonus * Math.Max(0, LevelOf(e)) / 100.0;
            v *= 1 + cfg.AuctionUpgradeBonus * Math.Max(0, upgrade) + cfg.AuctionQualityBonus * Math.Max(0, quality);
            if (converted) v *= cfg.AuctionConvertedMultiplier;
            return v;
        }

        // cupom de carro (Coupon Book): vale o preco do carro na loja x AuctionCouponPriceFactor (bem caro)
        static double CouponValue(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            var c = Coupons.Table().FirstOrDefault(x => x.item == itemId && x.car > 0);
            VehicleListEntry v;
            List<VehicleUpgradeEntry> ups;
            if (c == null || !VehicleTable.Vehicles.TryGetValue(c.car, out v) || v == null
                || !VehicleTable.VehicleUpgrades.TryGetValue(c.car, out ups) || ups.Count < Math.Max(1, v.Level)) return 0;
            return Math.Max(1, ups[Math.Max(1, v.Level) - 1].Price) * RiceServer.Config.AuctionCouponPriceFactor;
        }

        static GenericItemEntry RandomCoupon()
        {
            var ids = Coupons.Table().Where(x => !string.IsNullOrEmpty(x.item) && x.car > 0 && TableIdx(x.item) >= 0)
                                     .Select(x => x.item).Distinct().ToList();
            return ids.Count == 0 ? null : Entry(ids[Rng.Next(ids.Count)]);
        }

        static int LevelOf(GenericItemEntry e)
        {
            var p = e as ItemTableEntry;
            int lv;
            return p != null && int.TryParse(p.MinLevel, out lv) ? lv : 0;
        }

        static readonly Dictionary<string, int> Tabs = new Dictionary<string, int>
        {
            { "speed", 1 }, { "crash", 2 }, { "accel", 3 }, { "boost", 4 },
            { "op_S", 6 }, { "op_C", 7 }, { "op_A", 8 }, { "op_B", 9 }, { "op_F", 10 },
            { "dock_S", 11 }, { "dock_C", 12 }, { "dock_A", 13 }, { "dock_B", 14 }
        };

        static int TabOf(GenericItemEntry e)
        {
            int t;
            return e is ItemTableEntry && Tabs.TryGetValue(e.Category ?? "", out t) ? t : 5;
        }

        static readonly HashSet<string> UseCategories = new HashSet<string>
        {
            "upgrade", "repair", "turbosave", "accelpointup", "speedpointup", "crashpointup", "fuel", "fuelsave",
            "trans", "craft_up", "craft_up_plus", "mitrondust", "new_repair", "battle_quick", "under_quick",
            "heattreatment", "piece", "partsgrinder", "fullchance_upgrade", "double_upgrade"
        };

        static List<KeyValuePair<GenericItemEntry, double>> pool;

        static List<KeyValuePair<GenericItemEntry, double>> Pool()
        {
            if (pool != null) return pool;
            pool = new List<KeyValuePair<GenericItemEntry, double>>();
            foreach (var e in ItemTable.Items)
            {
                if (!string.Equals(e.Auctionable, "True", StringComparison.OrdinalIgnoreCase) || ShopValue(e) == 0) continue;
                var part = e as ItemTableEntry;
                if (part != null)
                {
                    if (!Tabs.ContainsKey(e.Category ?? "")) continue;
                    string g = (part.Grade ?? "").ToUpperInvariant();
                    char last = g.Length > 0 ? g[g.Length - 1] : 'N';
                    double w = last == 'N' ? 5 : last == 'S' ? 3 : last == 'H' ? 1 : last == 'U' ? 0.25 : 0;
                    if (w > 0) pool.Add(new KeyValuePair<GenericItemEntry, double>(e, w));
                }
                else if (UseCategories.Contains(e.Category ?? ""))
                    pool.Add(new KeyValuePair<GenericItemEntry, double>(e, 2.5));
            }
            Log.WriteLine("Leilao: {0} itens possiveis para os bots anunciarem.", pool.Count);
            return pool;
        }

        // Item aleatorio para recompensas (cartas "?" do fim da corrida): ate o nivel informado.
        // um anuncio de bot a venda (para o chat dos bots): nome do item, preco, vendedor
        public static bool RandomBotAd(Random rng, out string item, out uint price, out string seller)
        {
            item = null; price = 0; seller = null;
            lock (Sync)
            {
                var l = Data().Listings.Where(x => x.State == OnSale && !x.FromPlayer).ToList();
                if (l.Count == 0) return false;
                var x0 = l[rng.Next(l.Count)];
                item = ListingName(x0); price = x0.Price; seller = x0.SellerName;
                return !string.IsNullOrEmpty(item);
            }
        }

        // ---- negocios anunciados no chat pelos bots (BotChatter): desconto de verdade, reserva para o jogador ----
        public class Deal
        {
            public uint Serial; public string Name = ""; public uint Price, OldPrice; public string Seller = "";
            public bool IsCar; public int CarType, Grade; public string ItemId = "";
        }
        static readonly Dictionary<uint, DateTime> held = new Dictionary<uint, DateTime>();   // reservado para o jogador ate

        public static bool IsHeld(uint serial)
        {
            lock (held)
            {
                DateTime t;
                if (!held.TryGetValue(serial, out t)) return false;
                if (DateTime.UtcNow < t) return true;
                held.Remove(serial); return false;
            }
        }

        public static void Hold(uint serial, double seconds) { lock (held) held[serial] = DateTime.UtcNow.AddSeconds(seconds); }

        // escolhe um anuncio de bot e baixa o preco (desconto de verdade no leilao)
        public static Deal MakeDeal(Random rng, int minOff, int maxOff)
        {
            Deal d = null;
            lock (Sync)
            {
                var l = Data().Listings.Where(x => x.State == OnSale && !x.FromPlayer && !IsHeld(x.Serial) && Bots.ByName(x.SellerName) != null
                                                  && !string.IsNullOrEmpty(ListingName(x))).ToList();
                if (l.Count == 0) return null;
                var x0 = l[rng.Next(l.Count)];
                uint old = x0.Price;
                uint price = (uint) Math.Max(1, old * (100 - rng.Next(minOff, maxOff + 1)) / 100);
                x0.Price = price;
                d = new Deal { Serial = x0.Serial, Name = ListingName(x0), Price = price, OldPrice = old, Seller = x0.SellerName,
                               IsCar = x0.IsCar, CarType = x0.CarType, Grade = x0.Grade, ItemId = x0.ItemId };
            }
            Save();
            Log.WriteLine("Leilao: {0} baixou {1} de {2} para {3} mito.", d.Seller, d.Name, d.OldPrice, d.Price);
            return d;
        }

        public static Deal GetDeal(uint serial)
        {
            lock (Sync)
            {
                var x0 = Data().Listings.FirstOrDefault(x => x.Serial == serial && x.State == OnSale);
                if (x0 == null) return null;
                return new Deal { Serial = x0.Serial, Name = ListingName(x0), Price = x0.Price, OldPrice = x0.Price, Seller = x0.SellerName,
                                  IsCar = x0.IsCar, CarType = x0.CarType, Grade = x0.Grade, ItemId = x0.ItemId };
            }
        }

        public static bool OnSaleNow(uint serial)
        {
            lock (Sync) return Data().Listings.Any(x => x.Serial == serial && x.State == OnSale);
        }

        public static int ItemLevel(string id) { var e = Entry(id); return e != null ? LevelOf(e) : 0; }

        // um bot compra o anuncio (tira do leilao). Retorna false se ja foi vendido ou esta reservado.
        public static bool BotTake(uint serial, Bots.BotProfile buyer)
        {
            Listing l;
            lock (Sync)
            {
                l = Data().Listings.FirstOrDefault(x => x.Serial == serial && x.State == OnSale && !x.FromPlayer);
                if (l == null || IsHeld(serial)) return false;
                Data().Listings.Remove(l);
            }
            Save();
            var seller = Bots.ByCid(l.SellerCid) ?? Bots.ByName(l.SellerName);
            if (seller != null) seller.Sold++;
            if (buyer != null) buyer.Bought++;
            Bots.Save();
            Log.WriteLine("Leilao: {0} comprou {1} de {2} por {3}.", buyer != null ? buyer.Name : "?", ListingName(l), l.SellerName, l.Price);
            return true;
        }

        // carta "?" do fim da corrida: peca, option ou dock do grau do seu nivel - as do nivel mais alto que voce ja pode
        // usar (ate 12 niveis abaixo dele) em cada tipo; nivel 1-30 sai N, 31-59 S, 60-89 H e 90+ U. As vezes (15%) um
        // item de uso (reparo, upgrade...), como antes.
        static readonly string[] CardCats = { "speed", "crash", "accel", "boost", "op_S", "op_C", "op_A", "op_B", "op_F", "dock_S", "dock_C", "dock_A", "dock_B" };
        static Dictionary<string, List<ItemTableEntry>> cardParts;
        public static GenericItemEntry CardReward(int level)
        {
            level = Math.Max(1, level);
            if (Rng.NextDouble() < 0.15)
            {
                var use = Pool().Where(x => !(x.Key is ItemTableEntry)).ToList();
                if (use.Count > 0) return use[Rng.Next(use.Count)].Key;
            }
            if (cardParts == null)
            {
                cardParts = new Dictionary<string, List<ItemTableEntry>>();
                foreach (var cat in CardCats)
                    cardParts[cat] = ItemTable.Items.OfType<ItemTableEntry>()
                        .Where(e => e.Category == cat && LevelOf(e) > 0 && !string.IsNullOrEmpty(e.Name) && e.Name != "n/a" && (e.ID ?? "").Length > 0).ToList();
            }
            var picks = new List<ItemTableEntry>();
            foreach (var kv in cardParts)
            {
                var all = kv.Value.Where(e => LevelOf(e) <= level).ToList();
                if (all.Count == 0) continue;
                // o grau da faixa do nivel (U > H > S > N); sem esse grau no tipo, o de baixo
                string order = "NSHU";
                int band = level >= 90 ? 3 : level >= 60 ? 2 : level >= 31 ? 1 : 0;
                List<ItemTableEntry> can = null;
                for (int g = band; g >= 0 && (can == null || can.Count == 0); g--)
                    can = all.Where(e => { var gr = (e.Grade ?? "").ToUpperInvariant(); return gr.Length > 0 && gr[gr.Length - 1] == order[g]; }).ToList();
                if (can == null || can.Count == 0) continue;
                int top = can.Max(LevelOf);
                picks.AddRange(can.Where(e => LevelOf(e) >= top - 12));
            }
            if (picks.Count == 0) return RewardItem(level);
            return picks[Rng.Next(picks.Count)];
        }

        public static GenericItemEntry RewardItem(int level)
        {
            for (int i = 0; i < 12; i++)
            {
                var e = PickItem();
                if (e == null) return null;
                if (LevelOf(e) <= Math.Max(1, level)) return e;
            }
            return PickItem();
        }

        static GenericItemEntry PickItem()
        {
            var p = Pool();
            if (p.Count == 0) return null;
            double total = p.Sum(x => x.Value), r = Rng.NextDouble() * total;
            foreach (var x in p)
            {
                r -= x.Value;
                if (r <= 0) return x.Key;
            }
            return p[p.Count - 1].Key;
        }

        static uint RoundPrice(double v)
        {
            v = Math.Max(10, v);
            double step = v >= 10000 ? 100 : v >= 1000 ? 50 : 5;
            return (uint) (Math.Round(v / step) * step);
        }

        static Listing NewBotListing()
        {
            var e = PickItem();
            // as vezes um bot anuncia um cupom de carro (bem caro)
            if (Rng.NextDouble() < RiceServer.Config.AuctionCouponChance) e = RandomCoupon() ?? e;
            if (e == null) return null;
            var seller = Bots.Pick(b => b.Personality == "comerciante") ?? Bots.Pick();
            if (seller == null) return null;
            var l = new Listing
            {
                Serial = Data().NextSerial++, ItemId = e.ID, SellerCid = seller.Cid, SellerName = seller.Name,
                RegDate = Now() - Rng.Next(0, Math.Max(1, RiceServer.Config.AuctionDays - 1) * 86400)
            };
            double factor = seller.Personality == "comerciante" ? 1.1 + Rng.NextDouble() * 0.7 : 0.6 + Rng.NextDouble() * 0.7;
            double value = ShopValue(e);
            bool converted = false;
            string ecat = e.Category ?? "";
            if (e is ItemTableEntry && (ecat.StartsWith("op_") || ecat.StartsWith("dock_")))
            {
                // Option Part e Dock nao sobem de nivel no jogo: sem +N no leilao (os pontos sao sorteados ao comprar)
                l.Upgrade = 0; l.UpgradePoint = 0;
                // ... mas podem vir com Convert (2 opcoes)
                int a1, a2;
                if (Rng.NextDouble() < RiceServer.Config.AuctionConvertedChance
                    && Rice.Game.PartCrafting.RandomOptionPair((ItemTableEntry) e, Rng.NextDouble() < 0.3, out a1, out a2))
                {
                    l.AssistA = (uint) a1; l.AssistB = (uint) a2;
                    converted = true;
                }
                value = ItemValue(e, 0, 0, converted);
            }
            else if (e is ItemTableEntry)
            {
                l.Upgrade = Rng.NextDouble() < 0.35 ? Rng.Next(1, 4) : 0;
                l.UpgradePoint = l.Upgrade == 0 ? 0 : Rng.Next(l.Upgrade, l.Upgrade * 3 + 1);
                value = ItemValue(e, l.Upgrade, l.UpgradePoint, false);
            }
            else
            {
                value = ItemValue(e, 0, 0, false);
                uint max = 1;
                try { max = Math.Max(1u, e.GetMaxStack()); } catch (Exception) { max = 1; }
                if (e.IsStackable() && max > 1) l.Stack = (uint) Rng.Next(1, (int) Math.Min(max, 10) + 1);
            }
            double cv = CouponValue(e.ID);
            if (cv > 0) { value = cv; l.Stack = 1; l.Upgrade = 0; l.UpgradePoint = 0; }
            l.Price = RoundPrice(value * factor * l.Stack);
            seller.Reg++;
            return l;
        }

        static int ListingLevel(Listing l) { return l.IsCar ? l.Grade : LevelOf(Entry(l.ItemId)); }

        static string ListingName(Listing l)
        {
            if (l.IsCar) return Bots.CarName(l.CarType);
            var e = Entry(l.ItemId);
            return e != null ? e.Name ?? "" : "";
        }

        static List<int> carPool;

        static List<int> CarPool()
        {
            if (carPool != null) return carPool;
            carPool = new List<int>();
            foreach (var kv in VehicleTable.Vehicles)
            {
                var v = kv.Value;
                List<VehicleUpgradeEntry> ups;
                if (v == null || v.Level < 1 || !VehicleTable.VehicleUpgrades.TryGetValue(kv.Key, out ups) || ups.Count < v.Level) continue;
                if (ups[v.Level - 1].Price <= 0 || TableIdx(v.GetKeyId()) < 0) continue;
                if (v.Sellable != 1 && !v.IsSpecialCategory()) continue;
                if (Bots.Unsupported(kv.Key)) continue;
                carPool.Add(kv.Key);
            }
            Log.WriteLine("Leilao: {0} carros possiveis para os bots anunciarem.", carPool.Count);
            return carPool;
        }

        // valor do carro no leilao: preco da loja (no V de venda) x AuctionCarPriceMultiplier, +30% por V acima, menos
        // ate AuctionCarKmDiscount pelo km e AuctionCarFuelDiscount pelo tanque vazio
        static double CarValue(int carType, int grade, float km, float fuel)
        {
            VehicleListEntry v;
            List<VehicleUpgradeEntry> ups;
            if (!VehicleTable.Vehicles.TryGetValue(carType, out v) || v == null || !VehicleTable.VehicleUpgrades.TryGetValue(carType, out ups) || ups.Count == 0) return 0;
            var cfg = RiceServer.Config;
            int lv = Math.Max(1, Math.Min(v.Level, ups.Count));
            grade = Math.Max(lv, Math.Min(grade, ups.Count));
            int basePrice = ups[lv - 1].Price;
            if (basePrice <= 0) return 0;
            double value = basePrice * (1 + 0.3 * (grade - lv)) * cfg.AuctionCarPriceMultiplier;
            float capacity = Rice.Game.Vehicle.ScaledCapacity(ups[grade - 1]);
            if (capacity <= 0) capacity = 100f;
            double kmMax = Math.Max(1, cfg.AuctionCarMaxKm);
            double fuelRatio = Math.Max(0, Math.Min(1, fuel / capacity));
            return value * (1 - cfg.AuctionCarKmDiscount * Math.Min(km / kmMax, 1.0)) * (1 - cfg.AuctionCarFuelDiscount * (1 - fuelRatio));
        }

        static Listing NewCarListing(Bots.BotProfile seller, int carType, int grade)
        {
            VehicleListEntry v;
            List<VehicleUpgradeEntry> ups;
            if (seller == null || !VehicleTable.Vehicles.TryGetValue(carType, out v) || v == null
                || !VehicleTable.VehicleUpgrades.TryGetValue(carType, out ups) || ups.Count == 0) return null;
            grade = Math.Max(v.Level, Math.Min(grade, ups.Count));
            int basePrice = ups[Math.Max(v.Level, 1) - 1].Price;
            if (basePrice <= 0) basePrice = 500000;
            double value = basePrice * (1 + 0.3 * (grade - v.Level)) * RiceServer.Config.AuctionCarPriceMultiplier;
            double factor = seller.Special ? 1.0 : seller.Personality == "comerciante" ? 1.1 + Rng.NextDouble() * 0.5 : 0.8 + Rng.NextDouble() * 0.5;

            // Estado do carro: quilometragem e combustivel no tanque agora. Mais km e menos combustivel = um pouco
            // mais barato; pouco km e tanque cheio = mais caro (no maximo ~-20% / +0%).
            var cfg = RiceServer.Config;
            float capacity = Rice.Game.Vehicle.ScaledCapacity(ups[grade - 1]);
            if (capacity <= 0) capacity = 100f;
            double kmMax = Math.Max(1, cfg.AuctionCarMaxKm);
            double km = Math.Pow(Rng.NextDouble(), 1.6) * (seller.Special ? kmMax * 0.15 : kmMax);
            double fuelRatio = 0.1 + Rng.NextDouble() * 0.9;
            if (Rng.NextDouble() < 0.25) fuelRatio = 1.0;
            double condition = (1 - cfg.AuctionCarKmDiscount * Math.Min(km / kmMax, 1.0))
                             * (1 - cfg.AuctionCarFuelDiscount * (1 - fuelRatio));
            seller.Reg++;
            return new Listing
            {
                Serial = Data().NextSerial++, IsCar = true, CarType = carType, Grade = grade, ItemId = v.GetKeyId(),
                Color = (uint) Rng.Next(1, 40), SellerCid = seller.Cid, SellerName = seller.Name,
                RegDate = Now() - (seller.Special ? 0 : Rng.Next(0, Math.Max(1, cfg.AuctionDays - 1) * 86400)),
                Km = (float) Math.Round(km, 1), Fuel = (float) Math.Round(capacity * fuelRatio, 1),
                Price = RoundPrice(RiceServer.Config.AuctionCarShopPrice ? value * condition : value * factor * condition)
            };
        }

        static void StockCars(Store d, ref bool changed)
        {
            var cfg = RiceServer.Config;
            // bots fixos (Marsteh, Brunizin.X...): sempre com o(s) carro(s) deles a venda, no grau 9
            foreach (var b in Bots.All().Where(x => x.Special && x.CarsForSale != null).ToList())
                foreach (var car in b.CarsForSale)
                    if (!d.Listings.Any(x => x.IsCar && x.State == OnSale && x.SellerCid == b.Cid && x.CarType == car))
                    {
                        var l = NewCarListing(b, car, 9);
                        if (l != null) { d.Listings.Add(l); changed = true; }
                    }
            var pool = CarPool();
            int stock = d.Listings.Count(x => x.IsCar && x.State == OnSale && !x.FromPlayer);
            for (int i = stock; i < cfg.AuctionCarStock && pool.Count > 0; i++)
            {
                int car = pool[Rng.Next(pool.Count)];
                VehicleListEntry v = VehicleTable.Vehicles[car];
                int grade = v.Level + (Rng.NextDouble() < 0.3 ? Rng.Next(1, 3) : 0);
                var seller = Bots.Pick(x => !x.Special && x.Level >= Rice.Game.Character.GetRequiredLevelForTier(v.Level)) ?? Bots.Pick(x => !x.Special);
                var l = NewCarListing(seller, car, grade);
                if (l == null) break;
                d.Listings.Add(l);
                changed = true;
            }
        }

        // ---- quanto tempo um bot leva para comprar o anuncio do jogador ------------------------------------------
        // Referencias: a LOJA (preco de compra; cupom = preco do carro) e o LEILAO (o anuncio de bot mais barato do mesmo
        // item, por unidade; sem anuncio, o valor de referencia do leilao). Abaixo da loja ou do leilao: vende em poucos
        // minutos. Caro: demora; mais caro: demora mais (nunca e impossivel, so fica raro). Item muito valioso tem menos
        // compradores: a chance cai um pouco conforme o valor.
        // tier: "loja", "leilao", "justo", "alto", "caro", "muito caro"
        static double BuyChance(Store d, Listing l, out string tier)
        {
            var cfg = RiceServer.Config;
            uint n = Math.Max(1u, l.Stack);
            var e = Entry(l.ItemId);
            double cvp = CouponValue(l.ItemId);
            double value = (cvp > 0 ? cvp : ItemValue(e, l.Upgrade, l.UpgradePoint, l.AssistA != 0 && l.AssistB != 0)) * n;
            if (value <= 0) value = 1000;
            double shop = cvp > 0 ? cvp / Math.Max(0.01, cfg.AuctionCouponPriceFactor) : ShopValue(e) * (double) n;
            double market = 0;
            foreach (var o in d.Listings)
                if (!o.FromPlayer && !o.IsCar && o.State == OnSale && o.ItemId == l.ItemId)
                {
                    double u = o.Price / (double) Math.Max(1u, o.Stack) * n;
                    if (market <= 0 || u < market) market = u;
                }
            double reference = market > 0 ? market : value;
            double price = l.Price, ratio = price / reference, chance;
            if (shop > 0 && price <= shop) { tier = "loja"; chance = cfg.AuctionBuyChanceBargain; }
            else if (ratio <= 1.0) { tier = "leilao"; chance = cfg.AuctionBuyChanceCheap; }
            else if (ratio <= 1.5) { tier = "justo"; chance = cfg.AuctionBuyChanceFair; }
            else if (ratio <= 2.5) { tier = "alto"; chance = cfg.AuctionBuyChancePricey; }
            else if (ratio <= 5.0) { tier = "caro"; chance = cfg.AuctionBuyChanceExpensive; }
            else { tier = "muito caro"; chance = cfg.AuctionBuyChanceVeryExpensive * 5.0 / ratio; }
            double rich = Math.Max(0, Math.Log10(Math.Max(1, value) / Math.Max(1, cfg.AuctionRareItemValue)));
            return Math.Max(0, Math.Min(1, chance / (1 + rich)));
        }

        // ---- simulacao (1 vez por minuto) ----------------------------------------------------------------------

        static void Tick(int minutes)
        {
            var cfg = RiceServer.Config;
            if (!cfg.AuctionEnabled) return;
            var notices = new List<KeyValuePair<long, Listing>>();
            bool changed = false;
            lock (Sync)
            {
                var d = Data();
                int now = Now(), life = Math.Max(1, cfg.AuctionDays) * 86400;

                foreach (var l in d.Listings.Where(x => x.State == OnSale && now - x.RegDate >= life).ToList())
                {
                    if (l.FromPlayer) l.State = Expired;
                    else d.Listings.Remove(l);
                    changed = true;
                }

                if (minutes > 0)
                {
                    // bots compram anuncios de outros bots (o estoque gira)
                    foreach (var l in d.Listings.Where(x => !x.FromPlayer && x.State == OnSale && !IsHeld(x.Serial)).ToList())
                        if (Rng.NextDouble() < cfg.AuctionBotTurnover * minutes)
                        {
                            d.Listings.Remove(l);
                            changed = true;
                        }

                    // bots compram os anuncios do jogador: a chance depende do preco comparado a loja e ao leilao (BuyChance)
                    foreach (var l in d.Listings.Where(x => x.FromPlayer && x.State == OnSale))
                    {
                        string tier;
                        double p = BuyChance(d, l, out tier);
                        if (p > 0 && Rng.NextDouble() < 1 - Math.Pow(1 - p, minutes))
                        {
                            var buyer = Bots.Pick(b => b.Cid != l.SellerCid);
                            l.State = Sold;
                            l.BuyerName = buyer != null ? buyer.Name : "alguem";
                            if (buyer != null) buyer.Bought++;
                            notices.Add(new KeyValuePair<long, Listing>(l.SellerCid, l));
                            changed = true;
                        }
                    }
                }

                int stock = d.Listings.Count(x => !x.FromPlayer && !x.IsCar && x.State == OnSale);
                for (int i = stock; i < cfg.AuctionBotStock; i++)
                {
                    var l = NewBotListing();
                    if (l == null) break;
                    d.Listings.Add(l);
                    changed = true;
                }
                StockCars(d, ref changed);
            }
            if (changed)
            {
                Save();
                if (notices.Count > 0) Bots.Save();
            }

            foreach (var n in notices)
            {
                var c = Bots.ClientOf(n.Key);
                var e = Entry(n.Value.ItemId);
                Log.WriteLine("Leilao: {0} comprou o anuncio {1} ({2}) por {3}.", n.Value.BuyerName, n.Value.Serial, n.Value.ItemId, n.Value.Price);
                if (c == null) continue;
                try
                {
                    var p = new RicePacket(594);
                    p.Writer.Write(n.Value.Serial);
                    c.Send(p);
                    Chat.SendSystemMessage(c, string.Format("[Leilao] {0} comprou seu {1} por {2} mito. Abra 'Meus anuncios' e clique em Completar para receber.",
                        n.Value.BuyerName, e != null ? e.Name : n.Value.ItemId, n.Value.Price));
                }
                catch (Exception ex) { Log.WriteError("Leilao aviso: " + ex.Message); }
            }
        }

        // ---- registros do pacote ------------------------------------------------------------------------------

        static byte[] WStr(string s, int bytes)
        {
            var r = new byte[bytes];
            var e = Encoding.Unicode.GetBytes(s ?? "");
            Array.Copy(e, r, Math.Min(e.Length, bytes - 2));
            return r;
        }

        static int DaysLeft(Listing l)
        {
            int left = RiceServer.Config.AuctionDays - (Now() - l.RegDate) / 86400;
            return Math.Max(0, left);
        }

        // XiStrAuctionUnit (28): Serial, TableIdx, InvenIdx, Price, State, RegDate, dias restantes
        static void WriteUnit(BinaryWriter w, Listing l)
        {
            w.Write(l.Serial);
            w.Write((uint) Math.Max(0, TableIdx(l.ItemId)));
            w.Write(l.FromPlayer ? l.InvIdx : l.Serial);
            w.Write(l.Price);
            w.Write((uint) l.State);
            w.Write((uint) l.RegDate);
            w.Write(DaysLeft(l));
        }

        // ItemInfo [8:0x54] (76 bytes no 2016): StackNum .. o dword antes de TableIdx
        static void WriteBody(BinaryWriter w, Listing l, Item own)
        {
            if (l.IsCar)
            {
                // XiStrCarUnit (32): CarID, CarType, cor, grau, slot, vezes no leilao, mitron, km
                w.Write(l.Serial); w.Write((uint) l.CarType); w.Write(l.Color); w.Write((uint) l.Grade);
                w.Write(0u); w.Write(0u); w.Write(l.Fuel); w.Write(l.Km);
                return;
            }
            if (own != null)
            {
                w.Write(own.stackNum); w.Write(own.lastCarID);
                w.Write(own.assistA); w.Write(own.assistB); w.Write(own.assistC); w.Write(own.assistD); w.Write(own.assistE);
                w.Write(own.assistF); w.Write(own.assistG); w.Write(own.assistH); w.Write(own.assistI); w.Write(own.assistJ);
                w.Write(own.box); w.Write(own.belonging); w.Write(own.upgrade); w.Write(own.upgradePoint);
                w.Write(0u); w.Write(own.itemHealth); w.Write(0);
                return;
            }
            w.Write(l.Stack); w.Write(0u);
            w.Write(l.AssistA); w.Write(l.AssistB); w.Write(l.AssistC);
            for (int i = 0; i < 7; i++) w.Write(0u);
            w.Write(0u); w.Write(0u); w.Write(l.Upgrade); w.Write(l.UpgradePoint);
            w.Write(0u); w.Write(1f); w.Write(0);
        }

        // XiStrAuctionClientUnit (62): i64 cid, nome 42 bytes, registros, compras, vendas
        static void WriteSeller(BinaryWriter w, Listing l)
        {
            var b = Bots.ByCid(l.SellerCid);
            var mine = Data().Listings.Count(x => x.SellerCid == l.SellerCid);
            w.Write(l.SellerCid);
            w.Write(WStr(l.SellerName, 42));
            w.Write(b != null ? b.Reg : (uint) mine);
            w.Write(b != null ? b.Bought : 0u);
            w.Write(b != null ? b.Sold : (uint) Data().Listings.Count(x => x.SellerCid == l.SellerCid && x.State == Sold));
        }

        static Item OwnItem(Rice.Game.Character ch, Listing l)
        {
            return l.FromPlayer && ch != null && (long) ch.CID == l.SellerCid
                ? ch.Inventory.FirstOrDefault(i => i.invIdx == l.InvIdx) : null;
        }

        static void Send(RiceClient c, ushort id, Action<BinaryWriter> f)
        {
            var p = new RicePacket(id);
            f(p.Writer);
            c.Send(p);
        }

        static Rice.Game.Character Me(RicePacket packet)
        {
            return packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
        }

        // ---- listas -------------------------------------------------------------------------------------------

        static List<Listing> PublicList(int type, int lo, int hi, string itemText, string sellerText, bool cars = false)
        {
            lock (Sync)
            {
                IEnumerable<Listing> q = Data().Listings.Where(l => l.State == OnSale && l.IsCar == cars);
                var r = new List<Listing>();
                foreach (var l in q)
                {
                    if (!cars)
                    {
                        var e = Entry(l.ItemId);
                        if (e == null) continue;
                        if (type != 0 && TabOf(e) != type) continue;
                    }
                    else if (type != 0 && l.Grade != type) continue;
                    int lv = ListingLevel(l);
                    if (lo != 0 && lv < lo) continue;
                    if (hi != 0 && lv > hi) continue;
                    if (!string.IsNullOrEmpty(itemText) && ListingName(l).IndexOf(itemText, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!string.IsNullOrEmpty(sellerText) && (l.SellerName ?? "").IndexOf(sellerText, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    r.Add(l);
                }
                return r;
            }
        }

        static List<Listing> Sort(List<Listing> l, int sort)
        {
            if (sort < 0 || sort >= 14) return l;
            Func<Listing, IComparable> key;
            switch (sort / 2)
            {
                case 1: key = x => ListingLevel(x); break;
                case 2: key = x => ListingName(x); break;
                case 3: key = x => x.Price; break;
                case 4: key = x => x.Stack; break;
                case 5: key = x => x.SellerName ?? ""; break;
                case 6: key = x => { var b = Bots.ByCid(x.SellerCid); return b != null ? b.Sold : 0u; }; break;
                default: key = x => x.Serial; break;
            }
            return sort % 2 == 0 ? l.OrderByDescending(key).ToList() : l.OrderBy(key).ToList();
        }

        static void SendList(RiceClient c, ushort ack, List<Listing> found, int offset, int count, Rice.Game.Character ch)
        {
            var page = found.Skip(Math.Max(0, offset)).Take(Math.Max(0, Math.Min(count, 50))).ToList();
            lock (Sync) Send(c, ack, w =>
            {
                w.Write((uint) found.Count);
                w.Write((uint) page.Count);
                foreach (var l in page)
                {
                    WriteSeller(w, l);
                    WriteUnit(w, l);
                    WriteBody(w, l, OwnItem(ch, l));
                }
            });
        }

        [RicePacket(588, RiceServer.ServerType.Game)]
        public static void AuctionList(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 24) return;
            int offset = BitConverter.ToInt32(p, 0), count = BitConverter.ToInt32(p, 4), type = BitConverter.ToInt32(p, 8);
            int sort = BitConverter.ToInt32(p, 12), lo = BitConverter.ToInt32(p, 16), hi = BitConverter.ToInt32(p, 20);
            var found = RiceServer.Config.AuctionEnabled ? Sort(PublicList(type, lo, hi, null, null), sort) : new List<Listing>();
            Log.WriteLine("Leilao lista tipo {0} ordem {1} niveis {2}-{3} pagina {4}+{5}: {6} anuncio(s).", type, sort, lo, hi, offset, count, found.Count);
            SendList(packet.Sender, 589, found, offset, count, Me(packet));
        }

        [RicePacket(595, RiceServer.ServerType.Game)]
        public static void AuctionSearch(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 20) return;
            int offset = BitConverter.ToInt32(p, 0), count = BitConverter.ToInt32(p, 4), type = BitConverter.ToInt32(p, 8);
            int sort = BitConverter.ToInt32(p, 12);
            string itemText = p.Length >= 0x114 ? Encoding.Unicode.GetString(p, 0x14, 0x100).Split('\0')[0].Trim() : "";
            string sellerText = p.Length >= 0x214 ? Encoding.Unicode.GetString(p, 0x114, 0x100).Split('\0')[0].Trim() : "";
            int lo = p.Length >= 0x218 ? BitConverter.ToInt32(p, 0x214) : 0;
            int hi = p.Length >= 0x21c ? BitConverter.ToInt32(p, 0x218) : 0;
            // a caixa "ID/Nome do item" pode ser nome do item OU do vendedor
            var found = PublicList(type, lo, hi, null, sellerText);
            if (!string.IsNullOrEmpty(itemText))
                found = found.Where(l =>
                {
                    var e = Entry(l.ItemId);
                    return (e != null && (e.Name ?? "").IndexOf(itemText, StringComparison.OrdinalIgnoreCase) >= 0)
                           || (l.SellerName ?? "").IndexOf(itemText, StringComparison.OrdinalIgnoreCase) >= 0;
                }).ToList();
            found = Sort(found, sort);
            Log.WriteLine("Leilao busca '{0}' / '{1}' tipo {2}: {3} anuncio(s).", itemText, sellerText, type, found.Count);
            SendList(packet.Sender, 596, found, offset, count, Me(packet));
        }

        [RicePacket(580, RiceServer.ServerType.Game)]
        public static void MyAuctionList(RicePacket packet)
        {
            var ch = Me(packet);
            if (ch == null) return;
            List<Listing> mine;
            lock (Sync) mine = Data().Listings.Where(l => l.FromPlayer && !l.IsCar && l.SellerCid == (long) ch.CID).OrderBy(l => l.Serial).ToList();
            Log.WriteLine("Leilao: meus anuncios = {0}.", mine.Count);
            Send(packet.Sender, 581, w =>
            {
                w.Write(0x40000u);
                w.Write((uint) mine.Count);
                foreach (var l in mine)
                {
                    WriteUnit(w, l);
                    WriteBody(w, l, OwnItem(ch, l));
                }
            });
        }

        // ---- comprar ------------------------------------------------------------------------------------------

        [RicePacket(592, RiceServer.ServerType.Game)]
        public static void AuctionBuy(RicePacket packet)
        {
            var p = packet.Buffer;
            var ch = Me(packet);
            if (ch == null || p.Length < 4) return;
            uint serial = BitConverter.ToUInt32(p, 0);
            Listing l;
            string refuse = null;
            lock (Sync)
            {
                l = Data().Listings.FirstOrDefault(x => x.Serial == serial);
                if (l == null || l.State != OnSale) refuse = "Este item ja foi vendido.";
                else if (l.SellerCid == (long) ch.CID) refuse = "Voce nao pode comprar o seu proprio anuncio.";
                else if (Entry(l.ItemId) == null) refuse = "Item desconhecido.";
                else if (ch.Mito < l.Price) refuse = "Mito insuficiente.";
                else if (!ch.SpendMito(l.Price)) refuse = "Mito insuficiente.";
                else Data().Listings.Remove(l);
            }
            if (refuse != null)
            {
                Log.WriteLine("Leilao compra {0}: recusada ({1})", serial, refuse);
                Bots.Popup(packet.Sender, refuse);
                return;
            }
            Save();

            Item it;
            if (!ch.GrantItem(l.ItemId, Math.Max(1u, l.Stack), out it) || it == null)
            {
                ch.GrantMito(l.Price, false);
                Bots.Popup(packet.Sender, "Nao foi possivel entregar o item (inventario cheio?). Mito devolvido.");
                return;
            }
            bool noLevel = l.ItemId != null && (l.ItemId.StartsWith("op_") || l.ItemId.StartsWith("dock_"));
            if (noLevel && !l.FromPlayer) { l.Upgrade = 0; l.UpgradePoint = 0; }   // anuncio antigo de bot com option/dock "+N"
            if (!(Entry(l.ItemId) is UseItemTableEntry) && (l.Upgrade != 0 || l.UpgradePoint != 0 || l.AssistA != 0))
            {
                if (!noLevel) { it.upgrade = l.Upgrade; it.upgradePoint = l.UpgradePoint; }   // option/dock: ficam os pontos sorteados
                it.assistA = l.AssistA; it.assistB = l.AssistB; it.assistC = l.AssistC;
                ch.SaveItemState(it);
            }
            var seller = Bots.ByCid(l.SellerCid);
            if (seller != null) { seller.Sold++; Bots.Save(); }
            Log.WriteLine("Leilao: {0} comprou {1} x{2} de {3} por {4}.", ch.Name, l.ItemId, l.Stack, l.SellerName, l.Price);
            ch.FlushModInfo(packet.Sender);
            Send(packet.Sender, 593, w => { w.Write(l.Serial); w.Write(l.Price); });
            if (seller != null && Rng.NextDouble() < 0.5)
                Bots.SayLater(packet.Sender, 2000, "channel", seller.Name, "Valeu pela compra, " + ch.Name + "!");
        }

        // ---- anunciar, cancelar, receber ----------------------------------------------------------------------

        [RicePacket(582, RiceServer.ServerType.Game)]
        public static void AuctionRegister(RicePacket packet)
        {
            var p = packet.Buffer;
            var ch = Me(packet);
            if (ch == null || p.Length < 16) return;
            uint table = BitConverter.ToUInt32(p, 0), inv = BitConverter.ToUInt32(p, 4);
            uint count = BitConverter.ToUInt32(p, 8), price = BitConverter.ToUInt32(p, 12);
            var cfg = RiceServer.Config;
            var it = ch.Inventory.FirstOrDefault(i => i.invIdx == inv);
            string refuse = null;
            if (!cfg.AuctionEnabled) refuse = "Leilao desligado (AuctionEnabled).";
            else if (it == null || it.itemEntry == null || (uint) it.tblIdx != table) refuse = "Item nao encontrado.";
            else if (price == 0) refuse = "Defina um preco.";
            else if (it.state != 0) refuse = "Tire o item do carro antes de anunciar.";
            else if (!string.Equals(it.itemEntry.Auctionable, "True", StringComparison.OrdinalIgnoreCase)) refuse = "Este item nao pode ir para o leilao.";
            count = Math.Max(1u, count);
            if (refuse == null && count > Math.Max(1u, it.stackNum)) refuse = "Quantidade invalida.";
            uint fee = (uint) Math.Max(1, (long) price * cfg.AuctionFeePercent / 100);
            if (refuse == null && !ch.SpendMito(fee)) refuse = "Mito insuficiente para a taxa (" + fee + ").";
            if (refuse != null)
            {
                Log.WriteLine("Leilao registro inv {0}: recusado ({1})", inv, refuse);
                Bots.Popup(packet.Sender, refuse);
                return;
            }

            if (it.itemEntry.IsStackable() && count < it.stackNum)
            {
                Item rest;
                ch.DropItem((int) it.invIdx, out rest, count, true);
                var split = Item.CreateEntry(ch.CID, it.ID, count);
                if (split == null) { ch.GrantMito(fee, false); Bots.Popup(packet.Sender, "Falha ao separar a pilha."); return; }
                ch.NotifyItem(split, true);
                it = split;
            }
            it.state = LockedState;
            ch.SaveItemState(it);

            Listing l;
            lock (Sync)
            {
                l = new Listing
                {
                    Serial = Data().NextSerial++, ItemId = it.ID, Stack = Math.Max(1u, it.stackNum), Price = price,
                    RegDate = Now(), SellerCid = (long) ch.CID, SellerName = ch.Name, FromPlayer = true, InvIdx = it.invIdx,
                    Upgrade = it.upgrade, UpgradePoint = it.upgradePoint, AssistA = it.assistA, AssistB = it.assistB, AssistC = it.assistC
                };
                Data().Listings.Add(l);
            }
            Save();
            Log.WriteLine("Leilao: {0} anunciou {1} x{2} por {3} (taxa {4}), serial {5}.", ch.Name, l.ItemId, l.Stack, price, fee, l.Serial);
            try
            {
                string tier;
                double pm;
                lock (Sync) pm = BuyChance(Data(), l, out tier);
                Log.WriteLine("Leilao: preco {0}, chance de venda {1:0.####} por minuto (~{2} min).", tier, pm, pm > 0 ? (int) Math.Ceiling(1 / pm) : -1);
                Chat.SendSystemMessage(packet.Sender, TierHint(tier));
            }
            catch (Exception hex) { Log.WriteError("Leilao (dica de preco): " + hex.Message); }
            try { Licenses.Count(ch, "GC_AuctionOn"); } catch (Exception lex) { Log.WriteError("Licencas (leilao): " + lex.Message); }
            ch.FlushModInfo(packet.Sender);
            var own = it;
            Send(packet.Sender, 583, w =>
            {
                w.Write(own.invIdx); w.Write(fee);
                WriteUnit(w, l);
                WriteBody(w, l, own);
            });
        }

        static string TierHint(string tier)
        {
            switch (tier)
            {
                case "loja": return "[Leilao] Mais barato que a loja: um bot deve comprar em poucos minutos.";
                case "leilao": return "[Leilao] Mais barato que no leilao: um bot deve comprar em pouco tempo.";
                case "justo": return "[Leilao] Preco justo: deve vender em menos de 1 hora.";
                case "alto": return "[Leilao] Preco alto: pode levar algumas horas para vender.";
                case "caro": return "[Leilao] Preco caro: pode levar mais de um dia para vender.";
                default: return "[Leilao] Preco muito caro: pode levar dias, ou expirar sem vender.";
            }
        }

        static Listing OwnListing(Rice.Game.Character ch, byte[] p)
        {
            if (ch == null || p.Length < 4) return null;
            uint serial = BitConverter.ToUInt32(p, 0);
            lock (Sync) return Data().Listings.FirstOrDefault(x => x.Serial == serial && x.FromPlayer && x.SellerCid == (long) ch.CID);
        }

        [RicePacket(584, RiceServer.ServerType.Game)]
        public static void AuctionCancel(RicePacket packet)
        {
            var ch = Me(packet);
            var l = OwnListing(ch, packet.Buffer);
            if (l == null || (l.State != OnSale && l.State != Expired))
            {
                if (ch != null) Bots.Popup(packet.Sender, l != null && l.State == Sold ? "Ja foi vendido: use Completar." : "Anuncio nao encontrado.");
                return;
            }
            lock (Sync) Data().Listings.Remove(l);
            Save();
            var it = ch.Inventory.FirstOrDefault(i => i.invIdx == l.InvIdx);
            Send(packet.Sender, 585, w => w.Write(l.Serial));
            if (it != null)
            {
                it.state = 0;
                ch.SaveItemState(it);
                ch.FlushModInfo(packet.Sender);
            }
            Log.WriteLine("Leilao: anuncio {0} cancelado, item devolvido.", l.Serial);
        }

        [RicePacket(586, RiceServer.ServerType.Game)]
        public static void AuctionComplete(RicePacket packet)
        {
            var ch = Me(packet);
            var l = OwnListing(ch, packet.Buffer);
            if (l == null || l.State != Sold)
            {
                if (ch != null) Bots.Popup(packet.Sender, "Este anuncio ainda nao foi vendido.");
                return;
            }
            lock (Sync) Data().Listings.Remove(l);
            Save();
            ch.GrantMito(l.Price, false);
            Send(packet.Sender, 587, w => { w.Write(l.Serial); w.Write(l.Price); });
            var it = ch.Inventory.FirstOrDefault(i => i.invIdx == l.InvIdx);
            if (it != null)
            {
                Item gone;
                ch.DropItem((int) it.invIdx, out gone, Math.Max(1u, it.stackNum), true);
                ch.FlushModInfo(packet.Sender);
            }
            Log.WriteLine("Leilao: {0} recebeu {1} mito pelo anuncio {2}.", ch.Name, l.Price, l.Serial);
            try { Licenses.Count(ch, "GC_AuctionSuccess"); } catch (Exception lex) { Log.WriteError("Licencas (leilao): " + lex.Message); }
        }

        // ---- leilao de carros ---------------------------------------------------------------------------------
        // 1700 MyCarList -> 1701 ; 1708 CarList -> 1709 ; 1713 CarSearch -> 1714 ; 1710 CarBuy -> 402 + 1711
        // Entrada publica: vendedor (62) + XiStrAuctionUnit (28) + XiStrCarUnit (32). Grau = filtro de tipo.

        [RicePacket(1700, RiceServer.ServerType.Game)]
        public static void MyAuctionCarList(RicePacket packet)
        {
            Send(packet.Sender, 1701, w => { w.Write(0x40000u); w.Write(0u); });
        }

        [RicePacket(1708, RiceServer.ServerType.Game)]
        public static void AuctionCarList(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 24) return;
            int offset = BitConverter.ToInt32(p, 0), count = BitConverter.ToInt32(p, 4), type = BitConverter.ToInt32(p, 8);
            int sort = BitConverter.ToInt32(p, 12), lo = BitConverter.ToInt32(p, 16), hi = BitConverter.ToInt32(p, 20);
            var found = RiceServer.Config.AuctionEnabled ? Sort(PublicList(type, lo, hi, null, null, true), sort) : new List<Listing>();
            Log.WriteLine("Leilao de carros: grau {0} ordem {1} pagina {2}+{3}: {4} carro(s).", type, sort, offset, count, found.Count);
            SendList(packet.Sender, 1709, found, offset, count, Me(packet));
        }

        [RicePacket(1713, RiceServer.ServerType.Game)]
        public static void AuctionCarSearch(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 20) return;
            int offset = BitConverter.ToInt32(p, 0), count = BitConverter.ToInt32(p, 4), type = BitConverter.ToInt32(p, 8);
            int sort = BitConverter.ToInt32(p, 12), searchType = BitConverter.ToInt32(p, 16);
            string text = p.Length >= 0x114 ? Encoding.Unicode.GetString(p, 0x14, 0x100).Split('\0')[0].Trim() : "";
            int lo = p.Length >= 0x118 ? BitConverter.ToInt32(p, 0x114) : 0;
            int hi = p.Length >= 0x11c ? BitConverter.ToInt32(p, 0x118) : 0;
            var found = PublicList(type, lo, hi, null, null, true);
            if (!string.IsNullOrEmpty(text))
                found = found.Where(l => ListingName(l).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                                         || (l.SellerName ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            found = Sort(found, sort);
            Log.WriteLine("Leilao de carros: busca '{0}' (tipo {1}): {2} carro(s).", text, searchType, found.Count);
            SendList(packet.Sender, 1714, found, offset, count, Me(packet));
        }

        [RicePacket(1710, RiceServer.ServerType.Game)]
        public static void AuctionCarBuy(RicePacket packet)
        {
            var p = packet.Buffer;
            var ch = Me(packet);
            if (ch == null || p.Length < 4) return;
            uint serial = BitConverter.ToUInt32(p, 0);
            Listing l;
            string refuse = null;
            lock (Sync)
            {
                l = Data().Listings.FirstOrDefault(x => x.Serial == serial && x.IsCar);
                VehicleListEntry v = null;
                if (l == null || l.State != OnSale) refuse = "Este carro ja foi vendido.";
                else if (!VehicleTable.Vehicles.TryGetValue(l.CarType, out v) || v == null) refuse = "Carro desconhecido.";
                else
                {
                    int need = Rice.Game.Character.GetRequiredLevelForTier(v.Level);
                    if (v.IsSpecialCategory()) need = Math.Max(need, RiceServer.Config.SpecialCarsMinLevel);
                    if (ch.Level < need) refuse = "Nivel insuficiente: este carro exige nivel " + need + ".";
                    else if (ch.Mito < l.Price || !ch.SpendMito(l.Price)) refuse = "Mito insuficiente.";
                    else Data().Listings.Remove(l);
                }
            }
            if (refuse != null)
            {
                Log.WriteLine("Leilao de carros: compra {0} recusada ({1})", serial, refuse);
                Bots.Popup(packet.Sender, refuse);
                return;
            }
            Save();
            Vehicle veh;
            Item key;
            if (!ch.GrantVehicle(l.CarType, out veh, out key, (int) l.Color, l.Grade) || veh == null)
            {
                ch.GrantMito(l.Price, false);
                Bots.Popup(packet.Sender, "Nao foi possivel entregar o carro (garagem cheia?). Mito devolvido.");
                return;
            }
            ch.SetCarCondition(veh.CarID, l.Fuel, l.Km);
            var seller = Bots.ByCid(l.SellerCid);
            if (seller != null) { seller.Sold++; Bots.Save(); }
            Log.WriteLine("Leilao de carros: {0} comprou {1} V{2} de {3} por {4} (carro {5}).", ch.Name, Bots.CarName(l.CarType), l.Grade, l.SellerName, l.Price, veh.CarID);
            ch.FlushModInfo(packet.Sender);
            Send(packet.Sender, 1711, w => { w.Write(l.Serial); w.Write(l.Price); ((PacketWriter) w).Write(veh.GetInfo()); });
            if (seller != null)
                Bots.SayLater(packet.Sender, 2000, "channel", seller.Name,
                    seller.Special ? "Valeu, " + ch.Name + "! Cuida bem do " + Bots.CarName(l.CarType) + "." : "Boa compra, " + ch.Name + "!");
        }

        // ---- comando de chat ----------------------------------------------------------------------------------

        public static bool HandleCommand(RiceClient c, string lower, string body)
        {
            if (lower != "leilao") return false;
            var ch = c.Player.ActiveCharacter;
            List<Listing> mine;
            int total;
            lock (Sync)
            {
                mine = Data().Listings.Where(l => l.FromPlayer && l.SellerCid == (long) ch.CID).ToList();
                total = Data().Listings.Count(l => l.State == OnSale);
            }
            Chat.SendSystemMessage(c, string.Format("Leilao: {0} anuncio(s) a venda. Seus: {1} a venda, {2} vendido(s) (clique Completar), {3} expirado(s).",
                total, mine.Count(l => l.State == OnSale), mine.Count(l => l.State == Sold), mine.Count(l => l.State == Expired)));
            Chat.SendSystemMessage(c, "Dica: bots compram rapido se o preco for ate o valor de loja; acima de 2.5x ninguem compra.");
            return true;
        }
    }
}
