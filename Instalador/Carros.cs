using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Instalador
{
    // Arruma os carros e a loja de QUALQUER cliente do Drift City com base nas tabelas de referencia (o nosso
    // Init_us.agt ja arrumado: Instalador\modificacoes\carros_*.ref, geradas por Cliente\ferramentas\carros_referencia.py).
    // Mesmo algoritmo de Cliente\ferramentas\adaptar_carros.py:
    //  1. tabela ATIVA de cada coisa: Data_US\Init_us.agt (se tiver a tabela) senao Data\Init.agt;
    //  2. modelos de carro (vehicle\player\pc_XXXX...) em todos os pacotes (Data, Data_US, Patch);
    //  3. VehicleDisplay: nos carros que o cliente tem, copia V (Lvl), nivel pedido, condicao (Req Cond: EVENT trava o
    //     carro; na referencia todos N/A = liberados, so nivel e cupom travam), pontos, Sellable, GradeType e a
    //     posicao na loja (Display); liga o carro (Support 1) se a referencia usa e o modelo existe;
    //  4. VehicleList: pontos, preco, venda, nivel pedido... de cada V (mesmo carro e mesmo V);
    //  5. loja: os carros da referencia que o cliente tem (V1..V9, especiais depois do V9); os que so o cliente tem vao no
    //     fim do V deles (especiais no fim de tudo) e ai a posicao vira 1..N;
    //  6. grava so o que mudou (com backup); a tabela que nao cabe no lugar vai para o fim do pacote.
    // Tambem grava Rice\res\ShopCars.json (a loja, para os bots) e Rice\res\carros_cliente.json (carros que faltam).
    static class Carros
    {
        static readonly string[] VdCopy = { "Lvl", "Req Level", "Req Cond", "MakeType", "closestage", "Accel", "Speed", "Crash", "Boost", "Sellable", "GradeType", "Display" };
        static readonly string[] VlCopy = { "Coupon", "Accel", "Speed", "Crash", "Boost", "Price", "Sell", "CloseSell", "UpgradeMito",
                                            "Efficiency", "Capacity", "Req Level", "Sum", "GradeType" };
        static readonly byte[] Key = Convert.FromBase64String("ND0w6Gc2NjI+MzQ7ERUWFhQTHRgRAwYMBAMGCC5VJiMqIy4oISEmJy4ALS3PpQYCBA8HGOEVNhhgExoZERUWEBITFzjxJQEFBgIEAwcIAQUGDwQDBwwxhXY5");

        public class Result
        {
            public int Shop, Mine, Missing, VdRows, VlRows, Positions, Coupons;
            public bool NewList, Changed, Pending;      // Pending: a loja deste cliente precisa (ou precisava) de ajuste
            public List<string> Written = new List<string>();
        }

        // ------------------------------------------------------------------------------------------- pacote NayaPack
        static void Xor(byte[] buf, int index, int count, long start)
        {
            for (int i = 0; i < count; i++) buf[index + i] ^= Key[(int) ((start + i - 20) % 90)];
        }
        static byte[] XorCopy(byte[] d, int off, int count)
        {
            var b = new byte[count];
            Array.Copy(d, off, b, 0, count);
            Xor(b, 0, count, off);
            return b;
        }

        class Entry { public string Name; public int Off, Chunks, USize, DirPos; }

        class Pack
        {
            public string Path;
            public byte[] D;
            public List<Entry> Ents = new List<Entry>();
            public bool Changed;

            public Pack(string path)
            {
                Path = path;
                D = File.ReadAllBytes(path);
                if (D.Length >= 44 && Encoding.ASCII.GetString(D, 0, 8) == "NayaPack") Ents = ParseDir(D, D.Length, D.Length);
            }

            public string Read(Entry e)
            {
                if (e.Off <= 0 || e.Off + 2 * e.Chunks > D.Length) return null;
                var tab = XorCopy(D, e.Off, 2 * e.Chunks);
                int p = e.Off + 2 * e.Chunks;
                var o = new MemoryStream();
                for (int i = 0; i < e.Chunks; i++)
                {
                    int n = BitConverter.ToUInt16(tab, 2 * i);
                    if (n < 6 || p + n > D.Length) return null;
                    var c = XorCopy(D, p, n);
                    using (var src = new MemoryStream(c, 2, n - 2))
                    using (var df = new DeflateStream(src, CompressionMode.Decompress)) df.CopyTo(o);
                    p += n;
                }
                var b = o.ToArray();
                if (b.Length != e.USize || b.Length < 2) return null;
                string t = Encoding.Unicode.GetString(b);
                return t.Length > 0 && t[0] == '﻿' ? t.Substring(1) : t;
            }

            // procura a tabela pelo conteudo (os nomes dentro do pacote nao batem com o conteudo)
            public int Find(Func<string, bool> test, out string text)
            {
                text = null;
                for (int i = 0; i < Ents.Count; i++)
                {
                    var e = Ents[i];
                    if (e.USize < 200 || e.USize > 8000000) continue;
                    string t;
                    try { t = Read(e); } catch (Exception) { continue; }
                    if (t != null && test(t)) { text = t; return i; }
                }
                return -1;
            }

            public void Store(int i, string text)
            {
                var pre = Encoding.Unicode.GetPreamble();
                var body = Encoding.Unicode.GetBytes(text);
                var raw = new byte[pre.Length + body.Length];
                Array.Copy(pre, raw, pre.Length); Array.Copy(body, 0, raw, pre.Length, body.Length);
                StoreRaw(i, raw);
            }

            public byte[] ReadRaw(Entry e)
            {
                if (e.Off <= 0 || e.Off + 2 * e.Chunks > D.Length) return null;
                var tab = XorCopy(D, e.Off, 2 * e.Chunks);
                int p = e.Off + 2 * e.Chunks;
                var o = new MemoryStream();
                for (int k = 0; k < e.Chunks; k++)
                {
                    int n = BitConverter.ToUInt16(tab, 2 * k);
                    if (n < 6 || p + n > D.Length) return null;
                    var c = XorCopy(D, p, n);
                    using (var src = new MemoryStream(c, 2, n - 2))
                    using (var df = new DeflateStream(src, CompressionMode.Decompress)) df.CopyTo(o);
                    p += n;
                }
                var b = o.ToArray();
                return b.Length == e.USize ? b : null;
            }

            public void StoreRaw(int i, byte[] raw)
            {
                var e = Ents[i];
                var chunks = new List<byte[]>();
                for (int k = 0; k < raw.Length; k += 16384) chunks.Add(Zlib(raw, k, Math.Min(16384, raw.Length - k)));
                int size = 2 * chunks.Count + chunks.Sum(c => c.Length);
                int next = Math.Min(D.Length, Ents.Where(x => x.Off > e.Off).Select(x => x.Off).DefaultIfEmpty(D.Length).Min());
                int off = size <= next - e.Off ? e.Off : D.Length;          // nao cabe: vai para o fim do pacote
                if (off == D.Length) { var nd = new byte[D.Length + size]; Array.Copy(D, nd, D.Length); D = nd; }
                int pos = off;
                var tab = new byte[2 * chunks.Count];
                for (int k = 0; k < chunks.Count; k++) { tab[2 * k] = (byte) chunks[k].Length; tab[2 * k + 1] = (byte) (chunks[k].Length >> 8); }
                foreach (var part in new[] { tab }.Concat(chunks))
                {
                    Array.Copy(part, 0, D, pos, part.Length);
                    Xor(D, pos, part.Length, pos);
                    pos += part.Length;
                }
                int p = e.DirPos + 4 + e.Name.Length;
                var dir = new byte[12];
                Array.Copy(BitConverter.GetBytes(off), 0, dir, 0, 4);
                Array.Copy(BitConverter.GetBytes(chunks.Count), 0, dir, 4, 4);
                Array.Copy(BitConverter.GetBytes(raw.Length), 0, dir, 8, 4);
                Xor(dir, 0, 12, p);
                Array.Copy(dir, 0, D, p, 12);
                e.Off = off; e.Chunks = chunks.Count; e.USize = raw.Length;
                Changed = true;
            }
        }

        // ------------------------------------------------------------------------------------------- Init_us "nosso"
        // Data_US\Init_us.agt que nao bate o md5 do manifesto: e nosso? (Instalador\modificacoes\init_familia.lst,
        // Cliente\ferramentas\init_familia.py). As tabelas de carros/loja/cupons nao contam (o ajuste de carros cuida).
        //  1 = as outras tabelas sao iguais as nossas de agora (so a loja foi ajustada a este cliente): fica como esta;
        //  2 = todas sao nossas ou de uma versao original conhecida (mexido por instalacao anterior): troca pelo nosso inteiro;
        //  0 = outro arquivo (nao mexe).
        static readonly string[] CarTables = { "Init\\VehicleDisplay.xlt", "Init\\VehicleList.xlt", "Init\\VehicleColorGroups.xlt", "Init\\compound.xlt" };
        public static int InitFamily(string path)
        {
            try
            {
                var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("mod/init_familia.lst"))
                {
                    if (s == null) return 0;
                    using (var r = new StreamReader(s))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            var f = line.Trim().Split('|');
                            if (f.Length != 3) continue;
                            if (f[0] == "T") target[f[1]] = f[2];
                            known.Add(f[1] + "|" + f[2]);
                        }
                    }
                }
                if (target.Count == 0 || !File.Exists(path)) return 0;
                var pk = new Pack(path);
                var names = new HashSet<string>(pk.Ents.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
                if (!names.SetEquals(target.Keys)) return 0;
                bool same = true;
                foreach (var e in pk.Ents)
                {
                    if (CarTables.Contains(e.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    var raw = pk.ReadRaw(e);
                    if (raw == null) return 0;
                    string md5;
                    using (var h = System.Security.Cryptography.MD5.Create()) md5 = BitConverter.ToString(h.ComputeHash(raw)).Replace("-", "").ToLowerInvariant();
                    if (md5 != target[e.Name]) same = false;
                    if (md5 != target[e.Name] && !known.Contains(e.Name + "|" + md5)) return 0;
                }
                return same ? 1 : 2;
            }
            catch (Exception) { return 0; }
        }

        // ------------------------------------------------------------------------------------------- outras tabelas
        // Instalador\modificacoes\tabelas.lst (Cliente\ferramentas\tabelas_referencia.py): md5 do conteudo ORIGINAL de
        // cada tabela que modificamos | tamanho | arquivo .gz com a nossa | md5 novo | de onde. Procura em todos os pacotes
        // do cliente (Data, Data_US, Patch e subpastas) uma tabela com exatamente o conteudo original (o nome e o pacote nao
        // importam: no openDC ficam em Data\World\data.agt e Data\Init.agt) e troca pela nossa. skip: arquivos (relativos
        // a pasta do jogo) que o instalador ja modificou pelo jeito normal.
        public static List<string> Tables(string game, string backup, HashSet<string> skip, Action<string> log)
        {
            var done = new List<string>();
            var want = new Dictionary<string, string[]>();
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("mod/tabelas.lst"))
            {
                if (s == null) return done;
                using (var r = new StreamReader(s))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        var f = line.Trim().Split('|');
                        if (f.Length >= 4) want[f[0]] = f;
                    }
                }
            }
            var sizes = new HashSet<int>(want.Values.Select(f => Int(f[1], -1)));
            var files = new List<string>();
            foreach (var sub in new[] { "Data", "Data_US", "Patch" })
            {
                string folder = Path.Combine(game, sub);
                if (Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
            }
            foreach (var file in files)
            {
                string rel = file.Substring(game.Length).TrimStart('\\', '/');
                if (skip != null && skip.Contains(rel)) continue;
                long len;
                try { len = new FileInfo(file).Length; } catch (Exception) { continue; }
                if (len < 64 || len > 120L * 1024 * 1024) continue;
                bool naya;
                using (var fs = File.OpenRead(file)) { var h = new byte[8]; naya = fs.Read(h, 0, 8) == 8 && Encoding.ASCII.GetString(h) == "NayaPack"; }
                if (!naya) continue;
                Pack pk;
                try { pk = new Pack(file); } catch (Exception) { continue; }
                for (int i = 0; i < pk.Ents.Count; i++)
                {
                    if (!sizes.Contains(pk.Ents[i].USize)) continue;
                    byte[] raw;
                    try { raw = pk.ReadRaw(pk.Ents[i]); } catch (Exception) { continue; }
                    if (raw == null) continue;
                    string md5;
                    using (var h = System.Security.Cryptography.MD5.Create()) md5 = BitConverter.ToString(h.ComputeHash(raw)).Replace("-", "").ToLowerInvariant();
                    string[] w;
                    if (!want.TryGetValue(md5, out w)) continue;
                    byte[] neu;
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("mod/" + w[2]))
                    {
                        if (s == null) continue;
                        using (var gz = new GZipStream(s, CompressionMode.Decompress)) using (var ms = new MemoryStream()) { gz.CopyTo(ms); neu = ms.ToArray(); }
                    }
                    pk.StoreRaw(i, neu);
                    done.Add(rel + ": " + (w.Length > 4 ? w[4] : w[2]));
                }
                if (pk.Changed)
                {
                    if (backup != null)
                    {
                        string b = Path.Combine(backup, rel);
                        if (!File.Exists(b)) { Directory.CreateDirectory(Path.GetDirectoryName(b)); File.Copy(file, b, false); }
                    }
                    var a = File.GetAttributes(file);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, a & ~FileAttributes.ReadOnly);
                    File.WriteAllBytes(file, pk.D);
                }
            }
            return done;
        }

        // len = bytes lidos de d; fileLen = tamanho do arquivo. Para na primeira entrada invalida (o cabecalho as vezes conta
        // uma entrada a mais com lixo: ex. a 45a do Init_us.agt aponta para 128 MB num arquivo de 880 KB).
        static List<Entry> ParseDir(byte[] d, int len, long fileLen)
        {
            var l = new List<Entry>();
            int count = BitConverter.ToInt32(d, 16), p = 44;
            for (int k = 0; k < count; k++)
            {
                if (p + 4 > len) break;
                int n = BitConverter.ToInt32(XorCopy(d, p, 4), 0);
                if (n <= 0 || n > 512 || p + 16 + n > len) break;
                string name = Encoding.GetEncoding(28591).GetString(XorCopy(d, p + 4, n));
                var m = XorCopy(d, p + 4 + n, 12);
                int off = BitConverter.ToInt32(m, 0), nch = BitConverter.ToInt32(m, 4);
                if (off <= 0 || nch < 0 || (long) off + 2L * nch > fileLen || name.Any(ch => ch < ' ' || ch > '~')) break;
                l.Add(new Entry { Name = name, Off = off, Chunks = nch, USize = BitConverter.ToInt32(m, 8), DirPos = p });
                p += 4 + n + 12;
            }
            return l;
        }

        // zlib = cabecalho 78 DA + deflate + adler32 (big endian)
        static byte[] Zlib(byte[] b, int off, int len)
        {
            var o = new MemoryStream();
            o.WriteByte(0x78); o.WriteByte(0xDA);
            using (var df = new DeflateStream(o, CompressionLevel.Optimal, true)) df.Write(b, off, len);
            uint a = 1, s = 0;
            for (int i = off; i < off + len; i++) { a = (a + b[i]) % 65521; s = (s + a) % 65521; }
            uint ad = (s << 16) | a;
            o.WriteByte((byte) (ad >> 24)); o.WriteByte((byte) (ad >> 16)); o.WriteByte((byte) (ad >> 8)); o.WriteByte((byte) ad);
            return o.ToArray();
        }

        // so os nomes dentro de um pacote (le o comeco do arquivo, sem os dados)
        static List<string> PackNames(string path)
        {
            var names = new List<string>();
            try
            {
                byte[] d;
                int len;
                using (var f = File.OpenRead(path))
                {
                    d = new byte[(int) Math.Min(f.Length, 16 * 1024 * 1024)];
                    len = 0;
                    int r;
                    while (len < d.Length && (r = f.Read(d, len, d.Length - len)) > 0) len += r;
                }
                if (len < 44 || Encoding.ASCII.GetString(d, 0, 8) != "NayaPack") return names;
                foreach (var e in ParseDir(d, len, new FileInfo(path).Length)) names.Add(e.Name);
            }
            catch (Exception) { }
            return names;
        }

        static HashSet<string> Models(string game)
        {
            var found = new HashSet<string>();
            // nos pacotes de Data/Data_US o nome e "vehicle\player\pc_...", nos da pasta Patch "Data\vehicle\player\pc_..."
            var rx = new Regex(@"(?:^|\\)vehicle\\player\\(pc_[0-9]+[a-z]*)[_.]", RegexOptions.IgnoreCase);
            foreach (var sub in new[] { "Data", "Data_US", "Patch" })
            {
                string folder = Path.Combine(game, sub);
                if (!Directory.Exists(folder)) continue;
                foreach (var f in Directory.GetFiles(folder))
                {
                    try { if (new FileInfo(f).Length < 64) continue; } catch (Exception) { continue; }
                    foreach (var n in PackNames(f))
                    {
                        var m = rx.Match(n);
                        if (m.Success) found.Add(m.Groups[1].Value.ToLowerInvariant());
                    }
                }
            }
            return found;
        }

        // ------------------------------------------------------------------------------------------- tabelas .xlt
        class Table
        {
            public List<string> Lines;
            public int Hdr;
            public Dictionary<string, int> Col = new Dictionary<string, int>();
            public Table(string text, bool vehicleList)
            {
                Lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None).ToList();
                Hdr = -1;
                for (int i = 0; i < Math.Min(8, Lines.Count) && Hdr < 0; i++)
                    if (vehicleList ? Lines[i].StartsWith("Idx\t") : (Lines[i].StartsWith("Index\t") && Lines[i].Contains("\tUnique Id\t"))) Hdr = i;
                if (Hdr < 0) throw new Exception("cabecalho");
                var h = Lines[Hdr].Split('\t');
                for (int i = 0; i < h.Length; i++) if (!Col.ContainsKey(h[i].Trim())) Col[h[i].Trim()] = i;
            }
            public IEnumerable<KeyValuePair<int, string[]>> Rows()
            {
                for (int i = Hdr + 1; i < Lines.Count; i++)
                {
                    var c = Lines[i].Split('\t');
                    if (c.Length > 2) yield return new KeyValuePair<int, string[]>(i, c);
                }
            }
            public string Text() { return string.Join("\r\n", Lines); }
        }

        static bool IsVd(string t) { return t.Contains("\tUnique Id\tSellable\t") && t.Contains("PlayerCar"); }
        static bool IsVl(string t) { return t.Contains("Game UI name") && t.Contains("\tUpgradeMito\t"); }
        static bool IsCg(string t) { return t.Contains("\tUnique Id\tNew\tHot\tGM\tCategory"); }
        static bool IsCp(string t) { return t.Contains("CouponStampDisaply") && t.Contains("\ti_n_c"); }
        // linha da tabela de cupons: carro + item do cupom
        static string CpKey(string line)
        {
            var c = line.Split('\t');
            return c.Length > 8 && c[7].Trim().StartsWith("i_n_c") ? c[6].Trim() + "|" + c[7].Trim() : null;
        }

        static bool Special(string n)
        {
            n = n.Trim().ToUpperInvariant();
            return n.EndsWith(" ST") || n.EndsWith(" BT") || n.EndsWith(" XMAS") || n.EndsWith(" MINI") || n.EndsWith(" CT");
        }

        static string RefText(string name)
        {
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("mod/carros_" + name + ".ref"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, new UTF8Encoding(false))) { string t = r.ReadToEnd(); return t.Length > 0 && t[0] == '﻿' ? t.Substring(1) : t; }
            }
        }

        static List<string> shopIds0(Table t)
        {
            int uc = t.Col["Unique Id"];
            return t.Rows().Where(kv => kv.Value.Length > uc && Int(kv.Value[uc], -1) >= 0).Select(kv => kv.Value[uc].Trim()).ToList();
        }

        // servidor: Rice\res\ShopCars.json (a loja na ordem do jogo; os bots so usam esses) e Rice\res\carros_cliente.json
        // (carros que este cliente nao tem). shop: {id, nome, V}. So carros que o servidor conhece (res\VehicleList.json).
        static void WriteServer(string game, List<string[]> shop, List<string> missing)
        {
            string resDir = Path.Combine(game, "Rice", "res");
            if (!Directory.Exists(Path.Combine(game, "Rice"))) return;
            Directory.CreateDirectory(resDir);
            var known = new HashSet<string>();
            try
            {
                string vl = Path.Combine(resDir, "VehicleList.json");
                if (File.Exists(vl))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vl), "^\\s{1,4}\"([0-9]+)\"\\s*:\\s*\\{", RegexOptions.Multiline)) known.Add(m.Groups[1].Value);
            }
            catch (Exception) { }
            if (known.Count > 0) shop = shop.Where(e => known.Contains(e[0])).ToList();
            var sb = new StringBuilder("[\n");
            for (int k = 0; k < shop.Count; k++)
            {
                string nm = shop[k][1].Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendFormat(" {{\"pos\": {0}, \"id\": {1}, \"name\": \"{2}\", \"v\": {3}}}{4}\n", k + 1, shop[k][0], nm, shop[k][2], k + 1 < shop.Count ? "," : "");
            }
            sb.Append("]\n");
            File.WriteAllText(Path.Combine(resDir, "ShopCars.json"), sb.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(resDir, "carros_cliente.json"),
                "{\n \"comment\": \"Carros que este cliente nao tem (sem modelo ou sem linha). O servidor nao usa esses carros (bots, leilao, troca).\",\n \"missing\": [" +
                string.Join(", ", missing) + "]\n}\n", new UTF8Encoding(false));
        }

        static int Int(string s, int def) { int v; return int.TryParse((s ?? "").Trim(), out v) ? v : def; }

        // ------------------------------------------------------------------------------------------- adaptar
        public const int Test = 0, Fix = 1, Keep = 2;
        // mode: Test = so ve o que mudaria; Fix = arruma a loja (e grava o servidor); Keep = deixa a loja do cliente como
        // esta e so grava o servidor com a loja DELE (os bots usam os carros que essa loja tem).
        // backup: pasta do backup desta instalacao (o pacote original vai para la antes de gravar)
        public static Result Adapt(string game, string backup, int mode, Action<string> log)
        {
            string rvd = RefText("VehicleDisplay"), rvl = RefText("VehicleList"), rcg = RefText("Loja");
            if (rvd == null || rvl == null || rcg == null) { log("ref"); return null; }
            var RV = new Table(rvd, false); var RL = new Table(rvl, true); var RC = new Table(rcg, false);

            var packs = new List<Pack>();
            foreach (var rel in new[] { @"Data_US\Init_us.agt", @"Data\Init.agt" })
            {
                string p = Path.Combine(game, rel);
                if (File.Exists(p)) packs.Add(new Pack(p));
            }
            Pack pvd = null, pvl = null, pcg = null;
            int ivd = -1, ivl = -1, icg = -1;
            string tvd = null, tvl = null, tcg = null;
            foreach (var pk in packs) if (pvd == null) { ivd = pk.Find(IsVd, out tvd); if (ivd >= 0) pvd = pk; }
            foreach (var pk in packs) if (pvl == null) { ivl = pk.Find(IsVl, out tvl); if (ivl >= 0) pvl = pk; }
            foreach (var pk in packs) if (pcg == null) { icg = pk.Find(IsCg, out tcg); if (icg >= 0) pcg = pk; }
            if (pvd == null || pvl == null || pcg == null) { log("tabelas"); return null; }
            var VD = new Table(tvd, false); var VL = new Table(tvl, true); var CG = new Table(tcg, false);

            // cupons de carro (Coupon Book): a linha do mesmo carro e mesmo cupom fica igual a da referencia
            Pack pcp = null; int icp = -1; string tcp = null;
            foreach (var pk in packs) if (pcp == null) { icp = pk.Find(IsCp, out tcp); if (icp >= 0) pcp = pk; }
            List<string> cpLines = null;
            int coupons = 0;
            string rcp = RefText("Cupons");
            if (pcp != null && rcp != null)
            {
                var rk = new Dictionary<string, string>();
                foreach (var l in rcp.Split(new[] { "\r\n" }, StringSplitOptions.None)) { string k = CpKey(l); if (k != null && !rk.ContainsKey(k)) rk[k] = l; }
                cpLines = tcp.Split(new[] { "\r\n" }, StringSplitOptions.None).ToList();
                for (int j = 0; j < cpLines.Count; j++)
                {
                    string k = CpKey(cpLines[j]);
                    if (k != null && rk.ContainsKey(k) && rk[k] != cpLines[j]) { cpLines[j] = rk[k]; coupons++; }
                }
            }
            var have = Models(game);
            var res = new Result();
            // trava: se a busca dos modelos "nao achar" muitos carros da nossa loja, ela falhou (nome diferente no pacote):
            // nao tira nenhum carro da loja por falta de modelo
            if (have.Count > 0)
            {
                int refCars = 0, found = 0;
                foreach (var kv in RV.Rows())
                {
                    var c = kv.Value;
                    if (c.Length <= RV.Col["File Name"] || c[RV.Col["Desc"]] != "PlayerCar" || c[RV.Col["Support"]].Trim() != "1") continue;
                    string f = c[RV.Col["File Name"]].Trim().ToLowerInvariant();
                    if (!f.StartsWith("pc_")) continue;
                    refCars++;
                    if (have.Contains(f.EndsWith(".nif") ? f.Substring(0, f.Length - 4) : f)) found++;
                }
                if (refCars > 0 && found < refCars * 0.7) { log("modelos"); have.Clear(); }
            }

            // ---- VehicleDisplay
            var rv = new Dictionary<string, string[]>();
            foreach (var kv in RV.Rows())
                if (kv.Value.Length > RV.Col["Display"] && kv.Value[RV.Col["Desc"]] == "PlayerCar" && !rv.ContainsKey(kv.Value[RV.Col["Unique Id"]]))
                    rv[kv.Value[RV.Col["Unique Id"]]] = kv.Value;
            var cv = new Dictionary<string, int>();
            foreach (var kv in VD.Rows())
                if (kv.Value.Length > VD.Col["Display"] && kv.Value[VD.Col["Desc"]] == "PlayerCar" && !cv.ContainsKey(kv.Value[VD.Col["Unique Id"]]))
                    cv[kv.Value[VD.Col["Unique Id"]]] = kv.Key;
            Func<string, string[]> crow = u => VD.Lines[cv[u]].Split('\t');
            Func<string, bool> modelOk = u =>
            {
                string f = crow(u)[VD.Col["File Name"]].Trim().ToLowerInvariant();
                string b = f.EndsWith(".nif") ? f.Substring(0, f.Length - 4) : f;
                return have.Count == 0 || !b.StartsWith("pc_") || have.Contains(b);
            };
            Func<string, bool> sup = u => crow(u)[VD.Col["Support"]].Trim() == "1";
            Func<string, bool> rsup = u => rv.ContainsKey(u) && rv[u][RV.Col["Support"]].Trim() == "1";
            Func<string, bool> available = u => cv.ContainsKey(u) && modelOk(u) && (sup(u) || (rv.ContainsKey(u) && !rsup(u)));

            var missing0 = shopIds0(RC).Where(u => rv.ContainsKey(u) && rsup(u) && !(cv.ContainsKey(u) && sup(u) && modelOk(u))).Distinct().ToList();
            if (mode == Keep)
            {
                // loja do cliente como esta: os carros ligados com modelo, na ordem da loja dele
                var own = shopIds0(CG).Where(u => cv.ContainsKey(u) && sup(u) && modelOk(u)).Distinct()
                    .OrderBy(u => Int(crow(u)[VD.Col["Display"]], 0)).ToList();
                res.Shop = own.Count; res.Missing = missing0.Count;
                WriteServer(game, own.Select(u => new[] { u, crow(u)[VD.Col["UI Name"]].Trim(), Int(crow(u)[VD.Col["Lvl"]], 9).ToString() }).ToList(), missing0);
                return res;
            }

            foreach (var kv in cv.ToList())
            {
                string u = kv.Key;
                if (!rv.ContainsKey(u)) continue;
                var c = VD.Lines[kv.Value].Split('\t'); var r = rv[u];
                string before = string.Join("\t", c);
                foreach (var name in VdCopy)
                    if (VD.Col.ContainsKey(name) && RV.Col.ContainsKey(name) && RV.Col[name] < r.Length && VD.Col[name] < c.Length) c[VD.Col[name]] = r[RV.Col[name]];
                if (r[RV.Col["Support"]].Trim() == "1" && c[VD.Col["Support"]].Trim() != "1" && modelOk(u)) c[VD.Col["Support"]] = "1";
                string after = string.Join("\t", c);
                if (after != before) { VD.Lines[kv.Value] = after; res.VdRows++; }
            }

            // ---- VehicleList (mesmo carro e mesmo V)
            Func<Table, string[], string> vkey = (t, c) => c[t.Col["ID"]].Trim() + "|" + (c.Length > t.Col["GradeLvl"] ? c[t.Col["GradeLvl"]].Trim() : "");
            var rl = new Dictionary<string, string[]>();
            foreach (var kv in RL.Rows()) if (kv.Value.Length > RL.Col["GradeLvl"]) { string k = vkey(RL, kv.Value); if (!rl.ContainsKey(k)) rl[k] = kv.Value; }
            foreach (var kv in VL.Rows().ToList())
            {
                var c = kv.Value;
                if (c.Length <= VL.Col["GradeLvl"]) continue;
                string[] r;
                if (!rl.TryGetValue(vkey(VL, c), out r)) continue;
                string before = string.Join("\t", c);
                foreach (var name in VlCopy)
                    if (VL.Col.ContainsKey(name) && RL.Col.ContainsKey(name) && RL.Col[name] < r.Length && VL.Col[name] < c.Length) c[VL.Col[name]] = r[RL.Col[name]];
                string after = string.Join("\t", c);
                if (after != before) { VL.Lines[kv.Key] = after; res.VlRows++; }
            }

            // ---- loja
            Func<Table, List<string>> shopIds = t =>
            {
                int uc = t.Col["Unique Id"];
                return t.Rows().Where(kv => kv.Value.Length > uc && Int(kv.Value[uc], -1) >= 0).Select(kv => kv.Value[uc].Trim()).ToList();
            };
            var refOrder = shopIds(RC).Where(u => rv.ContainsKey(u)).Distinct().OrderBy(u => Int(rv[u][RV.Col["Display"]], 0)).ToList();
            var pos = new Dictionary<string, int>();
            for (int k = 0; k < refOrder.Count; k++) pos[refOrder[k]] = k;
            Func<string, int> lvl = u => Int(crow(u)[VD.Col["Lvl"]], 9);
            Func<string, int> group = u => rv.ContainsKey(u)
                ? (Special(rv[u][RV.Col["UI Name"]]) ? 10 : Int(rv[u][RV.Col["Lvl"]], 9))
                : (Special(crow(u)[VD.Col["UI Name"]]) ? 10 : lvl(u));
            var mine = shopIds(CG).Where(u => !pos.ContainsKey(u) && cv.ContainsKey(u) && sup(u) && modelOk(u)).Distinct().ToList();
            Func<string, int> cdisp = u => Int(crow(u)[VD.Col["Display"]], 0);
            var final = refOrder.Where(available).Concat(mine)
                .OrderBy(u => group(u)).ThenBy(u => pos.ContainsKey(u) ? 0 : 1).ThenBy(u => pos.ContainsKey(u) ? pos[u] : cdisp(u))
                .Distinct().ToList();
            var missing = refOrder.Where(u => rsup(u) && !(cv.ContainsKey(u) && sup(u) && modelOk(u))).ToList();

            var rrow = new Dictionary<string, string[]>();
            foreach (var kv in RC.Rows()) if (kv.Value.Length > RC.Col["Unique Id"] && !rrow.ContainsKey(kv.Value[RC.Col["Unique Id"]].Trim())) rrow[kv.Value[RC.Col["Unique Id"]].Trim()] = kv.Value;
            var ccg = new Dictionary<string, string[]>();
            foreach (var kv in CG.Rows()) if (kv.Value.Length > CG.Col["Unique Id"] && !ccg.ContainsKey(kv.Value[CG.Col["Unique Id"]].Trim())) ccg[kv.Value[CG.Col["Unique Id"]].Trim()] = kv.Value;
            bool same = RC.Lines[RC.Hdr] == CG.Lines[CG.Hdr];
            var keep = new HashSet<string>(final);
            List<string> order;
            bool renum;
            if (mine.Count == 0)
            {
                // so carros da referencia: posicao (Display, ja copiada) e linhas da lista iguais as da referencia
                order = RC.Rows().Where(kv => kv.Value.Length > RC.Col["Unique Id"]).Select(kv => kv.Value[RC.Col["Unique Id"]].Trim()).Where(keep.Contains).Distinct().ToList();
                renum = order.Count != rrow.Count;
            }
            else
            {
                order = final; renum = true;
                for (int k = 0; k < final.Count; k++)
                {
                    var c = crow(final[k]);
                    if (c[VD.Col["Display"]] != (k + 1).ToString()) { c[VD.Col["Display"]] = (k + 1).ToString(); VD.Lines[cv[final[k]]] = string.Join("\t", c); res.Positions++; }
                }
            }
            var bodyLines = new List<string>();
            int ncol = CG.Lines[CG.Hdr].Split('\t').Length;
            for (int k = 0; k < order.Count; k++)
            {
                string u = order[k];
                string[] c;
                if (same && rrow.ContainsKey(u)) c = (string[]) rrow[u].Clone();
                else if (ccg.ContainsKey(u)) c = (string[]) ccg[u].Clone();
                else
                {
                    c = new string[ncol];
                    for (int j = 0; j < ncol; j++) c[j] = "";
                    var r = crow(u);
                    c[CG.Col["Car Name"]] = r[VD.Col["Car Name"]]; c[CG.Col["UI Name"]] = r[VD.Col["UI Name"]]; c[CG.Col["Unique Id"]] = u;
                    foreach (var n in new[] { "New", "Hot", "GM" }) c[CG.Col[n]] = "0";
                    c[CG.Col["Category"]] = "7";
                }
                if (renum || !same) c[0] = k.ToString();
                bodyLines.Add(string.Join("\t", c));
            }
            var head = CG.Lines.Take(CG.Hdr + 1).ToList();
            for (int j = 0; j < head.Count; j++)
                if (head[j].StartsWith("Value\t")) { var c = head[j].Split('\t'); c[1] = order.Count.ToString(); head[j] = string.Join("\t", c); }
            var tail = CG.Lines.Skip(CG.Hdr + 1).Where(l => Int(l.Split('\t')[0], -1) < 0).ToList();
            if (tail.Count == 0) tail.Add("");
            var newCg = head.Concat(bodyLines).Concat(tail).ToList();
            res.NewList = !newCg.SequenceEqual(CG.Lines);
            CG.Lines = newCg;

            res.Shop = order.Count; res.Mine = mine.Count; res.Missing = missing.Count;
            res.Coupons = coupons;
            res.Pending = res.VdRows > 0 || res.VlRows > 0 || res.Positions > 0 || res.NewList || coupons > 0;
            if (mode == Fix)
            {
                if (res.VdRows > 0 || res.Positions > 0) pvd.Store(ivd, VD.Text());
                if (res.VlRows > 0) pvl.Store(ivl, VL.Text());
                if (res.NewList) pcg.Store(icg, CG.Text());
                if (coupons > 0) pcp.Store(icp, string.Join("\r\n", cpLines));
                foreach (var pk in packs.Where(x => x.Changed))
                {
                    if (backup != null)
                    {
                        string rel = pk.Path.Substring(game.Length).TrimStart('\\', '/');
                        string b = Path.Combine(backup, rel);
                        if (!File.Exists(b)) { Directory.CreateDirectory(Path.GetDirectoryName(b)); File.Copy(pk.Path, b, false); }
                    }
                    var a = File.GetAttributes(pk.Path);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(pk.Path, a & ~FileAttributes.ReadOnly);
                    File.WriteAllBytes(pk.Path, pk.D);
                    res.Written.Add(pk.Path.Substring(game.Length).TrimStart('\\', '/'));
                    res.Changed = true;
                }
                var shop = order.Where(u => cv.ContainsKey(u)).OrderBy(u => Int(crow(u)[VD.Col["Display"]], 0)).ToList();
                WriteServer(game, shop.Select(u => new[] { u, crow(u)[VD.Col["UI Name"]].Trim(), lvl(u).ToString() }).ToList(), missing);
            }
            return res;
        }
    }
}
