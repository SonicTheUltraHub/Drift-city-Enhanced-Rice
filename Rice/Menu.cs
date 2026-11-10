using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rice
{
    // Menu do Offline Aprimorado (Rice.exe menu / Menu.bat): mexer nos saves, bots, crews e configuracoes do servidor.
    // Feche o servidor (Rice.exe) antes: o menu grava direto nos arquivos e no banco, e o servidor le tudo ao abrir.
    public static class Menu
    {
        static Config cfg;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Run(Config config)
        {
            cfg = config;
            Console.Title = "Offline Aprimorado - Menu";
            while (true)
            {
                Header("Offline Aprimorado - Menu");
                if (ServerRunning())
                {
                    Warn("O servidor (Rice.exe) esta aberto. Feche ele antes de mudar alguma coisa aqui,");
                    Warn("senao ele grava por cima das suas mudancas.");
                    Console.WriteLine();
                }
                Opt("1", "Saves (contas e corredores)", "Suas contas e corredores: criar, renomear, apagar, mudar nivel, mito e o V dos carros.");
                Opt("2", "Bots", "Os jogadores simulados: nome, nivel, carro, V, personalidade, cidade, habilidade e mito; recriar todos.");
                Opt("3", "Crews", "As crews dos bots: nome, descricao e nivel minimo para entrar.");
                Opt("4", "Configuracoes do servidor", "Todas as opcoes do servidor (server.json), separadas por assunto e com explicacao de cada uma.");
                Opt("5", "Backup dos saves", "Guarda e restaura seus saves (save, mundo, mapas e server.json) num arquivo .zip.");
                Opt("6", "Abrir a pasta dos saves", "Abre a pasta save no Windows Explorer.");
                Opt("7", "Evento especial (Natal, Valentine, Halloween)", "Visual de evento do jogo: automatico pelo mes (padrao) ou escolhido por voce.");
                Opt("8", "Mods (Escada de crews)", "Instala, liga, desliga ou desinstala o mod Escada de crews (crews por cidade, chefes e Most Wanted).");
                Opt("9", "Idioma / Language", "Ingles ou portugues no servidor, no chat do jogo e neste menu.");
                Opt("G", "Graficos do jogo", "Resolucao, tela cheia, sombras, distancia de visao, detalhes e tamanho da interface do jogo.");
                Opt("0", "Sair", "");
                switch (Ask("Escolha"))
                {
                    case "1": Guard(SavesMenu); break;
                    case "2": Guard(BotsMenu); break;
                    case "3": Guard(CrewsMenu); break;
                    case "4": Guard(ConfigMenu); break;
                    case "5": Guard(BackupMenu); break;
                    case "6": OpenFolder(Path.GetFullPath(Paths.Save)); break;
                    case "7": Guard(EventMenu); break;
                    case "8": Guard(ModsMenu); break;
                    case "9": Guard(LanguageMenu); break;
                    case "g": case "G": Guard(GraphicsMenu); break;
                    case "0": case "": return;
                }
            }
        }

        static void Guard(Action a)
        {
            try { a(); }
            catch (Exception ex) { Warn("Erro: " + ex.Message); Pause(); }
        }

        // ------------------------------------------------------------------------------------------------ saves
        class Acc { public long ID; public string Name; public List<Chr> Chars = new List<Chr>(); }
        class Chr { public long ID; public string Name; public int Level; public long Mito; public int Cars; public int Items; }

        static List<Acc> LoadAccounts()
        {
            using (var rc = Database.GetContext())
            {
                var accs = rc.Users.OrderBy(u => u.ID).ToList().Select(u => new Acc { ID = u.ID, Name = u.Name }).ToList();
                var chars = rc.Characters.ToList();
                var cars = rc.Vehicles.GroupBy(v => v.CID).Select(g => new { g.Key, N = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.N);
                var items = rc.Items.GroupBy(v => v.CID).Select(g => new { g.Key, N = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.N);
                foreach (var c in chars.OrderBy(ch => ch.ID))
                {
                    var a = accs.FirstOrDefault(x => x.ID == c.UID);
                    if (a == null) continue;
                    int nc, ni; cars.TryGetValue(c.ID, out nc); items.TryGetValue(c.ID, out ni);
                    a.Chars.Add(new Chr { ID = c.ID, Name = c.Name, Level = c.Level, Mito = c.Mito, Cars = nc, Items = ni });
                }
                return accs;
            }
        }

        static void SavesMenu()
        {
            while (true)
            {
                Header("Saves - contas");
                Intro("Cada conta e o nome que voce digita no login; dentro dela ficam os corredores (personagens).",
                      "Escolha o numero de uma conta para ver os corredores dela, ou N para criar uma conta nova.");
                var accs = LoadAccounts();
                for (int i = 0; i < accs.Count; i++)
                {
                    var a = accs[i];
                    string tag = a.Name == AdminName() ? "  [admin]" : "";
                    Console.WriteLine(" {0}) {1}{2} - {3} corredor(es)", i + 1, a.Name, tag, a.Chars.Count);
                    foreach (var c in a.Chars) Console.WriteLine("       - {0} (nivel {1}, {2:N0} mito, {3} carro(s), {4} item(ns))", c.Name, c.Level, c.Mito, c.Cars, c.Items);
                }
                Console.WriteLine();
                Console.WriteLine(" N) Nova conta    0) Voltar");
                string s = Ask("Escolha uma conta");
                if (s == "0" || s == "") return;
                if (s.Equals("n", StringComparison.OrdinalIgnoreCase)) { NewAccount(); continue; }
                int k;
                if (int.TryParse(s, out k) && k >= 1 && k <= accs.Count) AccountMenu(accs[k - 1].ID);
            }
        }

        static string AdminName() { return string.IsNullOrEmpty(cfg.AdminAccount) ? "admin" : cfg.AdminAccount; }

        static void NewAccount()
        {
            if (!CanWrite()) return;
            string name = Ask("Nome da conta nova (o mesmo do login)");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 32) return;
            using (var rc = Database.GetContext())
            {
                if (rc.Users.Any(u => u.Name == name)) { Warn("Ja existe uma conta com esse nome."); Pause(); return; }
                rc.Users.Add(new Server.Database.Models.User { Name = name, PasswordHash = Utilities.MD5(name), CreateIP = "127.0.0.1", Status = 1, Credits = 1000 });
                rc.SaveChanges();
            }
            Directory.CreateDirectory(Path.Combine(Paths.Save, Paths.Safe(name)));
            Ok("Conta criada. Entre no jogo com esse usuario para criar os corredores.");
        }

        static void AccountMenu(long uid)
        {
            while (true)
            {
                var a = LoadAccounts().FirstOrDefault(x => x.ID == uid);
                if (a == null) return;
                Header("Conta " + a.Name);
                Intro("Escolha o numero de um corredor para mudar nome, nivel, mito ou o V de um carro.",
                      "R renomeia a conta (o nome do login), A apaga a conta inteira, P abre a pasta dela.");
                for (int i = 0; i < a.Chars.Count; i++)
                    Console.WriteLine(" {0}) {1} (nivel {2}, {3:N0} mito)", i + 1, a.Chars[i].Name, a.Chars[i].Level, a.Chars[i].Mito);
                Console.WriteLine();
                Console.WriteLine(" R) Renomear a conta   A) Apagar a conta   P) Abrir a pasta   0) Voltar");
                string s = Ask("Escolha um corredor ou opcao");
                if (s == "0" || s == "") return;
                if (s.Equals("r", StringComparison.OrdinalIgnoreCase)) RenameAccount(a);
                else if (s.Equals("a", StringComparison.OrdinalIgnoreCase)) { if (DeleteAccount(a)) return; }
                else if (s.Equals("p", StringComparison.OrdinalIgnoreCase)) OpenFolder(Path.GetFullPath(Path.Combine(Paths.Save, Paths.Safe(a.Name))));
                else
                {
                    int k;
                    if (int.TryParse(s, out k) && k >= 1 && k <= a.Chars.Count) CharMenu(a, a.Chars[k - 1].ID);
                }
            }
        }

        static void RenameAccount(Acc a)
        {
            if (!CanWrite()) return;
            string name = Ask("Novo nome da conta (atual: " + a.Name + ")");
            if (string.IsNullOrWhiteSpace(name) || name == a.Name || name.Length > 32) return;
            using (var rc = Database.GetContext())
            {
                if (rc.Users.Any(u => u.Name == name)) { Warn("Ja existe uma conta com esse nome."); Pause(); return; }
                var usr = rc.Users.Find(a.ID);
                usr.Name = name;
                usr.PasswordHash = Utilities.MD5(name);
                rc.SaveChanges();
            }
            MoveDir(Path.Combine(Paths.Save, Paths.Safe(a.Name)), Path.Combine(Paths.Save, Paths.Safe(name)));
            // a conta admin / normal tem o nome na configuracao: acompanha
            if (a.Name == AdminName()) { SetConfig("AdminAccount", name); cfg.AdminAccount = name; }
            if (a.Name == cfg.NormalAccount) { SetConfig("NormalAccount", name); cfg.NormalAccount = name; }
            Ok("Conta renomeada. No login, use \"" + name + "\".");
        }

        static bool DeleteAccount(Acc a)
        {
            if (!CanWrite()) return false;
            bool admin = a.Name == AdminName();
            Warn("Apagar a conta \"" + a.Name + "\" e os " + a.Chars.Count + " corredor(es) dela? Nao tem volta (faca um backup antes).");
            if (admin) Warn("E a conta admin: ao abrir o servidor ela volta do padrao, com o perfil upado (" + cfg.UppedProfileName + ", carro de policia todo upado).");
            if (!Confirm("Digite APAGAR para confirmar", "APAGAR")) return false;
            using (var rc = Database.GetContext())
            {
                foreach (var c in a.Chars) DeleteCharRows(rc, c.ID);
                var usr = rc.Users.Find(a.ID);
                if (usr != null) rc.Users.Remove(usr);
                rc.SaveChanges();
            }
            DeleteDir(Path.Combine(Paths.Save, Paths.Safe(a.Name)));
            Ok("Conta apagada." + (admin ? " Abra o servidor para a conta admin voltar do padrao." : ""));
            return true;
        }

        static void DeleteCharRows(RiceContext rc, long cid)
        {
            rc.Items.RemoveRange(rc.Items.Where(i => i.CID == cid).ToList());
            rc.Vehicles.RemoveRange(rc.Vehicles.Where(v => v.CID == cid).ToList());
            rc.QuestStates.RemoveRange(rc.QuestStates.Where(q => q.CID == cid).ToList());
            var c = rc.Characters.Find(cid);
            if (c != null) rc.Characters.Remove(c);
        }

        static void CharMenu(Acc a, long cid)
        {
            while (true)
            {
                Server.Database.Models.Character c;
                List<Server.Database.Models.Vehicle> cars;
                using (var rc = Database.GetContext())
                {
                    c = rc.Characters.Find(cid);
                    if (c == null) return;
                    cars = rc.Vehicles.Where(v => v.CID == cid).OrderBy(v => v.CarID).ToList();
                }
                Header("Corredor " + c.Name + " (conta " + a.Name + ")");
                Intro("1 muda o nome, 2 o nivel (a exp acompanha), 3 o mito, 4 o V de um carro da garagem,",
                      "5 apaga o corredor e 6 abre a pasta dele.");
                Console.WriteLine(" Nivel {0} | {1:N0} mito | {2} carro(s)", c.Level, c.Mito, cars.Count);
                foreach (var v in cars)
                    Console.WriteLine("   carro {0}: {1} V{2}{3}", v.CarID, CarName(v.CarType), v.Grade, v.CarID == c.CurrentCarID ? "  (atual)" : "");
                Console.WriteLine();
                Console.WriteLine(" 1) Renomear   2) Mudar nivel   3) Mudar mito   4) Mudar o V de um carro");
                Console.WriteLine(" 5) Apagar o corredor   6) Abrir a pasta   0) Voltar");
                switch (Ask("Escolha"))
                {
                    case "1": RenameChar(a, c); break;
                    case "2":
                        {
                            int lv; if (!int.TryParse(Ask("Nivel novo (1-149)"), out lv) || lv < 1 || lv > 149 || !CanWrite()) break;
                            using (var rc = Database.GetContext()) { var x = rc.Characters.Find(cid); x.Level = lv; x.Experience = ExpForLevel(lv); rc.SaveChanges(); }
                            Ok("Nivel mudado para " + lv + ".");
                            break;
                        }
                    case "3":
                        {
                            long m; if (!long.TryParse(Ask("Mito novo").Replace(".", "").Replace(",", ""), out m) || m < 0 || !CanWrite()) break;
                            using (var rc = Database.GetContext()) { var x = rc.Characters.Find(cid); x.Mito = m; rc.SaveChanges(); }
                            Ok("Mito mudado para " + m.ToString("N0") + ".");
                            break;
                        }
                    case "4":
                        {
                            int id, g;
                            if (!int.TryParse(Ask("Numero do carro (carro N da lista)"), out id) || !int.TryParse(Ask("V novo (1-9)"), out g) || g < 1 || g > 9 || !CanWrite()) break;
                            using (var rc = Database.GetContext())
                            {
                                var v = rc.Vehicles.FirstOrDefault(x => x.CID == cid && x.CarID == id);
                                if (v == null) { Warn("Carro nao encontrado."); Pause(); break; }
                                v.Grade = g; rc.SaveChanges();
                            }
                            Ok("Carro " + id + " agora e V" + g + ".");
                            break;
                        }
                    case "5":
                        if (!CanWrite()) break;
                        Warn("Apagar o corredor \"" + c.Name + "\" (carros, itens e progresso)? Nao tem volta.");
                        if (c.Name == cfg.UppedProfileName) Warn("E o perfil upado: ao abrir o servidor ele volta do padrao.");
                        if (!Confirm("Digite APAGAR para confirmar", "APAGAR")) break;
                        using (var rc = Database.GetContext()) { DeleteCharRows(rc, cid); rc.SaveChanges(); }
                        DeleteDir(Path.Combine(Paths.Save, Paths.Safe(a.Name), Paths.Safe(c.Name)));
                        Ok("Corredor apagado.");
                        return;
                    case "6": OpenFolder(Path.GetFullPath(Path.Combine(Paths.Save, Paths.Safe(a.Name), Paths.Safe(c.Name)))); break;
                    case "0": case "": return;
                }
            }
        }

        static void RenameChar(Acc a, Server.Database.Models.Character c)
        {
            if (!CanWrite()) return;
            string name = Ask("Nome novo do corredor (atual: " + c.Name + ")");
            if (string.IsNullOrWhiteSpace(name) || name == c.Name || name.Length > 16) { if (name != null && name.Length > 16) { Warn("Maximo de 16 letras."); Pause(); } return; }
            using (var rc = Database.GetContext())
            {
                if (rc.Characters.Any(x => x.Name == name)) { Warn("Ja existe um corredor com esse nome."); Pause(); return; }
                var x2 = rc.Characters.Find(c.ID);
                x2.Name = name;
                rc.SaveChanges();
            }
            MoveDir(Path.Combine(Paths.Save, Paths.Safe(a.Name), Paths.Safe(c.Name)), Path.Combine(Paths.Save, Paths.Safe(a.Name), Paths.Safe(name)));
            if (c.Name == cfg.UppedProfileName) { SetConfig("UppedProfileName", name); cfg.UppedProfileName = name; }
            Ok("Corredor renomeado.");
        }

        // mesma tabela do servidor (RiceServer.Initialize)
        // o servidor sobe de nivel enquanto Exp > soma(nivel); entao o nivel L comeca em soma(L-1) + 1
        static long ExpForLevel(int level)
        {
            long sum = 50;                                                    // soma(0)
            for (int i = 1; i < level; i++) sum += (long) (100 + Math.Pow(i, 2.25) * 1.5 + Math.Pow(i, 20) / Math.Pow(10, 33));
            return sum + 1;
        }

        // ------------------------------------------------------------------------------------------------ bots
        static JArray LoadJsonArray(string path)
        {
            if (!File.Exists(path)) return null;
            return JArray.Parse(File.ReadAllText(path));
        }
        static void SaveJson(string path, JToken t)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, t.ToString(Formatting.Indented));
        }

        static void BotsMenu()
        {
            string path = Paths.World("bots.json");
            while (true)
            {
                var bots = LoadJsonArray(path);
                Header("Bots");
                Intro("Os bots sao os jogadores simulados que andam pela cidade, correm, negociam e entram na sua party.",
                      "Digite o nome (ou parte) de um bot para mudar ele, L para ver todos, R para recriar todos do zero.");
                if (bots == null) { Warn("Ainda nao tem bots (abra o servidor uma vez para criar)."); Pause(); return; }
                Console.WriteLine(" {0} bots. Digite parte do nome para procurar, ou:", bots.Count);
                Console.WriteLine(" L) Listar todos   R) Recriar todos os bots do zero   0) Voltar");
                string s = Ask("Procurar");
                if (s == "0" || s == "") return;
                if (s.Equals("r", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CanWrite()) continue;
                    Warn("Apaga todos os bots (amizades, crews e itens deles). Ao abrir o servidor eles sao criados de novo.");
                    if (!Confirm("Digite APAGAR para confirmar", "APAGAR")) continue;
                    File.Delete(path);
                    Ok("Bots apagados. Abra o servidor para criar de novo.");
                    return;
                }
                var found = bots.OfType<JObject>().Where(b => s.Equals("l", StringComparison.OrdinalIgnoreCase)
                                                              || ((string) b["Name"] ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (found.Count == 0) { Warn("Nenhum bot com esse nome."); Pause(); continue; }
                for (int i = 0; i < found.Count; i++)
                    Console.WriteLine(" {0,3}) {1,-16} nivel {2,3}  {3} V{4}  crew {5}", i + 1, (string) found[i]["Name"], (int?) found[i]["Level"] ?? 0,
                                      CarName((int?) found[i]["Car"] ?? 0), BotGrade(found[i]), (string) found[i]["CrewName"] ?? "");
                int k;
                if (!int.TryParse(Ask("Numero do bot (0 volta)"), out k) || k < 1 || k > found.Count) continue;
                BotMenu(path, (long) found[k - 1]["Cid"]);
            }
        }

        static int BotGrade(JObject b)
        {
            int g = (int?) b["CarGrade"] ?? 0;
            return g > 0 ? g : CarLevel((int?) b["Car"] ?? 0);
        }

        static void BotMenu(string path, long cid)
        {
            while (true)
            {
                var bots = LoadJsonArray(path);
                var b = bots.OfType<JObject>().FirstOrDefault(x => (long?) x["Cid"] == cid);
                if (b == null) return;
                Header("Bot " + (string) b["Name"]);
                Intro("Personalidade muda o jeito que ele fala e joga; Habilidade (0,5 a 2; 1 = normal) o quanto ele corre bem;",
                      "Cidade onde ele mora. O carro e o V tambem aparecem para voce no jogo.");
                Console.WriteLine(" Nivel: {0}   Carro: {1} ({2}) V{3}   Personalidade: {4}", (int?) b["Level"], CarName((int?) b["Car"] ?? 0), (int?) b["Car"], BotGrade(b), (string) b["Personality"]);
                Console.WriteLine(" Crew: {0}   Cidade: {1}   Habilidade: {2}   Mito: {3}", (string) b["CrewName"], CityName((int?) b["HomeCity"] ?? 0), (double?) b["Skill"] ?? 1, (long?) b["Mito"] ?? -1);
                if ((bool?) b["Special"] == true) Console.WriteLine(" (bot fixo: algumas coisas voltam ao padrao quando o servidor abre)");
                Console.WriteLine();
                Console.WriteLine(" 1) Nome   2) Nivel   3) Carro   4) V do carro   5) Personalidade   6) Cidade");
                Console.WriteLine(" 7) Habilidade   8) Mito   9) Apagar o bot   0) Voltar");
                string op = Ask("Escolha");
                if (op == "0" || op == "") return;
                if (!CanWrite()) continue;
                switch (op)
                {
                    case "1":
                        {
                            string n = Ask("Nome novo");
                            if (string.IsNullOrWhiteSpace(n) || n.Length > 16) break;
                            if (bots.OfType<JObject>().Any(x => string.Equals((string) x["Name"], n, StringComparison.OrdinalIgnoreCase))) { Warn("Ja existe um bot com esse nome."); Pause(); break; }
                            b["Name"] = n; break;
                        }
                    case "2": { int v; if (int.TryParse(Ask("Nivel (1-149)"), out v) && v >= 1 && v <= 149) b["Level"] = v; break; }
                    case "3":
                        {
                            string q = Ask("Nome ou numero do carro");
                            int id = FindCar(q);
                            if (id <= 0) { Warn("Carro nao encontrado."); Pause(); break; }
                            if (Array.IndexOf(cfg.PoliceCars ?? new int[0], id) >= 0) { Warn("Carro de policia e so do admin."); Pause(); break; }
                            b["Car"] = id; b["CarGrade"] = 0; b["CustomLook"] = false;
                            Console.WriteLine(" Carro: " + CarName(id));
                            break;
                        }
                    case "4": { int v; if (int.TryParse(Ask("V (1-9)"), out v) && v >= 1 && v <= 9) b["CarGrade"] = v; break; }
                    case "5":
                        {
                            string v = Ask("amigavel, competitivo, comerciante ou calado").Trim().ToLowerInvariant();
                            if (new[] { "amigavel", "competitivo", "comerciante", "calado" }.Contains(v)) b["Personality"] = v;
                            break;
                        }
                    case "6": { int v; if (int.TryParse(Ask("Cidade (1 Moon Palace, 2 Koinonia, 3 Cras, 4 Oros)"), out v) && v >= 1 && v <= 4) b["HomeCity"] = v; break; }
                    case "7": { double v; if (double.TryParse(Ask("Habilidade (0,5 a 2; 1 = normal)").Replace(',', '.'), NumberStyles.Float, Inv, out v) && v >= 0.3 && v <= 3) b["Skill"] = v; break; }
                    case "8": { long v; if (long.TryParse(Ask("Mito"), out v) && v >= 0) b["Mito"] = v; break; }
                    case "9":
                        if (!Confirm("Apagar o bot " + (string) b["Name"] + "? Digite APAGAR", "APAGAR")) break;
                        b.Remove();
                        SaveJson(path, bots);
                        Ok("Bot apagado.");
                        return;
                }
                SaveJson(path, bots);
            }
        }

        // ------------------------------------------------------------------------------------------------ crews
        static void CrewsMenu()
        {
            string path = Paths.World("crews.json");
            while (true)
            {
                var crews = LoadJsonArray(path);
                Header("Crews");
                Intro("As crews dos bots (voce pode entrar nelas pelo chat com !!entrar <crew>).",
                      "Escolha o numero de uma crew para mudar o nome, a descricao ou o nivel minimo para entrar.");
                if (crews == null || crews.Count == 0) { Warn("Nenhuma crew ainda."); Pause(); return; }
                var list = crews.OfType<JObject>().ToList();
                for (int i = 0; i < list.Count; i++)
                    Console.WriteLine(" {0,3}) {1,-16} {2} membro(s){3}  lider: {4}", i + 1, (string) list[i]["Name"], ((JArray) list[i]["Members"] ?? new JArray()).Count,
                                      (bool?) list[i]["IsBot"] == true ? " (bots)" : "", (string) list[i]["MasterName"]);
                int k;
                if (!int.TryParse(Ask("Numero da crew (0 volta)"), out k) || k < 1 || k > list.Count) return;
                var c = list[k - 1];
                Header("Crew " + (string) c["Name"]);
                Console.WriteLine(" Descricao: {0}", (string) c["Desc"]);
                Console.WriteLine(" 1) Nome   2) Descricao   3) Nivel minimo para entrar   0) Voltar");
                string op = Ask("Escolha");
                if (op == "0" || op == "" || !CanWrite()) continue;
                if (op == "1")
                {
                    string n = Ask("Nome novo");
                    if (string.IsNullOrWhiteSpace(n) || n.Length > 16) continue;
                    string old = (string) c["Name"];
                    c["Name"] = n;
                    // os bots da crew levam o nome novo
                    string bp = Paths.World("bots.json");
                    var bots = LoadJsonArray(bp);
                    if (bots != null)
                    {
                        foreach (var b in bots.OfType<JObject>().Where(bb => (string) bb["CrewName"] == old)) b["CrewName"] = n;
                        SaveJson(bp, bots);
                    }
                }
                else if (op == "2") c["Desc"] = Ask("Descricao nova");
                else if (op == "3") { int v; if (int.TryParse(Ask("Nivel minimo"), out v) && v >= 0) c["MinLevel"] = v; }
                SaveJson(path, crews);
                Ok("Crew salva.");
            }
        }

        // ------------------------------------------------------------------------------------------------ config
        // Todas as configuracoes do Config.cs (com a explicacao de res/config_ajuda.json). So o que voce mudar vai para o
        // server.json - o resto continua no padrao (e acompanha as atualizacoes).
        static Dictionary<string, string[]> help;
        static Dictionary<string, string[]> Help()
        {
            if (help != null) return help;
            try { help = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(Path.Combine("res", "config_ajuda.json"))); }
            catch (Exception) { help = new Dictionary<string, string[]>(); }
            return help;
        }

        static List<FieldInfo> ConfigFields()
        {
            return typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(bool) || f.FieldType == typeof(int) || f.FieldType == typeof(long) || f.FieldType == typeof(float)
                         || f.FieldType == typeof(double) || f.FieldType == typeof(string) || f.FieldType == typeof(ushort) || f.FieldType == typeof(uint)
                         || f.FieldType == typeof(int[]))
                .ToList();
        }

        static string Show(object v)
        {
            if (v == null) return "";
            var arr = v as int[];
            if (arr != null) return string.Join(", ", arr);
            if (v is bool) return Lang.En ? ((bool) v ? "yes" : "no") : ((bool) v ? "sim" : "nao");
            if (v is float) return ((float) v).ToString(Inv);
            if (v is double) return ((double) v).ToString(Inv);
            return v.ToString();
        }

        static JObject ReadServerJson()
        {
            try { return File.Exists("server.json") ? JObject.Parse(File.ReadAllText("server.json")) : new JObject(); }
            catch (Exception) { return new JObject(); }
        }

        static void SetConfig(string key, object value)
        {
            var o = ReadServerJson();
            var def = typeof(Config).GetField(key) != null ? typeof(Config).GetField(key).GetValue(new Config()) : null;
            if (def != null && JToken.FromObject(def).ToString() == JToken.FromObject(value).ToString()) o.Remove(key);
            else o[key] = JToken.FromObject(value);
            File.WriteAllText("server.json", o.ToString(Formatting.Indented));
        }

        static void ConfigMenu()
        {
            var fields = ConfigFields();
            var h = Help();
            var groups = fields.GroupBy(f => { string[] x; return h.TryGetValue(f.Name, out x) && x.Length > 0 && !string.IsNullOrEmpty(x[0]) ? x[0] : "Outros"; })
                               .ToList();
            while (true)
            {
                Header("Configuracoes do servidor");
                Intro("Cada assunto abre a lista das opcoes dele, com o valor atual e a explicacao de cada uma.",
                      "B busca uma opcao pelo nome ou por uma palavra da explicacao; P volta tudo ao padrao.");
                Console.WriteLine(" Valores com * foram mudados (estao no server.json). O servidor le ao abrir.");
                Console.WriteLine();
                for (int i = 0; i < groups.Count; i++)
                {
                    Console.WriteLine(" {0,2}) {1} ({2})", i + 1, groups[i].Key, groups[i].Count());
                    string cd;
                    if (CategoryHelp.TryGetValue(groups[i].Key, out cd)) Info("       " , cd);
                }
                Console.WriteLine();
                Console.WriteLine(" B) Buscar pelo nome   P) Voltar tudo ao padrao   0) Voltar");
                string s = Ask("Escolha");
                if (s == "0" || s == "") return;
                if (s.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CanWrite()) continue;
                    if (!Confirm("Apagar o server.json (tudo volta ao padrao)? Digite SIM", "SIM")) continue;
                    if (File.Exists("server.json")) File.Copy("server.json", "server.json.antes_do_padrao", true);
                    File.Delete("server.json");
                    Ok("Pronto (copia em server.json.antes_do_padrao).");
                    continue;
                }
                List<FieldInfo> list;
                if (s.Equals("b", StringComparison.OrdinalIgnoreCase))
                {
                    string q = Ask("Parte do nome ou da explicacao");
                    list = fields.Where(f => f.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                          || (h.ContainsKey(f.Name) && h[f.Name].Length > 1 && (h[f.Name][1] ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                }
                else
                {
                    int k; if (!int.TryParse(s, out k) || k < 1 || k > groups.Count) continue;
                    list = groups[k - 1].ToList();
                }
                ConfigList(list);
            }
        }

        static void ConfigList(List<FieldInfo> list)
        {
            var h = Help();
            while (true)
            {
                var cur = CurrentConfig();
                var def = new Config();
                var sj = ReadServerJson();
                Header("Configuracoes");
                Intro("Digite o numero da opcao para mudar. Sim/nao, numero ou texto; P volta aquela opcao ao padrao.");
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    string[] x; string d = h.TryGetValue(f.Name, out x) && x.Length > 1 ? x[1] : "";
                    Console.WriteLine(" {0,3}) {1}{2} = {3}", i + 1, f.Name, sj[f.Name] != null ? " *" : "", Show(f.GetValue(cur)));
                    if (!string.IsNullOrEmpty(d)) Console.WriteLine("        {0}", Cut(Lang.T(d), 110));
                }
                int k;
                if (!int.TryParse(Ask("Numero para mudar (0 volta)"), out k) || k < 1 || k > list.Count) return;
                var fi = list[k - 1];
                if (!CanWrite()) continue;
                Console.WriteLine(" {0}: agora {1} (padrao {2})", fi.Name, Show(fi.GetValue(cur)), Show(fi.GetValue(def)));
                string v = Ask(fi.FieldType == typeof(bool) ? "sim / nao (P = padrao)" : fi.FieldType == typeof(int[]) ? "numeros separados por virgula (P = padrao)" : "valor novo (P = padrao)");
                if (v == "") continue;
                if (v.Equals("p", StringComparison.OrdinalIgnoreCase)) { var o = ReadServerJson(); o.Remove(fi.Name); File.WriteAllText("server.json", o.ToString(Formatting.Indented)); continue; }
                object val;
                if (!Parse(fi.FieldType, v, out val)) { Warn("Valor invalido."); Pause(); continue; }
                SetConfig(fi.Name, val);
            }
        }

        static Config CurrentConfig()
        {
            var c = new Config();
            try { if (File.Exists("server.json")) JsonConvert.PopulateObject(File.ReadAllText("server.json"), c); } catch (Exception) { }
            return c;
        }

        static bool Parse(Type t, string s, out object v)
        {
            v = null;
            s = s.Trim();
            if (t == typeof(bool))
            {
                var l = s.ToLowerInvariant();
                if (l == "sim" || l == "s" || l == "yes" || l == "y" || l == "true" || l == "1") { v = true; return true; }
                if (l == "nao" || l == "n" || l == "no" || l == "false" || l == "0") { v = false; return true; }
                return false;
            }
            if (t == typeof(string)) { v = s; return true; }
            if (t == typeof(int[]))
            {
                var parts = s.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var r = new List<int>();
                foreach (var p in parts) { int x; if (!int.TryParse(p, out x)) return false; r.Add(x); }
                v = r.ToArray(); return true;
            }
            s = s.Replace(',', '.');
            double d;
            if (!double.TryParse(s, NumberStyles.Float, Inv, out d)) return false;
            try
            {
                if (t == typeof(int)) v = (int) d;
                else if (t == typeof(long)) v = (long) d;
                else if (t == typeof(float)) v = (float) d;
                else if (t == typeof(double)) v = d;
                else if (t == typeof(ushort)) v = (ushort) d;
                else if (t == typeof(uint)) v = (uint) d;
                else return false;
            }
            catch (Exception) { return false; }
            return true;
        }

        // ------------------------------------------------------------------------------------------------ eventos
        static void EventMenu()
        {
            var op = new[] { new[] { "nenhum", "Nenhum (sem evento)" }, new[] { "natal", "Natal: neve, arvore de Natal e roupa de Papai Noel" },
                             new[] { "valentine", "Valentine (Dia dos Namorados)" }, new[] { "halloween", "Halloween" },
                             new[] { "auto", "Automatico pelo mes (padrao): outubro Halloween, dezembro Natal, fevereiro Valentine" } };
            while (true)
            {
                string now = CurrentConfig().Evento ?? "";
                Header("Evento especial");
                Intro("Liga o visual de evento do proprio jogo (neve, arvore de Natal, roupas) e as falas dos bots sobre o evento.",
                      "No automatico cada evento dura o mes dele: outubro Halloween, dezembro Natal, fevereiro Valentine.");
                Console.WriteLine(" Agora: {0}", op.Where(x => x[0].Equals(now, StringComparison.OrdinalIgnoreCase)).Select(x => x[1]).FirstOrDefault() ?? now);
                if (now.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(" Este mes: {0}", Rice.Server.Packets.Static.EventoAtivo("auto", DateTime.Now));
                Console.WriteLine(" Vale quando voce entra no jogo (se estiver jogando, saia e entre de novo).");
                Console.WriteLine(" Pascoa: o jogo nao tem visual de Pascoa; o evento dela e a entrega do coelho nas missoes.");
                Console.WriteLine();
                for (int i = 0; i < op.Length; i++) Console.WriteLine(" {0}) {1}", i + 1, op[i][1]);
                Console.WriteLine(" 0) Voltar");
                int k;
                if (!int.TryParse(Ask("Escolha"), out k) || k < 1 || k > op.Length) return;
                if (!CanWrite()) continue;
                SetConfig("Evento", op[k - 1][0]);
                Ok("Evento: " + op[k - 1][1]);
            }
        }

        // ------------------------------------------------------------------------------------------------ idioma
        static void LanguageMenu()
        {
            Header("Idioma / Language");
            System.Console.WriteLine(" 1) English (default)");
            System.Console.WriteLine(" 2) Portugues (Brasil)");
            System.Console.WriteLine(" 0) Voltar / Back");
            string s = Ask("Escolha");
            if (s != "1" && s != "2") return;
            if (!CanWrite()) return;
            Lang.Language = s == "2" ? "pt" : "en";
            SetConfig("Language", Lang.Language);
            Ok(Lang.En ? "Language: English. The server uses it the next time it opens." : "Idioma: portugues. O servidor usa na proxima vez que abrir.");
        }

        // ------------------------------------------------------------------------------------------------ graficos
        // Data\Init\goption.ini do jogo (secao [Graphic]). O Start.bat guarda uma copia (goption_salvo.ini) e coloca de
        // volta antes de abrir o jogo: o menu grava nos dois. Backup na primeira vez: goption.ini.antes_do_menu.
        static string GameIni()
        {
            foreach (var root in new[] { "..", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."), "." })
            {
                string p = Path.GetFullPath(Path.Combine(root, "Data", "Init", "goption.ini"));
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static string IniGet(List<string> lines, string key)
        {
            bool inG = false;
            foreach (var l in lines)
            {
                string t = l.Trim();
                if (t.StartsWith("[")) { inG = t.Equals("[Graphic]", StringComparison.OrdinalIgnoreCase); continue; }
                int i = t.IndexOf('=');
                if (inG && i > 0 && t.Substring(0, i).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return t.Substring(i + 1).Trim();
            }
            return null;
        }

        static void IniSet(List<string> lines, string key, string value)
        {
            bool inG = false; int end = -1;
            for (int n = 0; n < lines.Count; n++)
            {
                string t = lines[n].Trim();
                if (t.StartsWith("["))
                {
                    if (inG) { end = n; break; }
                    inG = t.Equals("[Graphic]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                int i = t.IndexOf('=');
                if (inG && i > 0 && t.Substring(0, i).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[n] = key + "=" + value; return; }
            }
            if (end < 0) end = lines.Count;
            lines.Insert(end, key + "=" + value);
        }

        static void GraphicsMenu()
        {
            string ini = GameIni();
            if (ini == null) { Warn("Nao achei o Data\\Init\\goption.ini do jogo (abra o jogo uma vez antes)."); Pause(); return; }
            string saved = Path.Combine(Path.GetDirectoryName(ini), "goption_salvo.ini");
            string[] res = { "1024x768", "1280x720", "1280x800", "1280x1024", "1360x768", "1366x768", "1440x900", "1600x900", "1680x1050", "1920x1080", "2560x1440" };
            while (true)
            {
                var lines = File.ReadAllLines(ini).ToList();
                Header("Graficos do jogo");
                Intro("Muda o Data\\Init\\goption.ini do jogo (feche o jogo antes). Interface menor = resolucao maior;",
                      "8 volta tudo como estava antes de mexer aqui.");
                if (Running("DriftCity")) { Warn("O jogo esta aberto: feche ele antes (ele grava por cima quando fecha)."); Console.WriteLine(); }
                Console.WriteLine(" Arquivo: {0}", ini);
                Console.WriteLine();
                Console.WriteLine(" 1) Resolucao: {0} x {1}", IniGet(lines, "ScreenWidth"), IniGet(lines, "ScreenHeight"));
                Console.WriteLine(" 2) Tela cheia: {0}", Show(IniGet(lines, "Fullscreen") == "true"));
                Console.WriteLine(" 3) Antisserrilhado (MultiSampling): {0}", IniGet(lines, "MultiSampling"));
                Console.WriteLine(" 4) Sombras (ShadowQuality 0-3): {0}", IniGet(lines, "ShadowQuality"));
                Console.WriteLine(" 5) Distancia de visao (VisibleRange 1-4): {0}", IniGet(lines, "VisibleRange"));
                Console.WriteLine(" 6) Detalhes (carro, cenario, efeitos e texturas, 1-4): {0}", IniGet(lines, "CarDetail"));
                Console.WriteLine(" 7) Mudar qualquer item da secao [Graphic]");
                Console.WriteLine(" 8) Voltar os graficos como estavam antes do menu");
                Console.WriteLine(" 9) Tamanho da interface (HUD e menus do jogo)");
                Console.WriteLine(" 0) Voltar");
                string s = Ask("Escolha");
                if (s == "0" || s == "") return;
                if (s == "8")
                {
                    string bk = ini + ".antes_do_menu";
                    if (!File.Exists(bk)) { Warn("Nao tem backup (o menu ainda nao mudou os graficos)."); Pause(); continue; }
                    File.Copy(bk, ini, true);
                    if (File.Exists(saved)) File.Copy(bk, saved, true);
                    Ok("Graficos voltaram como estavam.");
                    continue;
                }
                var ch = new Dictionary<string, string>();
                int v;
                switch (s)
                {
                    case "1":
                        for (int i = 0; i < res.Length; i++) Console.WriteLine(" {0,2}) {1}", i + 1, res[i]);
                        string r = Ask("Numero da lista ou LARGURAxALTURA (ex.: 1920x1080)");
                        int k;
                        if (int.TryParse(r, out k) && k >= 1 && k <= res.Length) r = res[k - 1];
                        var wh = r.ToLowerInvariant().Split('x');
                        int w, h;
                        if (wh.Length != 2 || !int.TryParse(wh[0].Trim(), out w) || !int.TryParse(wh[1].Trim(), out h) || w < 640 || h < 480) { Warn("Valor invalido."); Pause(); continue; }
                        ch["ScreenWidth"] = w.ToString(); ch["ScreenHeight"] = h.ToString();
                        break;
                    case "2":
                    {
                        object b;
                        if (!Parse(typeof(bool), Ask("sim / nao"), out b)) { Warn("Valor invalido."); Pause(); continue; }
                        ch["Fullscreen"] = (bool) b ? "true" : "false";
                        break;
                    }
                    case "3":
                        if (!int.TryParse(Ask("0, 2, 4 ou 8"), out v) || (v != 0 && v != 2 && v != 4 && v != 8)) { Warn("Valor invalido."); Pause(); continue; }
                        ch["MultiSampling"] = v.ToString();
                        break;
                    case "4":
                        if (!int.TryParse(Ask("0 a 3"), out v) || v < 0 || v > 3) { Warn("Valor invalido."); Pause(); continue; }
                        ch["ShadowQuality"] = v.ToString(); ch["VehicleShadow"] = v.ToString();
                        break;
                    case "5":
                        if (!int.TryParse(Ask("1 a 4"), out v) || v < 1 || v > 4) { Warn("Valor invalido."); Pause(); continue; }
                        ch["VisibleRange"] = v.ToString();
                        break;
                    case "6":
                        if (!int.TryParse(Ask("1 a 4"), out v) || v < 1 || v > 4) { Warn("Valor invalido."); Pause(); continue; }
                        foreach (var key in new[] { "CarDetail", "EnvironmentDetail", "EffectDetail", "CarTextureQuality" }) ch[key] = v.ToString();
                        foreach (var key in new[] { "ObjectTextureQuality", "TerrainTextureQuality" }) ch[key] = Math.Min(v, 3).ToString();
                        break;
                    case "9":
                    {
                        // o jogo desenha os menus em tamanho fixo (1024x768): em tela cheia, resolucao menor = tudo maior
                        // (o monitor estica a imagem para a tela inteira)
                        Console.WriteLine();
                        Console.WriteLine(" O jogo nao tem escala de interface: os menus tem tamanho fixo (1024x768).");
                        Console.WriteLine(" Em tela cheia, uma resolucao menor deixa tudo maior (o monitor estica a imagem).");
                        Console.WriteLine();
                        Console.WriteLine(" 1) Grande  - 1366 x 768 (os menus quase enchem a tela)");
                        Console.WriteLine(" 2) Media   - 1600 x 900");
                        Console.WriteLine(" 3) Pequena - 1920 x 1080 (mais nitido, menus pequenos)");
                        string q = Ask("Escolha");
                        string[] sizes = { "1366x768", "1600x900", "1920x1080" };
                        int qi;
                        if (!int.TryParse(q, out qi) || qi < 1 || qi > 3) continue;
                        var sz = sizes[qi - 1].Split('x');
                        ch["ScreenWidth"] = sz[0]; ch["ScreenHeight"] = sz[1]; ch["Fullscreen"] = "true";
                        break;
                    }
                    case "7":
                    {
                        bool inG = false;
                        foreach (var l in lines)
                        {
                            string t = l.Trim();
                            if (t.StartsWith("[")) { inG = t.Equals("[Graphic]", StringComparison.OrdinalIgnoreCase); continue; }
                            if (inG && t.Contains("=")) Console.WriteLine("   " + t);
                        }
                        string key = Ask("Nome do item (ex.: FogEnable)");
                        if (key == "" || IniGet(lines, key) == null) { Warn("Item nao encontrado."); Pause(); continue; }
                        string val = Ask("Valor novo (agora " + IniGet(lines, key) + ")");
                        if (val == "") continue;
                        ch[key] = val;
                        break;
                    }
                    default: continue;
                }
                if (!File.Exists(ini + ".antes_do_menu")) File.Copy(ini, ini + ".antes_do_menu");
                foreach (var kv in ch) IniSet(lines, kv.Key, kv.Value);
                File.WriteAllLines(ini, lines);
                // a copia do Start.bat (senao ele coloca a antiga de volta ao abrir o jogo)
                if (File.Exists(saved))
                {
                    var sl = File.ReadAllLines(saved).ToList();
                    foreach (var kv in ch) IniSet(sl, kv.Key, kv.Value);
                    File.WriteAllLines(saved, sl);
                }
                Ok("Graficos salvos. Valem na proxima vez que o jogo abrir.");
            }
        }

        static bool Running(string name)
        {
            try { return Process.GetProcessesByName(name).Length > 0; } catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------------------------------------ mods
        // mod ligado = o arquivo na pasta do Rice.exe; desligado = o mesmo arquivo com ".desligado" (guarda os seus ajustes);
        // o original fica em res/mods
        const string LadderMod = "mod_escada_de_crews.json";
        static void ModsMenu()
        {
            string off = LadderMod + ".desligado", orig = Path.Combine("res", "mods", LadderMod);
            while (true)
            {
                bool on = File.Exists(LadderMod), inst = on || File.Exists(off);
                Header("Mods");
                Intro("Escada de crews: cada cidade tem crews de bots; desafie membros 1x1 na Battle Zone, venca o chefe",
                      "para ganhar premios e chegar na Most Wanted (Blacklist, chefe Razor). Detalhes em res\\mods\\README_MODS.md.");
                Console.WriteLine(" Escada de crews: crews por cidade, desafios mano a mano, chefes com premio e o Most Wanted.");
                Console.WriteLine(" (explicacao completa em res\\mods\\LEIA-ME_MODS.md)");
                Console.Write(" Situacao: ");
                if (on) Console.WriteLine("instalado e LIGADO"); else if (inst) Console.WriteLine("instalado, desligado"); else Console.WriteLine("nao instalado");
                Console.WriteLine(" Ligar/desligar refaz as crews dos bots na proxima vez que o servidor abrir.");
                Console.WriteLine();
                if (!inst) Console.WriteLine(" 1) Instalar e ligar");
                else Console.WriteLine(" 1) {0}", on ? "Desligar" : "Ligar");
                if (inst) Console.WriteLine(" 2) Voltar os numeros do mod ao padrao   3) Desinstalar");
                Console.WriteLine(" 0) Voltar");
                string s = Ask("Escolha");
                if (s == "0" || s == "") return;
                if (!CanWrite()) continue;
                if (s == "1")
                {
                    if (!inst)
                    {
                        if (!File.Exists(orig)) { Warn("Nao achei " + orig + " (reinstale o Offline Aprimorado)."); Pause(); continue; }
                        File.Copy(orig, LadderMod, true);
                        Ok("Escada de crews instalada e ligada.");
                    }
                    else if (on) { if (File.Exists(off)) File.Delete(off); File.Move(LadderMod, off); Ok("Escada de crews desligada."); }
                    else { File.Move(off, LadderMod); Ok("Escada de crews ligada."); }
                }
                else if (s == "2" && inst)
                {
                    if (!File.Exists(orig)) { Warn("Nao achei " + orig + "."); Pause(); continue; }
                    File.Copy(orig, on ? LadderMod : off, true);
                    Ok("Numeros do mod voltaram ao padrao.");
                }
                else if (s == "3" && inst)
                {
                    if (!Confirm("Desinstalar a Escada de crews? Digite SIM", "SIM")) continue;
                    if (on) File.Delete(LadderMod);
                    if (File.Exists(off)) File.Delete(off);
                    Ok("Escada de crews desinstalada.");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ backup
        const string BackupDir = "backups";
        static void BackupMenu()
        {
            while (true)
            {
                Header("Backup dos saves (save, mundo, mapas e server.json)");
                Intro("F faz um backup agora (arquivo .zip na pasta backups). O numero de um backup restaura ele:",
                      "os saves atuais vao antes para um backup novo, entao nada se perde.");
                Directory.CreateDirectory(BackupDir);
                var files = Directory.GetFiles(BackupDir, "*.zip").OrderByDescending(f => f).ToList();
                for (int i = 0; i < files.Count; i++) Console.WriteLine(" {0,2}) {1}  ({2:N0} KB)", i + 1, Path.GetFileName(files[i]), new FileInfo(files[i]).Length / 1024);
                Console.WriteLine();
                Console.WriteLine(" F) Fazer backup agora   numero) Restaurar esse backup   0) Voltar");
                string s = Ask("Escolha");
                if (s == "0" || s == "") return;
                if (s.Equals("f", StringComparison.OrdinalIgnoreCase)) { string f = MakeBackup("manual"); Ok("Backup feito: " + f); continue; }
                int k;
                if (!int.TryParse(s, out k) || k < 1 || k > files.Count || !CanWrite()) continue;
                Warn("Restaurar " + Path.GetFileName(files[k - 1]) + "? Os saves de agora sao trocados (antes eu faco um backup deles).");
                if (!Confirm("Digite SIM", "SIM")) continue;
                MakeBackup("antes_de_restaurar");
                foreach (var d in new[] { Paths.Save, Paths.WorldDir, Paths.MapsDir }) DeleteDir(d);
                if (File.Exists("server.json")) File.Delete("server.json");
                ZipFile.ExtractToDirectory(files[k - 1], ".");
                Ok("Backup restaurado.");
            }
        }

        public static string MakeBackup(string tag)
        {
            Directory.CreateDirectory(BackupDir);
            string name = Path.Combine(BackupDir, "backup_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + tag + ".zip");
            using (var z = ZipFile.Open(name, ZipArchiveMode.Create))
            {
                foreach (var d in new[] { Paths.Save, Paths.WorldDir, Paths.MapsDir })
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        z.CreateEntryFromFile(f, f.Replace('\\', '/'));
                }
                if (File.Exists("server.json")) z.CreateEntryFromFile("server.json", "server.json");
            }
            return name;
        }

        // ------------------------------------------------------------------------------------------------ carros
        static JObject vehicles;
        static JObject Vehicles()
        {
            if (vehicles != null) return vehicles;
            try { vehicles = JObject.Parse(File.ReadAllText(Path.Combine("res", "VehicleList.json"))); } catch (Exception) { vehicles = new JObject(); }
            return vehicles;
        }
        static string CarName(int id)
        {
            var v = Vehicles()[id.ToString()] as JObject;
            return v != null ? ((string) v["car_name"] ?? ("carro " + id)) : "carro " + id;
        }
        static int CarLevel(int id)
        {
            var v = Vehicles()[id.ToString()] as JObject;
            int l; return v != null && int.TryParse((string) v["lvl"] ?? "1", out l) ? l : 1;
        }
        static int FindCar(string q)
        {
            int id;
            if (int.TryParse(q, out id)) return Vehicles()[id.ToString()] != null ? id : 0;
            var hits = Vehicles().Properties().Where(p => ((string) p.Value["car_name"] ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (hits.Count == 0) return 0;
            if (hits.Count == 1) return int.Parse(hits[0].Name);
            for (int i = 0; i < hits.Count && i < 30; i++) Console.WriteLine(" {0,3}) {1} ({2})", i + 1, (string) hits[i].Value["car_name"], hits[i].Name);
            int k;
            return int.TryParse(Ask("Qual"), out k) && k >= 1 && k <= Math.Min(30, hits.Count) ? int.Parse(hits[k - 1].Name) : 0;
        }
        static string CityName(int c)
        {
            switch (c) { case 1: return "Moon Palace"; case 2: return "Koinonia"; case 3: return "Cras"; case 4: return "Oros"; case 6: return "Neo City"; default: return "-"; }
        }

        // ------------------------------------------------------------------------------------------------ util
        static bool ServerRunning()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                return Process.GetProcessesByName("Rice").Any(p => p.Id != me);
            }
            catch (Exception) { return false; }
        }

        static bool CanWrite()
        {
            if (!ServerRunning()) return true;
            Warn("Feche o servidor (Rice.exe) primeiro.");
            Pause();
            return false;
        }

        static void MoveDir(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) { Directory.CreateDirectory(to); return; }
                if (Directory.Exists(to)) return;
                Directory.Move(from, to);
            }
            catch (Exception ex) { Warn("Pasta: " + ex.Message); }
        }

        static void DeleteDir(string d)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception ex) { Warn("Pasta: " + ex.Message); }
        }

        static void OpenFolder(string d)
        {
            try { Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); } catch (Exception) { Console.WriteLine(" " + d); Pause(); }
        }

        static string Cut(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 3) + "..."; }

        static void Header(string t)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" " + t);
            Console.WriteLine(" " + new string('-', Math.Min(78, t.Length + 2)));
            Console.ForegroundColor = ConsoleColor.Gray;
        }
        static string Ask(string p)
        {
            Console.Write(" " + p + ": ");
            return (Console.ReadLine() ?? "").Trim();
        }
        static bool Confirm(string p, string word)
        {
            string a = Ask(p);
            if (a.Equals(word, StringComparison.OrdinalIgnoreCase)) return true;
            // em ingles a palavra aparece traduzida (SIM = YES, APAGAR = DELETE)
            return (word == "SIM" && a.Equals("YES", StringComparison.OrdinalIgnoreCase))
                || (word == "APAGAR" && a.Equals("DELETE", StringComparison.OrdinalIgnoreCase));
        }

        // Console do menu: tudo que aparece passa pelo idioma escolhido (Lang)
        static class Console
        {
            public static string Title { get { return System.Console.Title; } set { System.Console.Title = Lang.T(value); } }
            public static ConsoleColor ForegroundColor { get { return System.Console.ForegroundColor; } set { System.Console.ForegroundColor = value; } }
            public static void Clear() { System.Console.Clear(); }
            public static string ReadLine() { return System.Console.ReadLine(); }
            public static void Write(string s) { System.Console.Write(Lang.T(s)); }
            public static void Write(string f, params object[] a) { System.Console.Write(Lang.T(f), Tr(a)); }
            public static void WriteLine() { System.Console.WriteLine(); }
            public static void WriteLine(string s) { System.Console.WriteLine(Lang.T(s)); }
            public static void WriteLine(string f, params object[] a) { System.Console.WriteLine(Lang.T(f), Tr(a)); }
            static object[] Tr(object[] a) { return a == null ? a : a.Select(x => x is string ? (object) Lang.T((string) x) : x).ToArray(); }
        }
        // opcao do menu com a explicacao embaixo (cinza)
        static void Opt(string key, string title, string desc)
        {
            Console.WriteLine(" {0}) {1}", key, title);
            if (!string.IsNullOrEmpty(desc)) Info("      ", desc);
        }
        static void Info(string indent, string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine(indent + Lang.T(text));
            Console.ForegroundColor = ConsoleColor.Gray;
        }
        static void Intro(params string[] lines)
        {
            foreach (var l in lines) Info(" ", l);
            Console.WriteLine();
        }
        static readonly Dictionary<string, string> CategoryHelp = new Dictionary<string, string>
        {
            { "Battle Zone", "Corridas da Battle Zone: bots nas salas, IA, recompensas, cartas e regras das arenas." },
            { "Cidade e trafego", "Bots andando nas cidades e no Driver Dome, trafego, clima e o que eles fazem na rua." },
            { "Neo City", "Neo City: bots da party, dano, vida, itens que caem dos HUVs e durabilidade dos equipamentos." },
            { "Bots", "Quantos bots, como evoluem, carros que usam, chat e o carro de policia." },
            { "Party", "Bots na sua party: seguir, ajudar em missoes, entregas, patrulhas e quanto eles ganham." },
            { "Combos", "Recompensas de combo e de nivel, drinks da Drift Shop e bonus." },
            { "Leilao", "Leilao: quantos anuncios, precos e em quanto tempo os bots compram o que voce vende." },
            { "Rush", "Rush Time: HUVs, tempo, recompensas e itens." },
            { "HUV", "Perseguicao de HUV na cidade: dano, itens que caem e recompensas." },
            { "Perfil upado", "Conta admin (RiceAdmin): carro de policia, pecas, itens e nivel." },
            { "Itens", "Itens: docks, options, Convert, producao (Skill Books) e cupons." },
            { "Crews", "Crews e licencas." },
            { "Rede", "Servidor: fechar sozinho junto com o jogo, portas e endereco (nao mude jogando sozinho) e modo de depuracao." },
            { "Contas", "Contas: login, nivel inicial e limites." },
            { "Lojas", "Lojas: Part Shop, loja de carros, precos e qualidade das pecas." },
            { "Economia", "Exp e mito ganhos no jogo." },
            { "Missoes", "Missoes e quests: recompensas." },
            { "Combustivel", "Combustivel dos carros: consumo e tanque." },
            { "Eventos", "Evento especial e idioma." },
        };
        static void Warn(string s) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(" " + s); Console.ForegroundColor = ConsoleColor.Gray; }
        static void Ok(string s) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(" " + s); Console.ForegroundColor = ConsoleColor.Gray; Pause(); }
        static void Pause() { Console.Write(" (Enter para continuar)"); Console.ReadLine(); }
    }
}
