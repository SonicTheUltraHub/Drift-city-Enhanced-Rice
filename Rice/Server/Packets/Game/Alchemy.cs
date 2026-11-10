using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Production Skill Book (fabricacao / "Alchemy"). Pacotes achados no DriftCity.exe 2016:
    //   3701 AlchemyInfoRes  : u32 0, u32 n, i64 exp, n x char[120] (ids dos livros aprendidos)
    //   3704 Create (cliente): char[56] id do livro + 8 bytes   -> 3705 CreateRes: char[56] id, u8 sucesso, i64 exp, u32 0
    //   3706 Delete (cliente): char[56] id                      -> 3707 DeleteRes: char[56] id
    //   3708 AddSkillBookRes : char[56] id (o livro aparece aceso na janela)
    //   3709 (cliente, u32)  -> respondemos com o 3701 ; 3710 SetLevelRes: i64 exp
    // O nivel de fabricacao vem da exp (tabela do cliente: 8610 / 66420 / 221430 / 521640). As receitas (materiais,
    // kit, resultado, chance, exp) estao em res/AlchemyRecipes.json (Cliente/ferramentas/producao_receitas.py).
    // Aprender: clique duas vezes no Skill Book no inventario. Livros e kits: !!livro e !!kit (custam mito).
    public static class Alchemy
    {
        public class Recipe
        {
            public string book, group, name, kit, result, failItem;
            public int level, kitCount, resultCount, failChance, failCount, chance, exp, failExp;
            public List<List<object>> need;
        }
        class Rec { public long Exp; public List<string> Books = new List<string>(); }

        const string FileName = "alchemy.json";
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static List<Recipe> recipes;
        static long[] levels;
        static Dictionary<string, Rec> store;

        static void Load()
        {
            if (recipes != null) return;
            recipes = new List<Recipe>(); levels = new long[] { 8610, 66420, 221430, 521640 };
            try
            {
                string json = null;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "AlchemyRecipes.json");
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AlchemyRecipes.json"))
                        if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                if (json == null) { Log.WriteError("Producao: AlchemyRecipes.json nao encontrado."); return; }
                var j = JObject.Parse(json);
                recipes = j["recipes"].ToObject<List<Recipe>>();
                levels = j["levels"].Select(x => (long) x).ToArray();
            }
            catch (Exception ex) { Log.WriteError("Producao: " + ex.Message); }
        }

        static Rec RecOf(Rice.Game.Character ch)
        {
            if (store == null)
            {
                try { store = CharFiles.Exists(FileName) ? JsonConvert.DeserializeObject<Dictionary<string, Rec>>(CharFiles.ReadAllText(FileName)) : null; }
                catch (Exception) { store = null; }
                if (store == null) store = new Dictionary<string, Rec>();
            }
            Rec r;
            if (!store.TryGetValue(ch.CID.ToString(), out r)) { r = new Rec(); store[ch.CID.ToString()] = r; }
            if (r.Books == null) r.Books = new List<string>();
            return r;
        }

        static void Save()
        {
            try { CharFiles.WriteAllText(FileName, JsonConvert.SerializeObject(store, Formatting.Indented)); }
            catch (Exception ex) { Log.WriteError("alchemy.json: " + ex.Message); }
        }

        // 1 Low, 2 Mid, 3 High, 4 Highest
        public static int LevelOf(long exp)
        {
            Load();
            for (int i = 0; i < levels.Length; i++) if (exp < levels[i]) return i + 1;
            return levels.Length;
        }
        static readonly string[] LevelNames = { "", "Low", "Mid", "High", "Highest" };
        static string LevelName(int lv) { return lv >= 1 && lv < LevelNames.Length ? LevelNames[lv] : lv.ToString(); }

        public static Recipe RecipeOf(string book) { Load(); return recipes.FirstOrDefault(r => string.Equals(r.book, book, StringComparison.OrdinalIgnoreCase)); }

        static byte[] Ascii(string s, int size)
        {
            var b = new byte[size];
            var e = Encoding.ASCII.GetBytes(s ?? "");
            Array.Copy(e, b, Math.Min(e.Length, size - 1));
            return b;
        }
        static string ReadAscii(byte[] buf, int at, int size)
        {
            int n = 0;
            while (n < size && at + n < buf.Length && buf[at + n] != 0) n++;
            return Encoding.ASCII.GetString(buf, at, n).Trim();
        }

        // ---- janela ------------------------------------------------------------------------------------------
        public static void SendInfo(RiceClient c, Rice.Game.Character ch)
        {
            if (c == null || ch == null) return;
            Load();
            long exp; List<string> books;
            lock (Sync) { var r = RecOf(ch); exp = r.Exp; books = r.Books.ToList(); }
            var pk = new RicePacket(3701);
            pk.Writer.Write(0u);
            pk.Writer.Write((uint) books.Count);
            pk.Writer.Write(exp);
            foreach (var b in books) pk.Writer.Write(Ascii(b, 120));
            c.Send(pk);
        }

        static void SendExp(RiceClient c, long exp)
        {
            var pk = new RicePacket(3710);
            pk.Writer.Write(exp);
            c.Send(pk);
        }

        [RicePacket(3700, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void InfoReq(RicePacket packet)
        {
            Log.WriteLine("Producao: pedido de informacoes (3700).");
            SendInfo(packet.Sender, packet.Sender.Player.ActiveCharacter);
        }

        [RicePacket(3709, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void LevelReq(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("Producao: pacote 3709: {0}", BitConverter.ToString(buf, 0, Math.Min(buf.Length, 16)));
            var ch = packet.Sender.Player.ActiveCharacter;
            SendInfo(packet.Sender, ch);
            long exp; lock (Sync) exp = RecOf(ch).Exp;
            SendExp(packet.Sender, exp);
        }

        // ---- aprender (Skill Book no inventario) -------------------------------------------------------------
        public static bool UseBook(RiceClient c, Rice.Game.Character ch, Rice.Game.Item item, int invIdx)
        {
            if (item == null || item.itemEntry == null || item.itemEntry.Category != "user_create") return false;
            var rc = RecipeOf(item.ID);
            if (rc == null) { Chat.SendSystemMessage(c, item.itemEntry.Name + ": receita nao encontrada."); return true; }
            int lv;
            lock (Sync)
            {
                var r = RecOf(ch);
                lv = LevelOf(r.Exp);
                if (r.Books.Any(b => string.Equals(b, rc.book, StringComparison.OrdinalIgnoreCase)))
                { Chat.SendSystemMessage(c, "Voce ja aprendeu " + rc.name + "."); return true; }
                if (lv < rc.level)
                { Chat.SendSystemMessage(c, rc.name + " pede nivel de fabricacao " + LevelName(rc.level) + " (o seu: " + LevelName(lv) + "). Fabrique outras coisas para subir."); return true; }
                r.Books.Add(rc.book);
                Save();
            }
            Rice.Game.Item used;
            ch.DropItem(invIdx, out used, 1, true);
            ch.FlushModInfo(c);
            var pk = new RicePacket(3708);
            pk.Writer.Write(Ascii(rc.book, 56));
            c.Send(pk);
            Chat.SendSystemMessage(c, "Receita aprendida: " + rc.name + "! Abra a janela de producao para fabricar. Precisa: " + NeedText(rc) + ".");
            Log.WriteLine("Producao: {0} aprendeu {1}.", ch.Name, rc.book);
            return true;
        }

        static string ItemName(string id)
        {
            var e = Rice.Server.Structures.Resources.ItemTable.Items.FirstOrDefault(x => x.ID == id);
            return e != null ? e.Name : id;
        }

        static string NeedText(Recipe rc)
        {
            var parts = rc.need.Select(n => Convert.ToInt32(n[1]) + "x " + ItemName((string) n[0])).ToList();
            if (!string.IsNullOrEmpty(rc.kit)) parts.Add(rc.kitCount + "x " + ItemName(rc.kit));
            return string.Join(", ", parts);
        }

        static long Count(Rice.Game.Character ch, string id)
        {
            return ch.Inventory.Where(i => i.ID == id && i.state != 1).Sum(i => (long) Math.Max(1u, i.stackNum));
        }

        static void Take(Rice.Game.Character ch, string id, long count)
        {
            foreach (var st in ch.Inventory.Where(i => i.ID == id && i.state != 1).OrderBy(i => i.stackNum).ToList())
            {
                if (count <= 0) break;
                uint take = (uint) Math.Min(count, Math.Max(1u, st.stackNum));
                Rice.Game.Item gone;
                if (ch.DropItem((int) st.invIdx, out gone, take, true)) count -= take;
            }
        }

        // ---- fabricar ----------------------------------------------------------------------------------------
        [RicePacket(3704, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Create(RicePacket packet)
        {
            var buf = packet.Buffer;
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            string id = ReadAscii(buf, 0, 56);
            Log.WriteLine("Producao: fabricar '{0}' ({1})", id, BitConverter.ToString(buf, Math.Min(buf.Length, 56), Math.Max(0, Math.Min(8, buf.Length - 56))));
            var rc = RecipeOf(id);
            long exp;
            bool ok = false; string msg;
            lock (Sync)
            {
                var r = RecOf(ch);
                exp = r.Exp;
                if (rc == null) msg = "Receita '" + id + "' nao encontrada.";
                else if (!r.Books.Any(b => string.Equals(b, rc.book, StringComparison.OrdinalIgnoreCase))) msg = "Voce ainda nao aprendeu " + rc.name + " (use o Skill Book).";
                else msg = null;
            }
            if (msg == null)
            {
                var missing = rc.need.Where(n => Count(ch, (string) n[0]) < Convert.ToInt32(n[1])).Select(n => ItemName((string) n[0])).ToList();
                if (!string.IsNullOrEmpty(rc.kit) && Count(ch, rc.kit) < rc.kitCount) missing.Add(ItemName(rc.kit) + " (!!kit)");
                if (missing.Count > 0) msg = "Falta para " + ItemName(rc.result) + ": " + string.Join(", ", missing) + ". Precisa: " + NeedText(rc) + ".";
            }
            if (msg != null)
            {
                Chat.SendSystemMessage(c, msg);
                Reply(c, id, false, exp);
                return;
            }
            foreach (var n in rc.need) Take(ch, (string) n[0], Convert.ToInt32(n[1]));
            if (!string.IsNullOrEmpty(rc.kit)) Take(ch, rc.kit, rc.kitCount);
            double chance = Math.Max(0, Math.Min(100, rc.chance + RiceServer.Config.AlchemyChanceBonus));
            lock (Rng) ok = Rng.NextDouble() * 100 < chance;
            string text;
            Rice.Game.Item got = null;
            if (ok)
            {
                ch.GrantItem(rc.result, (uint) Math.Max(1, rc.resultCount), out got);
                text = "Fabricou " + Math.Max(1, rc.resultCount) + "x " + ItemName(rc.result) + "!";
            }
            else
            {
                text = "A fabricacao de " + ItemName(rc.result) + " falhou (chance " + (int) chance + "%).";
                if (!string.IsNullOrEmpty(rc.failItem) && rc.failChance > 0)
                {
                    bool give; lock (Rng) give = Rng.Next(100) < rc.failChance;
                    Rice.Game.Item fi;
                    if (give && ch.GrantItem(rc.failItem, (uint) Math.Max(1, rc.failCount), out fi) && fi != null) text += " Consolo: " + fi.itemEntry.Name + ".";
                }
            }
            int before, after;
            lock (Sync)
            {
                var r = RecOf(ch);
                before = LevelOf(r.Exp);
                r.Exp += (long) Math.Round((ok ? rc.exp : rc.failExp) * Math.Max(0, RiceServer.Config.AlchemyExpMultiplier));
                exp = r.Exp; after = LevelOf(exp);
                Save();
            }
            ch.FlushModInfo(c);
            Reply(c, rc.book, ok, exp);
            if (got != null) Misc.GetItemFx(c, got, (uint) Math.Max(1, rc.resultCount));
            Chat.SendSystemMessage(c, text + " Exp de fabricacao: " + exp + ".");
            if (after > before) Chat.SendSystemMessage(c, "Nivel de fabricacao subiu: " + LevelName(after) + " Level Grade! Novas receitas liberadas.");
            Log.WriteLine("Producao: {0} {1} {2} (exp {3}).", ch.Name, ok ? "fabricou" : "falhou", rc.result, exp);
            if (ok) { try { Licenses.Count(ch, "GC_CreateThisMonth"); } catch (Exception) { } }
        }

        static void Reply(RiceClient c, string id, bool ok, long exp)
        {
            var pk = new RicePacket(3705);
            pk.Writer.Write(Ascii(id, 56));
            pk.Writer.Write((byte) (ok ? 1 : 0));
            pk.Writer.Write(exp);
            pk.Writer.Write(0u);
            c.Send(pk);
        }

        [RicePacket(3706, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Delete(RicePacket packet)
        {
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            string id = ReadAscii(packet.Buffer, 0, 56);
            lock (Sync) { var r = RecOf(ch); r.Books.RemoveAll(b => string.Equals(b, id, StringComparison.OrdinalIgnoreCase)); Save(); }
            var pk = new RicePacket(3707);
            pk.Writer.Write(Ascii(id, 56));
            c.Send(pk);
            Log.WriteLine("Producao: {0} apagou a receita {1}.", ch.Name, id);
        }

        // ---- comandos ----------------------------------------------------------------------------------------
        public static string Status(Rice.Game.Character ch)
        {
            Load();
            lock (Sync)
            {
                var r = RecOf(ch);
                int lv = LevelOf(r.Exp);
                long next = lv - 1 < levels.Length ? levels[lv - 1] : 0;
                var names = r.Books.Select(b => { var x = RecipeOf(b); return x != null ? ItemName(x.result) : b; });
                return "Fabricacao: " + LevelName(lv) + " Level Grade, exp " + r.Exp + (next > 0 ? "/" + next : "")
                       + ". Receitas: " + (r.Books.Count == 0 ? "nenhuma (compre com !!livro)" : string.Join(", ", names)) + ".";
            }
        }

        // !!livro (lista) / !!livro <parte do nome>: compra o Skill Book pelo preco da tabela
        public static string BuyBook(Rice.Game.Character ch, string query)
        {
            Load();
            query = (query ?? "").Trim();
            var real = recipes.Where(x => !x.book.StartsWith("201")).ToList();   // sem as de evento (Natal/Valentine)
            if (query.Length == 0)
                return "Livros: " + string.Join(", ", real.Select(x => ItemName(x.result) + " (" + LevelName(x.level) + ", " + BookPrice(x).ToString("N0") + ")"))
                       + ". Use !!livro <nome do item>.";
            var hit = real.Where(x => ItemName(x.result).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                                   || ItemName(x.book).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (hit.Count == 0) return "Nenhum livro com '" + query + "'. Digite !!livro para ver a lista.";
            if (hit.Count > 1 && !hit.Any(x => string.Equals(ItemName(x.result), query, StringComparison.OrdinalIgnoreCase)))
                return "Varios: " + string.Join(", ", hit.Select(x => ItemName(x.result))) + ". Seja mais especifico.";
            var rc = hit.FirstOrDefault(x => string.Equals(ItemName(x.result), query, StringComparison.OrdinalIgnoreCase)) ?? hit[0];
            long price = BookPrice(rc);
            if (!ch.SpendMito((ulong) price)) return "Mito insuficiente: " + ItemName(rc.book) + " custa " + price.ToString("N0") + ".";
            Rice.Game.Item it;
            if (!ch.GrantItem(rc.book, 1, out it)) { ch.GrantMito((ulong) price, false); return "Inventario cheio."; }
            return "Comprou " + ItemName(rc.book) + " por " + price.ToString("N0") + " mito. Clique duas vezes nele no inventario para aprender.";
        }

        // preco de cada Skill Book (AlchemyBookPrice, 50.000 mito)
        static long BookPrice(Recipe rc) { return Math.Max(0, RiceServer.Config.AlchemyBookPrice); }

        // !!kit <1|2|3> [quantidade]: Normal (1), Small (2) e Large (3) Manufacturing Kit
        public static string BuyKit(Rice.Game.Character ch, string args)
        {
            var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int k, n = 1;
            if (parts.Length == 0 || !int.TryParse(parts[0], out k) || k < 1 || k > 3)
                return "Uso: !!kit <1|2|3> [quantidade]. 1 = Normal Manufacturing Kit, 2 = Small, 3 = Large (cada receita diz qual pede).";
            if (parts.Length > 1) int.TryParse(parts[1], out n);
            n = Math.Max(1, Math.Min(99, n));
            string id = "user_create_kit" + k;
            long each = k == 1 ? 1200 : k == 2 ? 700 : 2000;
            long price = (long) Math.Round(each * n * Math.Max(0, RiceServer.Config.AlchemyKitPriceMultiplier));
            if (!ch.SpendMito((ulong) price)) return "Mito insuficiente: " + n + "x " + ItemName(id) + " custa " + price.ToString("N0") + ".";
            Rice.Game.Item it;
            if (!ch.GrantItem(id, (uint) n, out it)) { ch.GrantMito((ulong) price, false); return "Inventario cheio."; }
            return "Comprou " + n + "x " + ItemName(id) + " por " + price.ToString("N0") + " mito.";
        }
    }
}
