using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Rice
{
    // Idioma do Offline Aprimorado: "en" (padrao) ou "pt" (server.json "Language" / Menu.bat > Idioma).
    // O codigo continua escrito em portugues; na saida (console, chat do jogo, menu) o texto passa por T(), que troca
    // pelo ingles de res/lang/en.json (portugues -> ingles). Primeiro procura o texto inteiro; senao troca os pedacos
    // conhecidos (os textos que o codigo junta com nomes e numeros), do maior para o menor.
    public static class Lang
    {
        static string language = "en";
        public static string Language
        {
            get { return language; }
            set { language = string.Equals((value ?? "").Trim(), "pt", StringComparison.OrdinalIgnoreCase) ? "pt" : "en"; }
        }
        public static bool En { get { return language != "pt"; } }

        static Dictionary<string, string> exact;
        static KeyValuePair<string, string>[] frags;
        static readonly object Sync = new object();
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        static void Load()
        {
            if (exact != null) return;
            lock (Sync)
            {
                if (exact != null) return;
                var d = new Dictionary<string, string>();
                try
                {
                    string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "lang", "en.json");
                    if (File.Exists(p)) d = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(p, Encoding.UTF8)) ?? d;
                }
                catch (Exception) { }
                frags = d.Where(kv => Letters(kv.Key) >= 3).OrderByDescending(kv => kv.Key.Length).ToArray();
                exact = d;
            }
        }

        static int Letters(string s)
        {
            int n = 0;
            foreach (char ch in s) if (char.IsLetter(ch)) n++;
            return n;
        }

        // texto em portugues -> no idioma escolhido
        public static string T(string s)
        {
            if (!En || string.IsNullOrEmpty(s)) return s;
            Load();
            string r;
            if (exact.TryGetValue(s, out r)) return r;
            lock (Sync) { if (cache.TryGetValue(s, out r)) return r; }
            try { r = Frag(s); } catch (Exception) { r = s; }
            lock (Sync) { if (cache.Count > 5000) cache.Clear(); cache[s] = r; }
            return r;
        }

        static bool Letter(string w, int i) { return i >= 0 && i < w.Length && char.IsLetter(w[i]); }

        // troca os pedacos conhecidos; o que ja foi trocado vira marca (\u0001n\u0002) para nao ser trocado de novo
        static string Frag(string s)
        {
            var outs = new List<string>();
            string w = s;
            foreach (var kv in frags)
            {
                string k = kv.Key;
                if (k.Length > w.Length) continue;
                int i = w.IndexOf(k, StringComparison.Ordinal);
                if (i < 0) continue;
                var sb = new StringBuilder();
                int last = 0;
                bool any = false;
                while (i >= 0)
                {
                    bool okL = !char.IsLetter(k[0]) || !Letter(w, i - 1);
                    bool okR = !char.IsLetter(k[k.Length - 1]) || !Letter(w, i + k.Length);
                    if (okL && okR)
                    {
                        sb.Append(w, last, i - last);
                        sb.Append('\u0001').Append(outs.Count).Append('\u0002');
                        outs.Add(kv.Value);
                        last = i + k.Length;
                        any = true;
                        i = last < w.Length ? w.IndexOf(k, last, StringComparison.Ordinal) : -1;
                    }
                    else i = i + 1 < w.Length ? w.IndexOf(k, i + 1, StringComparison.Ordinal) : -1;
                }
                if (!any) continue;
                sb.Append(w, last, w.Length - last);
                w = sb.ToString();
            }
            if (outs.Count == 0) return s;
            var res = new StringBuilder();
            for (int j = 0; j < w.Length; j++)
            {
                if (w[j] == '\u0001')
                {
                    int e = w.IndexOf('\u0002', j);
                    res.Append(outs[int.Parse(w.Substring(j + 1, e - j - 1))]);
                    j = e;
                }
                else res.Append(w[j]);
            }
            return res.ToString();
        }
    }
}
