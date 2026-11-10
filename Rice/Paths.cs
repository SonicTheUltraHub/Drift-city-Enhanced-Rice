using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rice
{
    // Onde ficam os arquivos do servidor (pasta do Rice.exe):
    //   save/                     db.sdf (contas, corredores, carros, itens) e marcas do servidor
    //   save/<conta>/<corredor>/  o que e de cada corredor: amigos bots, licencas, cupons, Drift shop, alquimia...
    //   mundo/                    o que e do servidor inteiro: bots, crews, leilao, escada de crews
    //   mapas/                    pontos aprendidos: Driver Dome, base ROAD, tiros da Neo City, Rush
    //   res/                      tabelas do jogo (nao mudam)
    // Na primeira vez, os arquivos antigos (soltos na pasta do Rice.exe) vao sozinhos para o lugar novo.
    public static class Paths
    {
        public const string Save = "save", WorldDir = "mundo", MapsDir = "mapas";

        public static string Db { get { return Path.Combine(Save, "db.sdf"); } }
        public static string SaveFile(string n) { return Path.Combine(Save, n); }
        public static string World(string n) { return Path.Combine(WorldDir, n); }
        public static string Map(string n) { return Path.Combine(MapsDir, n); }

        static readonly string[] WorldFiles = { "bots.json", "crews.json", "auction.json", "crew_ladder.json" };
        static readonly string[] MapFiles = { "dome_points.json", "neo_dome_points.json", "neo_fire.json", "rush_spots.json" };
        static readonly string[] SaveFiles = { "db.sdf", "garage_v_fix.txt" };

        static bool migrated;
        // move os arquivos antigos da pasta do Rice.exe para save/, mundo/ e mapas/ (o que ja existir no lugar novo fica)
        public static void Migrate()
        {
            if (migrated) return;
            migrated = true;
            try
            {
                Directory.CreateDirectory(Save); Directory.CreateDirectory(WorldDir); Directory.CreateDirectory(MapsDir);
                int n = 0;
                foreach (var f in WorldFiles) n += Move(f, World(f));
                foreach (var f in MapFiles) n += Move(f, Map(f));
                foreach (var f in SaveFiles) n += Move(f, SaveFile(f));
                if (n > 0) Log.WriteLine("Pastas: {0} arquivo(s) antigo(s) organizados em save/, mundo/ e mapas/.", n);
            }
            catch (Exception ex) { Log.WriteError("Pastas: " + ex.Message); }
        }

        static int Move(string from, string to)
        {
            if (!File.Exists(from) || File.Exists(to)) return 0;
            File.Move(from, to);
            return 1;
        }

        // nome de pasta valido no Windows
        public static string Safe(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "_";
            var bad = Path.GetInvalidFileNameChars();
            var s = new string(name.Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
            return s.Length == 0 ? "_" : s;
        }
    }

    // Arquivos "por corredor": no codigo continuam sendo um dicionario so (chave = numero do corredor, ou
    // "corredor:alguma coisa"), mas no disco cada corredor tem o dele em save/<conta>/<corredor>/<arquivo>.
    // Ler junta todos; gravar separa pela chave. Corredor que o banco nao conhece vai para save/_sem_dono.
    public static class CharFiles
    {
        public static readonly string[] Names = { "friends.json", "licenses.json", "arbeit.json", "vsitems.json", "coupons.json",
                                                  "level_docks.json", "alchemy.json", "carboxes.json", "visualboxes.json" };
        static readonly object Sync = new object();
        static Dictionary<long, string> dirs = new Dictionary<long, string>();
        static DateTime loaded = DateTime.MinValue;
        public const string NoOwner = "_sem_dono";

        // pasta do corredor (save/<conta>/<corredor>); null se o banco nao conhece
        public static string Dir(long cid)
        {
            lock (Sync)
            {
                string d;
                if (dirs.TryGetValue(cid, out d)) return d;
                if ((DateTime.UtcNow - loaded).TotalSeconds > 2) { Refresh(); if (dirs.TryGetValue(cid, out d)) return d; }
                return null;
            }
        }

        // le do banco quem e dono de cada corredor
        public static void Refresh()
        {
            lock (Sync)
            {
                loaded = DateTime.UtcNow;
                try
                {
                    using (var rc = Database.GetContext())
                    {
                        var users = rc.Users.Select(us => new { us.ID, us.Name }).ToList().ToDictionary(us => us.ID, us => us.Name);
                        var map = new Dictionary<long, string>();
                        foreach (var c in rc.Characters.Select(ch => new { ch.ID, ch.Name, ch.UID }).ToList())
                        {
                            string un; if (!users.TryGetValue(c.UID, out un)) un = NoOwner;
                            map[c.ID] = Path.Combine(Paths.Save, Paths.Safe(un), Paths.Safe(c.Name));
                        }
                        dirs = map;
                        foreach (var u in users.Values) Directory.CreateDirectory(Path.Combine(Paths.Save, Paths.Safe(u)));
                    }
                }
                catch (Exception ex) { Log.WriteError("Pastas dos corredores: " + ex.Message); }
            }
        }

        // todos os arquivos com esse nome dentro de save/ (um por corredor)
        static IEnumerable<string> Parts(string name)
        {
            if (!Directory.Exists(Paths.Save)) return new string[0];
            return Directory.GetFiles(Paths.Save, name, SearchOption.AllDirectories)
                            .Where(f => !string.Equals(Path.GetDirectoryName(Path.GetFullPath(f)), Path.GetFullPath(Paths.Save), StringComparison.OrdinalIgnoreCase));
        }

        public static bool Exists(string name) { return ReadAllText(name) != null; }

        public static string ReadAllText(string name)
        {
            lock (Sync)
            {
                var all = new JObject();
                bool any = false;
                foreach (var f in Parts(name))
                {
                    try
                    {
                        var o = JObject.Parse(File.ReadAllText(f));
                        foreach (var p in o.Properties()) all[p.Name] = p.Value;
                        any = true;
                    }
                    catch (Exception ex) { Log.WriteError(f + ": " + ex.Message); }
                }
                if (any) return all.ToString(Formatting.None);
                // ainda no formato antigo (um arquivo so na pasta do Rice.exe)
                return File.Exists(name) ? File.ReadAllText(name) : null;
            }
        }

        static long CidOf(string key)
        {
            int i = 0;
            while (i < key.Length && char.IsDigit(key[i])) i++;
            long v;
            return i > 0 && long.TryParse(key.Substring(0, i), out v) ? v : -1;
        }

        public static void WriteAllText(string name, string json)
        {
            lock (Sync)
            {
                var o = JObject.Parse(json);
                var groups = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in o.Properties())
                {
                    long cid = CidOf(p.Name);
                    string d = cid >= 0 ? Dir(cid) : null;
                    if (d == null) d = Path.Combine(Paths.Save, NoOwner);
                    JObject g;
                    if (!groups.TryGetValue(d, out g)) groups[d] = g = new JObject();
                    g[p.Name] = p.Value;
                }
                var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in groups)
                {
                    Directory.CreateDirectory(kv.Key);
                    string f = Path.Combine(kv.Key, name);
                    File.WriteAllText(f, kv.Value.ToString(Formatting.Indented));
                    written.Add(Path.GetFullPath(f));
                }
                // corredor que saiu do dicionario: o arquivo dele vai embora
                foreach (var f in Parts(name).ToList())
                    if (!written.Contains(Path.GetFullPath(f))) try { File.Delete(f); } catch (Exception) { }
                // o arquivo antigo (formato de antes) ja foi dividido
                if (File.Exists(name)) try { File.Delete(name); } catch (Exception) { }
            }
        }

        // na subida do servidor: divide os arquivos antigos (soltos na pasta do Rice.exe) por corredor
        public static void Migrate()
        {
            Refresh();
            int n = 0;
            foreach (var name in Names)
            {
                if (!File.Exists(name)) continue;
                try { WriteAllText(name, File.ReadAllText(name)); n++; }
                catch (Exception ex) { Log.WriteError("Pastas (" + name + "): " + ex.Message); }
            }
            if (n > 0) Log.WriteLine("Pastas: {0} arquivo(s) dos corredores separados em save/<conta>/<corredor>/.", n);
        }
    }
}
