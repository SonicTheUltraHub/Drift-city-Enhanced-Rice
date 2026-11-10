using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace Instalador
{
    // Instalador do Offline Aprimorado: um .exe so (pacote.zip dentro, montado ao compilar). NAO leva os arquivos do
    // jogo: so as diferencas (Instalador\modificacoes). O DriftCity.exe vai inteiro (o do mini-Rice ja modificado, abre sem launcher). Quem instala precisa ter o Drift City dele; o instalador confere cada
    // arquivo (md5), aplica a modificacao em cima do original do usuario e guarda backup. Tambem coloca o servidor
    // (pasta Rice), Start.bat, Menu.bat e LEIA-ME. Os saves de quem ja joga nao sao tocados.
    // Tambem desfaz (volta o ultimo backup).
    static class Program
    {
        const string Resource = "Instalador.pacote.zip";
        static string game;

        static void Main(string[] args)
        {
            Console.Title = "Install Enhanced Rice";
            try { Run(args); }
            catch (Exception ex) { Red(L(" Erro: ", " Error: ") + ex.Message); Pause(); }
        }

        static void Run(string[] args)
        {
            ChooseLanguage();
            game = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
            game = Path.GetFullPath(game.Trim().Trim('"')).TrimEnd('\\', '/');
            while (true)
            {
                Header();
                if (!IsGame(game))
                {
                    Red(L(" Esta pasta nao e do Drift City (falta DriftCity.exe, Data ou Data_US):", " This folder is not Drift City (missing DriftCity.exe, Data or Data_US):"));
                    Console.WriteLine(" " + game);
                    Console.WriteLine();
                    Console.WriteLine(L(" Coloque o Install Enhanced Rice.exe do lado do DriftCity.exe, ou digite a pasta do jogo.", " Put Install Enhanced Rice.exe next to DriftCity.exe, or type the game folder."));
                    string p = Ask(L("Pasta do jogo (Enter sai)", "Game folder (Enter exits)"));
                    if (p == "") return;
                    try { game = Path.GetFullPath(p.Trim('"')).TrimEnd('\\', '/'); } catch (Exception) { }
                    continue;
                }
                Console.WriteLine(L(" Jogo: ", " Game: ") + game);
                Console.WriteLine(L(" Versao: ", " Version: ") + Version());
                Console.WriteLine(" Offline Aprimorado: " + (Directory.Exists(Path.Combine(game, "Rice")) && File.Exists(Path.Combine(game, "Rice", "Rice.exe")) ? L("ja instalado (pode atualizar)", "already installed (you can update)") : L("nao instalado", "not installed")));
                Console.WriteLine();
                Console.WriteLine(L(" 1) Instalar / atualizar", " 1) Install / update"));
                Console.WriteLine(L(" 2) Desfazer a ultima instalacao (volta o backup)", " 2) Undo the last installation (restores the backup)"));
                Console.WriteLine(L(" 3) Arrumar os carros, a loja e o som de motor para este cliente", " 3) Fix the cars, the car shop and the engine sounds for this client"));
                Console.WriteLine(L(" 0) Sair", " 0) Exit"));
                switch (Ask(L("Escolha", "Choose")))
                {
                    case "1": Install(); break;
                    case "2": Undo(); break;
                    case "3":
                        if (Running("DriftCity") || Running("Rice")) { Red(L(" Feche o jogo e o servidor (Rice.exe) antes.", " Close the game and the server (Rice.exe) first.")); Pause(); break; }
                        {
                            string bk3 = Path.Combine(game, "backup_instalador", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                            FixCars(bk3);
                            FixEngineSounds(bk3);
                        }
                        Pause();
                        break;
                    case "0": case "": return;
                }
            }
        }

        // ---------------------------------------------------------------------------------------------- instalar
        // Dentro do .exe: as modificacoes (Instalador\\modificacoes: so as diferencas + manifest.lst) e, no pacote.zip, o resto do
        // Offline Aprimorado (Rice = servidor, .bat, LEIA-ME), que sao arquivos do projeto.
        class Job
        {
            public string Rel, Target, State, Note;       // State: novo, troca, igual, falta, outro
            public ZipArchiveEntry Entry;                 // arquivo do projeto (copia)
            public string BasePath, PatchName, Md5;       // arquivo do jogo (diferenca)
            public List<KeyValuePair<long, byte[]>> InPlace;  // (nao usado mais)
            public string FullName;                       // arquivo inteiro dentro do instalador (DriftCity.exe)
        }

        static void Install()
        {
            if (Running("DriftCity") || Running("Rice")) { Red(L(" Feche o jogo e o servidor (Rice.exe) antes.", " Close the game and the server (Rice.exe) first.")); Pause(); return; }
            using (var zip = OpenPackage())
            {
                if (zip == null) { Red(L(" Este Install Enhanced Rice.exe nao tem os arquivos dentro (compile a solucao de novo).", " This Install Enhanced Rice.exe has no files inside (build the solution again).")); Pause(); return; }
                Console.WriteLine();
                Console.WriteLine(L(" Conferindo os arquivos do jogo...", " Checking the game files..."));
                var jobs = new List<Job>();
                // 1) arquivos do jogo: aplicar a diferenca sobre o original do usuario
                var man = Mod("manifest.lst");
                if (man == null)
                {
                    Red(L(" Este Install Enhanced Rice.exe nao tem as modificacoes dos arquivos do jogo (Init_us.agt, Patch...).", " This Install Enhanced Rice.exe does not have the game file modifications (Init_us.agt, Patch...)."));
                    Red(L(" Falta a pasta Instalador\\modificacoes no projeto: extraia o projeto atualizado inteiro e compile de novo.", " The Instalador\\modificacoes folder is missing in the project: extract the whole updated project and build again."));
                    if (!Ask(L("Instalar so o servidor e os .bat mesmo assim? (S/N)", "Install only the server and the .bat files anyway? (Y/N)")).Yes()) return;
                }
                if (man != null)
                    using (var r = new StreamReader(man))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            var f = line.Split('|');
                            if (f.Length < 5) continue;
                            var j = new Job { Rel = f[0].Replace('/', '\\'), Md5 = f[2] };
                            j.Target = Path.Combine(game, j.Rel);
                            j.BasePath = Path.Combine(game, f[1].Replace('/', '\\'));
                            string cur = File.Exists(j.Target) ? Md5(j.Target) : null;
                            if (cur == j.Md5) { j.State = "igual"; jobs.Add(j); continue; }
                            if (j.Rel.Equals("DriftCity.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                // DriftCity.exe vai inteiro (o do mini-Rice ja modificado, abre sem o launcher): troca qualquer um
                                j.FullName = "DriftCity.exe.completo";
                                j.State = cur == null ? "novo" : "troca";
                                j.Note = L("o DriftCity.exe do Offline Aprimorado, abre sem o launcher", "the Offline Aprimorado DriftCity.exe, opens without the launcher");
                                jobs.Add(j); continue;
                            }
                            string bm = File.Exists(j.BasePath) ? (j.BasePath == j.Target ? cur : Md5(j.BasePath)) : null;
                            var from = f[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':')).Where(x => x.Length == 2).ToList();
                            // cliente com outro arranjo (ex.: openDC): o arquivo do Data_US fica no Data (Data\audio\bgm\music.txt)
                            if (bm == null && j.Rel.StartsWith("Data_US\\", StringComparison.OrdinalIgnoreCase))
                            {
                                string altRel = "Data\\" + j.Rel.Substring(8);
                                string alt = Path.Combine(game, altRel);
                                if (File.Exists(alt))
                                {
                                    string am = Md5(alt);
                                    if (am == j.Md5) { j.Rel = altRel; j.Target = alt; j.BasePath = alt; j.State = "igual"; jobs.Add(j); continue; }
                                    if (from.Any(x => x[0] == am)) { j.Rel = altRel; j.Target = alt; j.BasePath = alt; cur = am; bm = am; }
                                }
                            }
                            var hit = from.FirstOrDefault(x => x[0] == bm);
                            if (hit != null) { j.PatchName = hit[1]; j.State = cur == null ? "novo" : "troca"; }
                            // emblemas das crews: o servidor manda os emblemas sozinho; cliente sem a pasta teammark nao e erro
                            else if (bm == null && j.Rel.StartsWith("Data\\GameUI\\teammark", StringComparison.OrdinalIgnoreCase)) j.State = "opcional";
                            else if (bm == null) { j.State = "falta"; j.Note = L("o jogo nao tem ", "the game does not have ") + f[1]; }
                            else
                            {
                                j.State = "outro";
                                j.Note = L("versao diferente da que o instalador conhece", "a different version from the one the installer knows");
                                // Init_us.agt nosso que nao bate o md5: com a loja ajustada a este cliente (fica) ou mexido por uma
                                // instalacao anterior (volta o nosso inteiro; o ajuste de carros roda no fim)
                                if (j.Rel.Equals(@"Data_US\Init_us.agt", StringComparison.OrdinalIgnoreCase))
                                {
                                    int fam = Carros.InitFamily(j.Target);
                                    if (fam == 1) { j.State = "igual"; j.Note = null; }
                                    else if (fam == 2)
                                    {
                                        j.FullName = "Init_us.agt.completo"; j.State = "troca"; j.PatchName = null;
                                        j.Note = L("versao nossa mexida por uma instalacao anterior: volta a certa", "our version changed by an earlier installation: the right one goes back");
                                    }
                                }
                            }
                            jobs.Add(j);
                        }
                    }
                // 2) arquivos do projeto (servidor, .bat, LEIA-ME)
                foreach (var en in zip.Entries)
                {
                    string e = en.FullName.Replace('/', '\\');
                    if (e.EndsWith("\\")) continue;
                    var j = new Job { Rel = e, Target = Path.Combine(game, e), Entry = en };
                    j.State = !File.Exists(j.Target) ? "novo" : Same(en, j.Target) ? "igual" : "troca";
                    jobs.Add(j);
                }

                var todo = jobs.Where(x => x.State == "novo" || x.State == "troca").ToList();
                var bad = jobs.Where(x => x.State == "falta" || x.State == "outro").ToList();
                foreach (var sk in jobs.Where(x => x.State == "pulado")) { Console.WriteLine(); Console.WriteLine(" " + sk.Rel + ": " + sk.Note); }
                Header();
                Console.WriteLine(L(" Jogo: ", " Game: ") + game);
                Console.WriteLine();
                Console.WriteLine(L(" {0} para instalar ({1} novos, {2} trocados com backup), {3} ja iguais.", " {0} to install ({1} new, {2} replaced with backup), {3} already the same."), todo.Count,
                                  todo.Count(x => x.State == "novo"), todo.Count(x => x.State == "troca"), jobs.Count(x => x.State == "igual"));
                if (bad.Count > 0)
                {
                    Console.WriteLine();
                    Red(L(" Nao da para modificar ", " Cannot modify ") + bad.Count + L(" arquivo(s) do seu jogo:", " file(s) of your game:"));
                    foreach (var b in bad) Red("   " + b.Rel + ": " + b.Note);
                    Console.WriteLine(L(" Eles ficam como estao (o resto pode ser instalado).", " They stay as they are (the rest can be installed)."));
                    if (bad.Any(bf => bf.Rel.EndsWith("Init_us.agt", StringComparison.OrdinalIgnoreCase) || bf.Rel.EndsWith("Init.agt", StringComparison.OrdinalIgnoreCase)))
                        Console.WriteLine(L(" Os carros e a loja deste cliente sao arrumados mesmo assim (no fim da instalacao).", " The cars and the car shop of this client are fixed anyway (at the end of the installation)."));
                }
                if (todo.Count == 0) { Console.WriteLine(); Green(L(" Nada para instalar.", " Nothing to install.")); Pause(); return; }
                Console.WriteLine();
                foreach (var x in todo.Where(t => t.PatchName != null || t.FullName != null).Concat(todo.Where(t => t.PatchName == null && t.FullName == null)).Take(30))
                    Console.WriteLine("   {0,-6} {1}{2}", StateName(x.State), x.Rel, x.FullName != null ? "  (" + x.Note + ")" : x.PatchName != null ? L("  (modificacao", "  (modification") + (x.Note != null ? ", " + x.Note : "") + ")" : "");
                if (todo.Count > 30) Console.WriteLine(L("   ... e mais {0}", "   ... and {0} more"), todo.Count - 30);
                Console.WriteLine();
                if (!Ask(L("Instalar agora? (S/N)", "Install now? (Y/N)")).Yes()) return;

                string backup = Path.Combine(game, "backup_instalador", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                BackupSaves(backup);
                int n = 0, errors = 0;
                foreach (var x in todo)
                {
                    Console.Write(L("\r Instalando... {0}/{1}   ", "\r Installing... {0}/{1}   "), n + 1, todo.Count);
                    try
                    {
                        byte[] data = null;
                        if (x.FullName != null)
                        {
                            using (var fs = Mod(x.FullName)) using (var ms = new MemoryStream()) { fs.CopyTo(ms); data = ms.ToArray(); }
                            if (Md5(data) != x.Md5) throw new Exception(L("o arquivo nao conferiu", "the file did not match"));
                        }
                        else if (x.InPlace != null)
                        {
                            data = File.ReadAllBytes(x.Target);
                            foreach (var kv in x.InPlace) Array.Copy(kv.Value, 0, data, kv.Key, kv.Value.Length);
                        }
                        else if (x.PatchName != null)
                        {
                            var pe = Mod(x.PatchName);
                            byte[] patch;
                            using (var ps = pe) using (var ms = new MemoryStream()) { ps.CopyTo(ms); patch = ms.ToArray(); }
                            data = Apply(File.ReadAllBytes(x.BasePath), patch);
                            if (Md5(data) != x.Md5) throw new Exception(L("o resultado nao conferiu", "the result did not match"));
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(x.Target));
                        if (x.State == "troca")
                        {
                            string b = Path.Combine(backup, x.Rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(b));
                            File.Copy(x.Target, b, true);
                            var a = File.GetAttributes(x.Target);
                            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(x.Target, a & ~FileAttributes.ReadOnly);
                        }
                        else
                        {
                            Directory.CreateDirectory(backup);
                            File.AppendAllText(Path.Combine(backup, "_novos.txt"), x.Rel + Environment.NewLine);
                        }
                        if (data != null) File.WriteAllBytes(x.Target, data);
                        else
                        {
                            x.Entry.ExtractToFile(x.Target, true);
                            if (x.Md5 != null && Md5(x.Target) != x.Md5) throw new Exception(L("o arquivo nao conferiu", "the file did not match"));
                        }
                        n++;
                    }
                    catch (Exception ex) { Console.WriteLine(); Red(" " + x.Rel + ": " + ex.Message); errors++; }
                }
                // o Menu.bat fica so na pasta do jogo: tira a copia antiga de dentro da pasta Rice
                try
                {
                    string om = Path.Combine(game, "Rice", "Menu.bat");
                    if (File.Exists(om) && File.Exists(Path.Combine(game, "Menu.bat"))) File.Delete(om);
                }
                catch (Exception) { }
                Console.WriteLine();
                // cliente que o instalador nao conhece (ex.: openDC): as tabelas que modificamos sao achadas pelo conteudo
                if (bad.Any(bf => bf.State == "outro"))
                {
                    var skip = new HashSet<string>(jobs.Where(x => x.Md5 != null && x.Entry == null && (x.State == "igual" || x.State == "troca" || x.State == "novo")).Select(x => x.Rel), StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var tabs = Carros.Tables(game, backup, skip, m => { });
                        if (tabs.Count > 0)
                        {
                            Green(L(" Tabelas do jogo modificadas neste cliente (achadas pelo conteudo): ", " Game tables modified in this client (found by content): ") + tabs.Count);
                            foreach (var tb in tabs) Console.WriteLine("   " + tb);
                        }
                    }
                    catch (Exception ex) { Red(L(" Tabelas: erro (", " Tables: error (") + ex.Message + ")"); }
                }
                FixCars(backup);
                FixEngineSounds(backup);
                Console.WriteLine();
                Green(L(" Pronto! ", " Done! ") + n + L(" arquivo(s) instalados", " file(s) installed") + (errors > 0 ? ", " + errors + L(" com erro.", " with errors.") : "."));
                if (Directory.Exists(backup)) Console.WriteLine(" Backup: " + backup);
                SaveLanguage();
                Console.WriteLine();
                Console.WriteLine(L(" Para jogar: Start.bat. No login:", " To play: Start.bat. At login:"));
                Console.WriteLine(L("   - \"admin\" entra na conta admin;", "   - \"admin\" enters the admin account;"));
                Console.WriteLine(L("   - qualquer outro nome cria uma conta nova na hora (sem ser admin), comecando do zero.", "   - any other name creates a new account right away (not admin), starting from zero."));
                Console.WriteLine(L(" Saves, bots e configuracoes: Menu.bat.", " Saves, bots and settings: Menu.bat."));
                Pause();
            }
        }

        // som de motor dos carros que o cliente nao tem (carros novos ficavam mudos): copia o som de um carro parecido
        // (Instalador\modificacoes\sons_motor.lst = Cliente\sons_motor_doadores.lst, "carro:doador") para idle<carro>.wav e
        // mid<carro>.wav, em Data_US\audio\engine e/ou Data\audio\engine (openDC). So cria o que falta; o Desfazer apaga.
        static void FixEngineSounds(string backup)
        {
            try
            {
                string map;
                using (var s = Mod("sons_motor.lst"))
                {
                    if (s == null) return;
                    using (var r = new StreamReader(s)) map = r.ReadToEnd();
                }
                var pairs = map.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':')).Where(x => x.Length == 2).ToList();
                var made = new List<string>();
                foreach (var rel in new[] { @"Data_US\audio\engine", @"Data\audio\engine" })
                {
                    string dir = Path.Combine(game, rel);
                    if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "idle1.wav"))) continue;
                    foreach (var pr in pairs)
                        foreach (var kind in new[] { "idle", "mid" })
                        {
                            string dst = Path.Combine(dir, kind + pr[0] + ".wav"), src = Path.Combine(dir, kind + pr[1] + ".wav");
                            if (File.Exists(dst) || !File.Exists(src)) continue;
                            File.Copy(src, dst, false);
                            made.Add(Path.Combine(rel, kind + pr[0] + ".wav"));
                        }
                }
                if (made.Count == 0) return;
                Directory.CreateDirectory(backup);
                File.AppendAllText(Path.Combine(backup, "_novos.txt"), string.Join(Environment.NewLine, made) + Environment.NewLine);
                Green(L(" Som de motor: ", " Engine sound: ") + made.Count / 2 + L(" carro(s) sem som ganharam o som de um carro parecido.", " car(s) without sound got the sound of a similar car."));
            }
            catch (Exception ex) { Red(L(" Som de motor: erro (", " Engine sound: error (") + ex.Message + ")"); }
        }

        // antes de instalar: copia de seguranca dos saves (Rice\save, mundo, mapas e server.json) num .zip dentro do backup.
        // O instalador nao mexe nos saves; a copia e para o caso de algo dar errado (ou de apagar a pasta sem querer).
        // Restaurar: extraia o .zip dentro da pasta Rice (com o servidor fechado).
        const string SavesZip = "saves_antes_de_instalar.zip";
        static void BackupSaves(string backup)
        {
            try
            {
                string rice = Path.Combine(game, "Rice");
                if (!Directory.Exists(Path.Combine(rice, "save"))) return;
                Directory.CreateDirectory(backup);
                string zp = Path.Combine(backup, SavesZip);
                int files = 0;
                using (var z = ZipFile.Open(zp, ZipArchiveMode.Create))
                {
                    foreach (var d in new[] { "save", "mundo", "mapas" })
                    {
                        string dir = Path.Combine(rice, d);
                        if (!Directory.Exists(dir)) continue;
                        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        {
                            z.CreateEntryFromFile(f, f.Substring(rice.Length).TrimStart('\\', '/').Replace('\\', '/'));
                            files++;
                        }
                    }
                    string sj = Path.Combine(rice, "server.json");
                    if (File.Exists(sj)) { z.CreateEntryFromFile(sj, "server.json"); files++; }
                }
                Console.WriteLine(L(" Copia de seguranca dos saves: ", " Save backup: ") + Path.Combine(backup, SavesZip) + " (" + files + L(" arquivos)", " files)"));
            }
            catch (Exception ex) { Red(L(" Copia dos saves: erro (", " Save backup: error (") + ex.Message + ")"); }
        }

        // carros e loja: ajusta as tabelas do cliente (qualquer versao) com base na nossa loja (Carros.cs), ou deixa a loja
        // do cliente como esta (os bots usam os carros dessa loja). Pergunta so quando a loja do cliente e diferente.
        static void FixCars(string backup)
        {
            Console.WriteLine(L(" Conferindo os carros e a loja deste cliente...", " Checking the cars and the car shop of this client..."));
            try
            {
                var t = Carros.Adapt(game, backup, Carros.Test, m => { });
                if (t == null) { Red(L(" Carros: nao achei as tabelas de carros deste cliente (ficam como estao).", " Cars: could not find the car tables of this client (they stay as they are).")); return; }
                int mode = Carros.Fix;
                if (t.Pending)
                {
                    Console.WriteLine();
                    Console.WriteLine(L(" A loja de carros deste cliente e diferente da do Offline Aprimorado.", " The car shop of this client is different from the Offline Aprimorado one."));
                    Console.WriteLine(L(" 1) Arrumar a loja (recomendado): ", " 1) Fix the car shop (recommended): ") + t.Shop +
                                      L(" carros, V1 a V9 e os especiais depois do V9, com os nossos V, pontos e precos.", " cars, V1 to V9 and the special cars after V9, with our V, stats and prices."));
                    Console.WriteLine(L(" 2) Deixar a loja como esta: nao mexe nos carros do jogo; os bots usam os carros da loja deste cliente.",
                                        " 2) Leave the car shop as it is: the game cars are not changed; the bots use the cars of this client's shop."));
                    if (Ask(L("Escolha (1)", "Choose (1)")) == "2") mode = Carros.Keep;
                }
                var r = Carros.Adapt(game, backup, mode, m => { });
                if (r == null) return;
                if (mode == Carros.Keep)
                    Green(L(" Carros: loja deixada como esta (", " Cars: car shop left as it is (") + r.Shop + L(" carros); os bots usam esses carros.", " cars); the bots use these cars."));
                else if (!r.Changed) Green(L(" Carros: a loja ja esta arrumada (", " Cars: the car shop is already fine (") + r.Shop + L(" carros).", " cars)."));
                else
                {
                    Green(L(" Carros: loja arrumada para este cliente: ", " Cars: car shop fixed for this client: ") + r.Shop + L(" carros, V1 a V9 e os especiais depois do V9.", " cars, V1 to V9 and the special cars after V9."));
                    Console.WriteLine(L("   {0} carros com V/pontos novos (todos liberados: so nivel e cupom travam), {1} linhas de preco por V, lista da loja {2}, {3} cupons.",
                                        "   {0} cars with new V/stats (all unlocked: only level and coupon lock them), {1} price rows per V, shop list {2}, {3} coupons."),
                                      r.VdRows, r.VlRows, r.NewList ? L("nova", "rebuilt") : L("igual", "the same"), r.Coupons);
                    foreach (var w in r.Written) Console.WriteLine(L("   modificado (com backup): ", "   modified (with backup): ") + w);
                }
                if (r.Mine > 0) Console.WriteLine(L("   {0} carro(s) que so este cliente tem: no fim do V deles.", "   {0} car(s) only this client has: at the end of their V."), r.Mine);
                if (r.Missing > 0) Console.WriteLine(L("   {0} carro(s) sem modelo neste cliente: fora da loja (o servidor tambem nao usa).", "   {0} car(s) without a model in this client: out of the shop (the server does not use them either)."), r.Missing);
            }
            catch (Exception ex) { Red(L(" Carros: erro (", " Cars: error (") + ex.Message + L("); os arquivos ficam como estavam.", "); the files stay as they were.")); }
        }

        // DriftCity.exe protegido (o original do jogo): secoes sem nome e a ".adata" do ASProtect; o desempacotado tem .idata
        static bool IsProtected(string exe)
        {
            try
            {
                var d = File.ReadAllBytes(exe);
                int pe = BitConverter.ToInt32(d, 0x3c);
                int n = BitConverter.ToUInt16(d, pe + 6), opt = BitConverter.ToUInt16(d, pe + 20);
                int sec = pe + 24 + opt;
                for (int i = 0; i < n; i++)
                {
                    string name = System.Text.Encoding.ASCII.GetString(d, sec + i * 40, 8).TrimEnd('\0');
                    if (name == ".adata" || name == ".aspack" || name.StartsWith(".asp")) return true;
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        static List<KeyValuePair<long, byte[]>> ReadInPlace(Stream e)
        {
            var l = new List<KeyValuePair<long, byte[]>>();
            using (var r = new StreamReader(e))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    var p = line.Trim().Split(':');
                    if (p.Length != 2) continue;
                    long off = Convert.ToInt64(p[0], 16);
                    var b = new byte[p[1].Length / 2];
                    for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(p[1].Substring(i * 2, 2), 16);
                    l.Add(new KeyValuePair<long, byte[]>(off, b));
                }
            }
            return l;
        }

        // as modificacoes (Instalador\modificacoes, embutidas no .exe como "mod/<nome>")
        static Stream Mod(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("mod/" + name);
        }

        // aplica um .dcp (gerado por Cliente/ferramentas/gerar_patches.py): "DCP1" + zlib(ops)
        static byte[] Apply(byte[] b, byte[] patch)
        {
            if (patch.Length < 6 || patch[0] != 'D' || patch[1] != 'C' || patch[2] != 'P' || patch[3] != '1') throw new Exception(L("patch invalido", "invalid patch"));
            byte[] raw;
            // zlib = 2 bytes de cabecalho + deflate + 4 bytes de checksum
            using (var src = new MemoryStream(patch, 6, patch.Length - 6))
            using (var df = new DeflateStream(src, CompressionMode.Decompress))
            using (var ms = new MemoryStream()) { df.CopyTo(ms); raw = ms.ToArray(); }
            var o = new MemoryStream();
            int p = 0;
            while (p < raw.Length)
            {
                byte t = raw[p++];
                if (t == (byte) 'C')
                {
                    int off = BitConverter.ToInt32(raw, p), len = BitConverter.ToInt32(raw, p + 4); p += 8;
                    o.Write(b, off, len);
                }
                else if (t == (byte) 'A')
                {
                    int len = BitConverter.ToInt32(raw, p); p += 4;
                    o.Write(raw, p, len); p += len;
                }
                else throw new Exception(L("patch invalido", "invalid patch"));
            }
            return o.ToArray();
        }

        static string Md5(string file) { using (var s = File.OpenRead(file)) return Md5(s); }
        static string Md5(byte[] data) { using (var s = new MemoryStream(data)) return Md5(s); }
        static string Md5(Stream s)
        {
            using (var h = System.Security.Cryptography.MD5.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        // volta o backup mais recente: os arquivos trocados voltam e os novos sao apagados (o servidor e os saves ficam)
        static void Undo()
        {
            string root = Path.Combine(game, "backup_instalador");
            var last = Directory.Exists(root) ? Directory.GetDirectories(root).OrderByDescending(d => d).FirstOrDefault() : null;
            if (last == null) { Red(L(" Nao tem backup de instalacao.", " There is no installation backup.")); Pause(); return; }
            if (Running("DriftCity") || Running("Rice")) { Red(L(" Feche o jogo e o servidor (Rice.exe) antes.", " Close the game and the server (Rice.exe) first.")); Pause(); return; }
            Console.WriteLine(" Backup: " + Path.GetFileName(last));
            if (!Ask(L("Voltar os arquivos desse backup? (S/N)", "Restore the files from this backup? (Y/N)")).Yes()) return;
            int n = 0;
            foreach (var f in Directory.GetFiles(last, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(last.Length).TrimStart('\\');
                if (rel.Equals("_novos.txt", StringComparison.OrdinalIgnoreCase) || rel.Equals(SavesZip, StringComparison.OrdinalIgnoreCase)) continue;
                string t = Path.Combine(game, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(t));
                File.Copy(f, t, true);
                n++;
            }
            string novos = Path.Combine(last, "_novos.txt");
            if (File.Exists(novos))
                foreach (var rel in File.ReadAllLines(novos).Where(l => l.Trim() != ""))
                {
                    // o servidor fica (os saves estao la); o resto que o instalador trouxe sai
                    if (rel.StartsWith("Rice\\", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(Path.Combine(game, rel)); n++; } catch (Exception) { }
                }
            Directory.Move(last, last + "_desfeito");
            Green(L(" Pronto: ", " Done: ") + n + L(" arquivo(s) voltaram como antes.", " file(s) restored."));
            Pause();
        }

        // ---------------------------------------------------------------------------------------------- util
        static ZipArchive OpenPackage()
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
            return s == null ? null : new ZipArchive(s, ZipArchiveMode.Read);
        }

        static bool Same(ZipArchiveEntry e, string file)
        {
            var fi = new FileInfo(file);
            if (fi.Length != e.Length) return false;
            using (var a = e.Open()) using (var b = File.OpenRead(file))
            {
                var ba = new byte[1 << 16]; var bb = new byte[1 << 16];
                while (true)
                {
                    int na = Fill(a, ba), nb = Fill(b, bb);
                    if (na != nb) return false;
                    if (na == 0) return true;
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                }
            }
        }

        static int Fill(Stream s, byte[] buf)
        {
            int n = 0;
            while (n < buf.Length) { int r = s.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
            return n;
        }

        static bool IsGame(string d)
        {
            return Directory.Exists(d) && File.Exists(Path.Combine(d, "DriftCity.exe")) && Directory.Exists(Path.Combine(d, "Data")) && Directory.Exists(Path.Combine(d, "Data_US"));
        }

        static string Version()
        {
            bool us = File.Exists(Path.Combine(game, "Data_US", "Init_us.agt"));
            bool ini = File.Exists(Path.Combine(game, "DriftCityUS.ini"));
            return us && ini ? "Drift City Alpha (US)" : us ? "Drift City (Data_US)" : L("desconhecida (sem Data_US\\Init_us.agt)", "unknown (no Data_US\\Init_us.agt)");
        }

        static bool Running(string name)
        {
            try { int me = Process.GetCurrentProcess().Id; return Process.GetProcessesByName(name).Any(p => p.Id != me); } catch (Exception) { return false; }
        }

        static void Header()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" Install Enhanced Rice (Offline Aprimorado)");
            Console.WriteLine(" ------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine();
        }
        // ---------------------------------------------------------------------------------------------- idioma
        static bool en = true;
        static string L(string pt, string english) { return en ? english : pt; }
        static string StateName(string st)
        {
            if (!en) return st;
            switch (st) { case "novo": return "new"; case "troca": return "update"; case "igual": return "same"; default: return st; }
        }
        static bool Yes(this string a) { a = (a ?? "").Trim(); return a.StartsWith("s", StringComparison.OrdinalIgnoreCase) || a.StartsWith("y", StringComparison.OrdinalIgnoreCase); }
        static void ChooseLanguage()
        {
            Header();
            Console.WriteLine(" 1) English");
            Console.WriteLine(" 2) Portugues (Brasil)");
            string a = Ask("Language / Idioma (1)");
            en = a != "2";
        }

        // idioma do servidor (Rice\server.json "Language"): portugues grava "pt"; ingles e o padrao (tira a chave)
        static void SaveLanguage()
        {
            try
            {
                string f = Path.Combine(game, "Rice", "server.json");
                string j = File.Exists(f) ? File.ReadAllText(f) : "";
                j = System.Text.RegularExpressions.Regex.Replace(j, @"\s*""Language""\s*:\s*""[^""]*""\s*,?", "");
                if (en) { if (File.Exists(f)) File.WriteAllText(f, j); return; }
                if (j.Trim().Length == 0 || j.Trim() == "{}") j = "{\r\n  \"Language\": \"pt\"\r\n}";
                else
                {
                    int b = j.IndexOf('{');
                    string rest = j.Substring(b + 1).Trim();
                    j = j.Substring(0, b + 1) + "\r\n  \"Language\": \"pt\"" + (rest.StartsWith("}") ? "\r\n" : ",\r\n  ") + rest;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, j);
            }
            catch (Exception) { }
        }

        static string Ask(string p) { Console.Write(" " + p + ": "); return (Console.ReadLine() ?? "").Trim(); }
        static void Red(string s) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(s); Console.ForegroundColor = ConsoleColor.Gray; }
        static void Green(string s) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(s); Console.ForegroundColor = ConsoleColor.Gray; }
        static void Pause() { Console.Write(L(" (Enter para continuar)", " (Enter to continue)")); Console.ReadLine(); }
    }
}
