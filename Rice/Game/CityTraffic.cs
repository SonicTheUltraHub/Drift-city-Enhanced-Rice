using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Rice.Server.Core;

namespace Rice.Game
{
    // Trafego da cidade feito pelo servidor (o servidor original tinha um programa so para isso).
    // Le o mapa de faixas/semaforos (.tcs do cliente, em res/traffic, tambem embutido no Rice.exe), move os carros
    // uma vez por segundo e manda a cada jogador os carros e semaforos a ate CastRange metros.
    // Formatos e regras conforme a documentacao do emulador openDC (github.com/Xerender13/openDC), escrito aqui do zero:
    //   701 (0x2bd) carro: u32 tick, f32 posicao na faixa, f16 velocidade, f16 aceleracao, u16 id, u16 modelo,
    //                      i16 faixa, i16 proxima faixa, i16 -1
    //   706 (0x2c2) semaforo: u32 tick da ultima troca, i32 lampada, i32 estado (1 verde, 0 vermelho)
    public static class CityTraffic
    {
        const float Unit = 100f / 3.6f;          // metros por unidade de posicao na faixa
        const double Period = 1.0;
        const double Slot = 30, Green = 15;     // cruzamento: cada via fica verde 15 s de cada 30 s
        const double WalkRed = 30, WalkGreen = 28;
        const double AmberStop = 4, AmberGo = 1;
        const float StopGap = 7.5f, SignalGap = 4f, Comfort = 2f, Announce = 200f, Stopped = -100f;

        class SigAt { public float Pos; public int Sig; }

        class Lane
        {
            public int Id, Type, NextType, NextId;
            public float End, MaxSpeed, Delta = 1f;
            public int[] SigIds;
            public List<SigAt> Signals = new List<SigAt>();
            public List<float[]> Points = new List<float[]>();
            public List<int> Outs = new List<int>();

            public void At(float pos, out float x, out float y)
            {
                if (Points.Count == 0) { x = y = 0; return; }
                float f = Math.Max(pos, 0f) / Delta;
                int k = Math.Min((int) f, Points.Count - 2);
                if (k < 0) { x = Points[0][0]; y = Points[0][1]; return; }
                f = Math.Min(f - k, 1f);
                var a = Points[k]; var b = Points[k + 1];
                x = a[0] + (b[0] - a[0]) * f;
                y = a[1] + (b[1] - a[1]) * f;
            }
        }

        class City
        {
            public string Name;
            public Dictionary<int, Lane> Lanes = new Dictionary<int, Lane>();
            public Dictionary<int, KeyValuePair<int, float>> Signals = new Dictionary<int, KeyValuePair<int, float>>();
            public List<int[]> RawLamps = new List<int[]>();
            public Dictionary<int, List<int[]>> Controllers = new Dictionary<int, List<int[]>>();
            public Dictionary<int, int[]> Joints = new Dictionary<int, int[]>();
            public Dictionary<int, int[]> Phase = new Dictionary<int, int[]>();   // sinal -> (via, vias)
            public List<float[]> Lamps = new List<float[]>();                     // lampada, sinal, x, y
            public List<Lane> Spawn = new List<Lane>();
            public List<double> SpawnWeight = new List<double>();
            public List<int> Models = new List<int>();
            public List<double> ModelWeight = new List<double>();

            public bool State(int sig, double t, out double since)
            {
                double cycle, start, end;
                int[] ph;
                if (!Phase.TryGetValue(sig, out ph)) { cycle = WalkRed + WalkGreen; start = WalkRed; end = WalkRed + WalkGreen; }
                else { cycle = ph[1] * Slot; start = ph[0] * Slot; end = ph[0] * Slot + Green; }
                double p = t % cycle;
                if (p >= start && p < end) { since = p - start; return true; }
                since = ((p - end) % cycle + cycle) % cycle;
                return false;
            }

            public char Light(int sig, double t)
            {
                double since;
                bool go = State(sig, t, out since);
                if (since < (go ? AmberGo : AmberStop)) return 'A';
                return go ? 'G' : 'R';
            }
        }

        class Car
        {
            public int Id, Model;
            public Lane Lane;
            public float Pos, V, A, X, Y;
            public int Next = -1;
            public bool Stopped;
            public DateTime FreeUntil = DateTime.MinValue;   // batido: nao manda ate essa hora
        }

        class Traffic
        {
            public City City;
            public List<Car> Cars = new List<Car>();
            public Dictionary<int, List<Car>> LaneCars = new Dictionary<int, List<Car>>();
            public double T;
            public Random Rng = new Random();
            public DateTime LastSeen = DateTime.Now;
        }

        static readonly object Sync = new object();
        static readonly Dictionary<int, City> cities = new Dictionary<int, City>();
        static readonly Dictionary<int, Traffic> running = new Dictionary<int, Traffic>();
        static Timer timer;
        static JObject models;

        // cidade do mundo (Character.City) -> arquivo
        static readonly Dictionary<int, string> Files = new Dictionary<int, string>
        {
            { 1, "moonpalace" }, { 2, "koinonia" }, { 3, "cras" }, { 4, "oros" }
        };

        public static void Start()
        {
            if (!RiceServer.Config.CityTrafficEnabled || timer != null) return;
            timer = new Timer(_ => Tick(), null, 2000, 1000);
            Log.WriteLine("Trafego da cidade ligado.");
        }

        // ------------------------------------------------------------------ leitura

        static byte[] ReadRes(string name)
        {
            string[] paths = { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "traffic", name), Path.Combine("res", "traffic", name) };
            foreach (var p in paths)
                if (File.Exists(p)) return File.ReadAllBytes(p);
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("traffic." + name))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static byte[] Gunzip(byte[] gz)
        {
            using (var g = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress))
            {
                var ms = new MemoryStream();
                g.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // Ponto aleatorio numa faixa de rua (para estacionar bots fora das construcoes), entre minD e maxD metros
        // de (nearX, nearY). dir = direcao da rua em radianos (atan2). Unidades das faixas = as do jogo.
        public static bool RoadSpot(int cityId, Random rng, float nearX, float nearY, float minD, float maxD,
                                    out float x, out float y, out float z, out float dir)
        {
            x = y = z = dir = 0;
            City c;
            lock (Sync) c = Load(cityId);
            if (c == null || c.Spawn.Count == 0) return false;
            for (int tries = 0; tries < 400; tries++)
            {
                var lane = c.Spawn[rng.Next(c.Spawn.Count)];
                if (lane.Points.Count < 2) continue;
                int k = rng.Next(lane.Points.Count - 1);
                var a = lane.Points[k]; var b = lane.Points[k + 1];
                float f = (float) rng.NextDouble();
                float px = a[0] + (b[0] - a[0]) * f, py = a[1] + (b[1] - a[1]) * f;
                float dx = px - nearX, dy = py - nearY, d = (float) Math.Sqrt(dx * dx + dy * dy);
                if (d < minD || d > maxD) continue;
                x = px; y = py; z = a.Length > 2 ? a[2] + (b[2] - a[2]) * f : 0f;
                dir = (float) Math.Atan2(b[1] - a[1], b[0] - a[0]);
                return true;
            }
            return false;
        }

        // Limites da cidade pelas faixas de rua: {xmin, xmax, ymin, ymax}
        static readonly Dictionary<int, float[]> bounds = new Dictionary<int, float[]>();
        public static float[] Bounds(int cityId)
        {
            lock (Sync)
            {
                float[] b;
                if (bounds.TryGetValue(cityId, out b)) return b;
                var c = Load(cityId);
                if (c == null || c.Lanes.Count == 0) return null;
                b = new[] { float.MaxValue, float.MinValue, float.MaxValue, float.MinValue };
                foreach (var l in c.Lanes.Values)
                    foreach (var p in l.Points)
                    {
                        b[0] = Math.Min(b[0], p[0]); b[1] = Math.Max(b[1], p[0]);
                        b[2] = Math.Min(b[2], p[1]); b[3] = Math.Max(b[3], p[1]);
                    }
                bounds[cityId] = b;
                return b;
            }
        }

        // Ponto aleatorio numa faixa de rua dentro do retangulo [x0, x1] x [y0, y1]
        public static bool RoadSpotIn(int cityId, Random rng, float x0, float x1, float y0, float y1,
                                      out float x, out float y, out float z, out float dir)
        {
            x = y = z = dir = 0;
            City c;
            lock (Sync) c = Load(cityId);
            if (c == null || c.Spawn.Count == 0) return false;
            for (int tries = 0; tries < 800; tries++)
            {
                var lane = c.Spawn[rng.Next(c.Spawn.Count)];
                if (lane.Points.Count < 2) continue;
                int k = rng.Next(lane.Points.Count - 1);
                var a = lane.Points[k]; var b = lane.Points[k + 1];
                float f = (float) rng.NextDouble();
                float px = a[0] + (b[0] - a[0]) * f, py = a[1] + (b[1] - a[1]) * f;
                if (px < x0 || px > x1 || py < y0 || py > y1) continue;
                x = px; y = py; z = a.Length > 2 ? a[2] + (b[2] - a[2]) * f : 0f;
                dir = (float) Math.Atan2(b[1] - a[1], b[0] - a[0]);
                return true;
            }
            return false;
        }

        // Ha um carro do transito na frente (ate 'range' m, na direcao 'dir')? Para os bots ultrapassarem pelo lado.
        public static bool TrafficAhead(int cityId, float x, float y, float dir, float range)
        {
            lock (Sync)
            {
                Traffic tr;
                if (!running.TryGetValue(cityId, out tr)) return false;
                float fx = (float) Math.Cos(dir), fy = (float) Math.Sin(dir);
                foreach (var car in tr.Cars)
                {
                    float dx = car.X - x, dy = car.Y - y;
                    float ahead = dx * fx + dy * fy;
                    if (ahead <= 0 || ahead > range) continue;
                    float side = Math.Abs(-dx * fy + dy * fx);
                    if (side < 2.5f) return true;
                }
                return false;
            }
        }

        // Cursor numa faixa de rua: bots que andam sozinhos pela cidade seguem as faixas do transito.
        public class RoadCursor { public int Lane; public int Seg; public float Off; }

        public static RoadCursor StartCursor(int cityId, Random rng, float nearX, float nearY, float minD, float maxD)
        {
            City c;
            lock (Sync) c = Load(cityId);
            if (c == null || c.Spawn.Count == 0) return null;
            for (int tries = 0; tries < 400; tries++)
            {
                var lane = c.Spawn[rng.Next(c.Spawn.Count)];
                if (lane.Points.Count < 2) continue;
                int k = rng.Next(lane.Points.Count - 1);
                var a = lane.Points[k];
                float dx = a[0] - nearX, dy = a[1] - nearY, d = (float) Math.Sqrt(dx * dx + dy * dy);
                if (d < minD || d > maxD) continue;
                return new RoadCursor { Lane = lane.Id, Seg = k, Off = 0 };
            }
            return null;
        }

        // Anda 'dist' unidades na faixa (passa para a proxima faixa no fim). false = sem caminho.
        public static bool Advance(int cityId, RoadCursor cur, float dist, Random rng,
                                   out float x, out float y, out float z, out float dir)
        {
            x = y = z = dir = 0;
            City c;
            lock (Sync) c = Load(cityId);
            Lane lane;
            if (c == null || cur == null || !c.Lanes.TryGetValue(cur.Lane, out lane) || lane.Points.Count < 2) return false;
            for (int guard = 0; guard < 200; guard++)
            {
                if (cur.Seg >= lane.Points.Count - 1)
                {
                    Lane next = null;
                    var outs = lane.Outs.Where(o => c.Lanes.ContainsKey(o) && c.Lanes[o].Points.Count > 1).ToList();
                    if (outs.Count > 0) next = c.Lanes[outs[rng.Next(outs.Count)]];
                    if (next == null) return false;
                    lane = next; cur.Lane = lane.Id; cur.Seg = 0; cur.Off = 0;
                }
                var a = lane.Points[cur.Seg]; var b = lane.Points[cur.Seg + 1];
                float sx = b[0] - a[0], sy = b[1] - a[1];
                float len = (float) Math.Sqrt(sx * sx + sy * sy);
                if (len < 0.001f) { cur.Seg++; cur.Off = 0; continue; }
                if (cur.Off + dist <= len)
                {
                    cur.Off += dist;
                    float f = cur.Off / len;
                    x = a[0] + sx * f; y = a[1] + sy * f;
                    z = a.Length > 2 ? a[2] + (b[2] - a[2]) * f : 0f;
                    dir = (float) Math.Atan2(sy, sx);
                    return true;
                }
                dist -= len - cur.Off;
                cur.Seg++; cur.Off = 0;
            }
            return false;
        }

        // Caminho pelas ruas (faixas do transito) de (fx, fy) ate perto de (tx, ty): lista de pontos {x, y, z}.
        // null = sem caminho (cidade sem faixas ou ponto longe de qualquer rua).
        public static List<float[]> Route(int cityId, float fx, float fy, float tx, float ty)
        {
            City c;
            lock (Sync) c = Load(cityId);
            if (c == null || c.Lanes.Count == 0) return null;
            Lane la = null, lb = null; int ka = 0, kb = 0; float da = float.MaxValue, db = float.MaxValue;
            foreach (var l in c.Lanes.Values)
                for (int k = 0; k < l.Points.Count; k++)
                {
                    var p = l.Points[k];
                    float d1 = (p[0] - fx) * (p[0] - fx) + (p[1] - fy) * (p[1] - fy);
                    float d2 = (p[0] - tx) * (p[0] - tx) + (p[1] - ty) * (p[1] - ty);
                    if (d1 < da) { da = d1; la = l; ka = k; }
                    if (d2 < db) { db = d2; lb = l; kb = k; }
                }
            if (la == null || lb == null || da > 80f * 80f || db > 80f * 80f) return null;
            var path = new List<float[]>();
            if (la == lb && kb >= ka) { for (int k = ka; k <= kb; k++) path.Add(la.Points[k]); path.Add(new[] { tx, ty, lb.Points[kb].Length > 2 ? lb.Points[kb][2] : 0f }); return path; }
            // Dijkstra pelas faixas (custo = comprimento da faixa); vale andar nos dois sentidos (bot nao respeita mao)
            Func<Lane, float> len = ln => { float sum = 0; for (int q = 1; q < ln.Points.Count; q++) { float ex = ln.Points[q][0] - ln.Points[q - 1][0], ey = ln.Points[q][1] - ln.Points[q - 1][1]; sum += (float) Math.Sqrt(ex * ex + ey * ey); } return sum; };
            var adj = new Dictionary<int, List<int>>();
            foreach (var l in c.Lanes.Values)
                foreach (var o in l.Outs)
                    if (c.Lanes.ContainsKey(o))
                    {
                        List<int> a; if (!adj.TryGetValue(l.Id, out a)) adj[l.Id] = a = new List<int>(); a.Add(o);
                        if (!adj.TryGetValue(o, out a)) adj[o] = a = new List<int>(); a.Add(l.Id);
                    }
            var dist = new Dictionary<int, float> { { la.Id, 0f } };
            var prev = new Dictionary<int, int>();
            var open = new HashSet<int> { la.Id };
            var done = new HashSet<int>();
            while (open.Count > 0)
            {
                int u = open.OrderBy(x => dist[x]).First();
                open.Remove(u);
                if (u == lb.Id) break;
                done.Add(u);
                List<int> nb;
                if (!adj.TryGetValue(u, out nb)) continue;
                float du = dist[u] + len(c.Lanes[u]);
                foreach (var v in nb)
                {
                    if (done.Contains(v)) continue;
                    float old;
                    if (!dist.TryGetValue(v, out old) || du < old) { dist[v] = du; prev[v] = u; open.Add(v); }
                }
                if (done.Count > 4000) break;
            }
            if (!dist.ContainsKey(lb.Id)) return null;
            var chain = new List<int>();
            for (int x = lb.Id; ; x = prev[x]) { chain.Add(x); if (x == la.Id || !prev.ContainsKey(x)) break; }
            chain.Reverse();
            // monta os pontos: cada faixa no sentido que liga com a seguinte
            float cx = fx, cy = fy;
            for (int i = 0; i < chain.Count; i++)
            {
                var pts = c.Lanes[chain[i]].Points;
                if (pts.Count == 0) continue;
                var here = new[] { cx, cy };
                int start, end;
                if (i == 0)
                {
                    start = ka;
                    end = pts.Count - 1;
                    if (chain.Count > 1)
                    {
                        var nxt = c.Lanes[chain[1]].Points;
                        if (nxt.Count > 0)
                        {
                            float e0 = Math.Min(D2(pts[0], nxt[0]), D2(pts[0], nxt[nxt.Count - 1]));
                            float e1 = Math.Min(D2(pts[pts.Count - 1], nxt[0]), D2(pts[pts.Count - 1], nxt[nxt.Count - 1]));
                            end = e0 < e1 ? 0 : pts.Count - 1;
                        }
                    }
                }
                else
                {
                    start = D2(pts[0], here) <= D2(pts[pts.Count - 1], here) ? 0 : pts.Count - 1;
                    end = i == chain.Count - 1 ? kb : (start == 0 ? pts.Count - 1 : 0);
                }
                int step = end >= start ? 1 : -1;
                for (int k = start; ; k += step)
                {
                    path.Add(pts[k]);
                    if (k == end) break;
                }
                var lastp = path[path.Count - 1]; cx = lastp[0]; cy = lastp[1];
            }
            path.Add(new[] { tx, ty, path.Count > 0 && path[path.Count - 1].Length > 2 ? path[path.Count - 1][2] : 0f });
            return path;
        }

        // em que cidade voce esta pela posicao: a cidade com uma rua a menos de 60 m (o jogo nem sempre avisa a troca
        // de cidade, e o transito/bots ficavam com as ruas de Moon Palace em Koinonia, Cras e Oros)
        static readonly Dictionary<ulong, DateTime> cityCheck = new Dictionary<ulong, DateTime>();
        static int DetectCity(int current, float x, float y)
        {
            int best = 0; float bd = float.MaxValue;
            foreach (var id in Files.Keys)
            {
                var c = Load(id);
                if (c == null) continue;
                float md = float.MaxValue;
                foreach (var l in c.Lanes.Values)
                    for (int k = 0; k < l.Points.Count; k += 2)
                    {
                        var p = l.Points[k];
                        float d = (p[0] - x) * (p[0] - x) + (p[1] - y) * (p[1] - y);
                        if (d < md) md = d;
                    }
                if (id == current && md < 60f * 60f) return current;
                if (md < bd) { bd = md; best = id; }
            }
            return bd < 60f * 60f ? best : current;
        }

        static float D2(float[] a, float[] b) { return (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]); }

        static City Load(int cityId)
        {
            City c;
            if (cities.TryGetValue(cityId, out c)) return c;
            cities[cityId] = null;
            string file;
            if (!Files.TryGetValue(cityId, out file)) return null;
            try
            {
                var gz = ReadRes(file + ".tcs.gz");
                if (gz == null) { Log.WriteError("Trafego: " + file + ".tcs.gz nao encontrado."); return null; }
                c = Parse(Gunzip(gz));
                c.Name = file;
                if (models == null)
                {
                    var mj = ReadRes("models.json");
                    models = mj != null ? JObject.Parse(System.Text.Encoding.UTF8.GetString(mj)) : new JObject();
                }
                var list = models[cityId.ToString()] as JArray;
                double acc = 0;
                if (list != null)
                    foreach (var m in list) { c.Models.Add((int) m[0]); acc += (int) m[1]; c.ModelWeight.Add(acc); }
                if (c.Models.Count == 0) { c.Models.Add(11); c.ModelWeight.Add(1); }
                cities[cityId] = c;
                Log.WriteLine("Trafego {0}: {1} faixas, {2} semaforos, {3} modelos de carro.", file, c.Lanes.Count, c.Lamps.Count, c.Models.Count);
                return c;
            }
            catch (Exception ex)
            {
                Log.WriteError("Trafego " + file + ": " + ex.Message);
                return null;
            }
        }

        static City Parse(byte[] d)
        {
            if (d[0] != 'T' || d[1] != 'C' || d[2] != 'S' || d[3] != 0) throw new InvalidDataException("nao e um TCS");
            var c = new City();
            int o = 10;
            for (int i = 0; i < 3; i++) { while (d[o] != 0) o++; o++; }
            Walk(c, d, o, d.Length);

            foreach (var lane in c.Lanes.Values)
            {
                if (lane.NextType == 0 && c.Lanes.ContainsKey(lane.NextId)) lane.Outs.Add(lane.NextId);
                else if (lane.NextType == 1)
                {
                    int[] outs;
                    if (c.Joints.TryGetValue(lane.NextId, out outs))
                        lane.Outs.AddRange(outs.Where(x => c.Lanes.ContainsKey(x)));
                }
                foreach (var s in lane.SigIds)
                {
                    KeyValuePair<int, float> sp;
                    if (c.Signals.TryGetValue(s, out sp)) lane.Signals.Add(new SigAt { Pos = sp.Value, Sig = s });
                }
                lane.Signals.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            }
            foreach (var ctl in c.Controllers.Values)
                for (int i = 0; i < ctl.Count; i++)
                    foreach (var s in ctl[i]) c.Phase[s] = new[] { i, ctl.Count };
            foreach (var l in c.RawLamps)
            {
                KeyValuePair<int, float> sp;
                Lane lane;
                if (c.Signals.TryGetValue(l[1], out sp) && c.Lanes.TryGetValue(sp.Key, out lane))
                {
                    float x, y;
                    lane.At(sp.Value, out x, out y);
                    c.Lamps.Add(new float[] { l[0], l[1], x, y });
                }
            }
            double acc = 0;
            foreach (var lane in c.Lanes.Values.Where(l => l.Type == 0 && l.Points.Count > 1 && l.End > 0))
            {
                c.Spawn.Add(lane);
                acc += lane.End;
                c.SpawnWeight.Add(acc);
            }
            return c;
        }

        static int I(byte[] d, int o) { return BitConverter.ToInt32(d, o); }
        static float F(byte[] d, int o) { return BitConverter.ToSingle(d, o); }

        static void Walk(City c, byte[] d, int o, int end)
        {
            while (o + 8 <= end)
            {
                uint cid = BitConverter.ToUInt32(d, o);
                int ln = I(d, o + 4);
                if (ln < 8 || o + ln > end) throw new InvalidDataException("bloco TCS invalido");
                int b = o + 8;
                switch (cid)
                {
                    case 0x5: case 0xA0000: case 0x70000: case 0xA5000: case 0x10000: case 0x20000: case 0x50000: case 0x75000:
                        Walk(c, d, b, o + ln);
                        break;
                    case 0xA0030:
                        c.Signals[I(d, b + 4)] = new KeyValuePair<int, float>(I(d, b + 20), F(d, b + 8));
                        break;
                    case 0xA6000:
                    {
                        int q = b + I(d, b + 4);
                        if (q + 12 <= o + ln && BitConverter.ToUInt32(d, q) == 0xA6030)
                        {
                            int n = I(d, q + 8);
                            for (int i = 0; i < n; i++)
                                c.RawLamps.Add(new[] { I(d, q + 12 + 16 * i), I(d, q + 12 + 16 * i + 12) });
                        }
                        break;
                    }
                    case 0x70200:
                    {
                        int n = I(d, b + 64);
                        var sigs = new int[n];
                        for (int i = 0; i < n; i++) sigs[i] = I(d, b + 68 + 4 * i);
                        var lane = new Lane
                        {
                            Id = I(d, b + 4), Type = I(d, b + 8), End = F(d, b + 16), MaxSpeed = F(d, b + 20),
                            NextType = I(d, b + 56), NextId = I(d, b + 60), SigIds = sigs
                        };
                        c.Lanes[lane.Id] = lane;
                        break;
                    }
                    case 0x70300:
                    {
                        Lane lane;
                        if (c.Lanes.TryGetValue(I(d, b + 4), out lane))
                        {
                            float delta = F(d, b + 8);
                            int n = I(d, b + 12);
                            lane.Delta = delta != 0 ? delta : 1f;
                            for (int i = 0; i < n; i++)
                                lane.Points.Add(new[] { F(d, b + 16 + 12 * i), F(d, b + 20 + 12 * i), F(d, b + 24 + 12 * i) });
                        }
                        break;
                    }
                    case 0x75200:
                    {
                        int ni = I(d, b + 12), no = I(d, b + 16);
                        var outs = new int[no];
                        for (int i = 0; i < no; i++) outs[i] = I(d, b + 20 + 4 * ni + 4 * i);
                        c.Joints[I(d, b + 4)] = outs;
                        break;
                    }
                    case 0xA5030:
                    {
                        int id = I(d, b + 4), roads = I(d, b + 8), q = b + 12;
                        var list = new List<int[]>();
                        for (int r = 0; r < roads; r++)
                        {
                            int n = I(d, q);
                            var s = new int[n];
                            for (int i = 0; i < n; i++) s[i] = I(d, q + 4 + 4 * i);
                            list.Add(s);
                            q += 4 + 4 * n;
                        }
                        c.Controllers[id] = list;
                        break;
                    }
                }
                o += ln;
            }
        }

        // ------------------------------------------------------------------ simulacao

        static int Pick(List<double> cumul, Random rng)
        {
            double r = rng.NextDouble() * cumul[cumul.Count - 1];
            int i = cumul.BinarySearch(r);
            if (i < 0) i = ~i;
            return Math.Min(i, cumul.Count - 1);
        }

        static Traffic NewTraffic(City c, int count)
        {
            var tr = new Traffic { City = c };
            for (int i = 0; i < count; i++)
            {
                var car = new Car { Id = i, Model = c.Models[Pick(c.ModelWeight, tr.Rng)] };
                Place(tr, car, (float) tr.Rng.NextDouble());
                car.V = car.Lane.MaxSpeed * new[] { 0f, 0.5f, 1f }[tr.Rng.Next(3)];
                tr.Cars.Add(car);
            }
            Replan(tr);
            return tr;
        }

        static void Place(Traffic tr, Car car, float frac)
        {
            Lane lane = null;
            float pos = 0;
            for (int k = 0; k < 20; k++)
            {
                lane = tr.City.Spawn[Pick(tr.City.SpawnWeight, tr.Rng)];
                pos = lane.End * frac;
                List<Car> cs;
                if (!tr.LaneCars.TryGetValue(lane.Id, out cs) || cs.All(o => Math.Abs(o.Pos - pos) * Unit > 2 * StopGap)) break;
            }
            MoveTo(tr, car, lane, pos);
            car.V = 0; car.Stopped = false;
        }

        static void MoveTo(Traffic tr, Car car, Lane lane, float pos)
        {
            List<Car> cs;
            if (car.Lane != null && tr.LaneCars.TryGetValue(car.Lane.Id, out cs)) cs.Remove(car);
            car.Lane = lane; car.Pos = pos;
            car.Next = lane.Outs.Count > 0 ? lane.Outs[tr.Rng.Next(lane.Outs.Count)] : -1;
            if (!tr.LaneCars.TryGetValue(lane.Id, out cs)) tr.LaneCars[lane.Id] = cs = new List<Car>();
            int i = 0;
            while (i < cs.Count && cs[i].Pos <= pos) i++;
            cs.Insert(i, car);
        }

        static float? Obstacle(Traffic tr, Car car, float look)
        {
            float? best = null;
            Lane lane = car.Lane;
            float pos = car.Pos, bas = 0;
            for (int depth = 0; depth < 2; depth++)
            {
                if (lane == null || bas > look) break;
                foreach (var s in lane.Signals)
                {
                    char light = s.Pos > pos ? tr.City.Light(s.Sig, tr.T) : 'G';
                    if (light != 'G')
                    {
                        float dist = bas + (s.Pos - pos) * Unit - SignalGap;
                        float brake = light == 'A' ? Comfort * lane.MaxSpeed / 10 : 0.5f * lane.MaxSpeed;
                        if (dist >= car.V * car.V / (2 * brake) - 1) best = best == null ? dist : Math.Min(best.Value, dist);
                        break;
                    }
                }
                List<Car> cs;
                if (tr.LaneCars.TryGetValue(lane.Id, out cs))
                    foreach (var o in cs)
                        if (o != car && o.Pos > pos)
                        {
                            float dist = bas + (o.Pos - pos) * Unit - StopGap;
                            best = best == null ? dist : Math.Min(best.Value, dist);
                            break;
                        }
                bas += (lane.End - pos) * Unit;
                Lane next = null;
                if (depth == 0 && car.Next >= 0) tr.City.Lanes.TryGetValue(car.Next, out next);
                lane = next;
                pos = -1;
            }
            return best;
        }

        static void Plan(Traffic tr, Car car, out float a, out bool stop)
        {
            float top = car.Lane.MaxSpeed, inc = top / 10;
            float look = car.V * car.V / (2 * inc) + top * (float) Period + 30;
            float? d = Obstacle(tr, car, look);
            float v = car.V;
            stop = false;
            if (d == null) { a = inc; return; }
            if (v <= 0.01f)
            {
                if (d.Value > inc) { a = inc; return; }
                a = 0; stop = true; return;
            }
            float v1 = Math.Min(v + inc, top);
            if (v1 * v1 / (2 * Comfort * inc) <= d.Value - (v + v1) / 2) { a = inc; return; }
            float need = v * v / (2 * Math.Max(d.Value, 0.5f));
            a = -Math.Min(Math.Max(need, inc / 2), Math.Min(v, top / 2));
        }

        static void Replan(Traffic tr)
        {
            var plans = new List<Tuple<Car, float, bool>>();
            foreach (var car in tr.Cars) { float a; bool s; Plan(tr, car, out a, out s); plans.Add(Tuple.Create(car, a, s)); }
            foreach (var p in plans)
            {
                if (p.Item3) { p.Item1.V = 0; p.Item1.A = Stopped; p.Item1.Stopped = true; }
                else { p.Item1.A = p.Item2; p.Item1.Stopped = false; }
            }
            foreach (var car in tr.Cars) { float x, y; car.Lane.At(car.Pos, out x, out y); car.X = x; car.Y = y; }
        }

        static void Advance(Traffic tr, Car car, float units)
        {
            float pos = car.Pos + units;
            int guard = 0;
            while (pos >= car.Lane.End && guard++ < 20)
            {
                Lane next = null;
                if (car.Next >= 0) tr.City.Lanes.TryGetValue(car.Next, out next);
                if (next == null) { Place(tr, car, 0); return; }
                pos -= car.Lane.End;
                MoveTo(tr, car, next, pos);
            }
            car.Pos = pos;
        }

        static void Step(Traffic tr)
        {
            tr.T += Period;
            foreach (var car in tr.Cars)
            {
                if (car.Stopped) continue;
                float v0 = car.V;
                float v1 = Math.Min(Math.Max(v0 + car.A * (float) Period, 0f), car.Lane.MaxSpeed);
                Advance(tr, car, (v0 + v1) / 2 * (float) Period / Unit);
                car.V = v1;
            }
            foreach (var cs in tr.LaneCars.Values) cs.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            Replan(tr);
        }

        // ------------------------------------------------------------------ envio

        static ushort Half(float f)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            int sign = (bits >> 16) & 0x8000;
            int exp = ((bits >> 23) & 0xff) - 127 + 15;
            int mant = bits & 0x7fffff;
            if (exp <= 0) return (ushort) sign;
            if (exp >= 31) return (ushort) (sign | 0x7bff);
            return (ushort) (sign | (exp << 10) | (mant >> 13));
        }

        static void SendNear(Traffic tr, Player p, float x, float y)
        {
            var client = p.AreaClient;
            if (client == null) return;
            float range = RiceServer.Config.CityTrafficRange;
            float r2 = range * range;
            int now = Environment.TickCount;
            var nowDt = DateTime.Now;
            foreach (var car in tr.Cars)
            {
                if (car.FreeUntil > nowDt) continue;          // batido: o jogo cuida dele
                float dx = car.X - x, dy = car.Y - y;
                if (dx * dx + dy * dy > r2) continue;
                int next = car.Next >= 0 && (car.Lane.End - car.Pos) * Unit <= Announce ? car.Next : -1;
                var pk = new RicePacket(701);
                pk.Writer.Write(now);
                pk.Writer.Write(car.Pos);
                pk.Writer.Write(Half(car.V));
                pk.Writer.Write(Half(car.A));
                pk.Writer.Write((ushort) car.Id);
                pk.Writer.Write((ushort) car.Model);
                pk.Writer.Write((short) car.Lane.Id);
                pk.Writer.Write((short) next);
                pk.Writer.Write((short) -1);
                client.Send(pk);
            }
            foreach (var l in tr.City.Lamps)
            {
                float dx = l[2] - x, dy = l[3] - y;
                if (dx * dx + dy * dy > r2) continue;
                double since;
                bool go = tr.City.State((int) l[1], tr.T, out since);
                int changed = since * 1000 < now ? now - (int) (since * 1000) : 0;
                var pk = new RicePacket(706);
                pk.Writer.Write(changed);
                pk.Writer.Write((int) l[0]);
                pk.Writer.Write(go ? 1 : 0);
                client.Send(pk);
            }
        }

        static int hitLogs;

        // Um carro do trafego foi batido (0x220 / 0x223): fica livre por CityTrafficHitSeconds e e recolocado numa
        // faixa longe do jogador (aparece de novo la, fora da sua vista).
        public static void Hit(Character ch, int carId)
        {
            var cfg = RiceServer.Config;
            if (ch == null || cfg.CityTrafficHitSeconds <= 0) return;
            lock (Sync)
            {
                Traffic tr;
                if (!running.TryGetValue(ch.City, out tr) || carId < 0 || carId >= tr.Cars.Count) return;
                var car = tr.Cars[carId];
                bool already = car.FreeUntil > DateTime.Now;
                car.FreeUntil = DateTime.Now.AddSeconds(cfg.CityTrafficHitSeconds);
                if (already) return;
                float far = Math.Max(cfg.CityTrafficRange + 80f, 400f);
                for (int k = 0; k < 12; k++)
                {
                    Place(tr, car, (float) tr.Rng.NextDouble());
                    float x, y;
                    car.Lane.At(car.Pos, out x, out y);
                    car.X = x; car.Y = y;
                    float dx = x - ch.LastX, dy = y - ch.LastY;
                    if (dx * dx + dy * dy >= far * far) break;
                }
                if (hitLogs++ < 5) Log.WriteLine("Trafego: carro {0} batido, livre por {1} s.", carId, cfg.CityTrafficHitSeconds);
            }
        }

        static void Tick()
        {
            try
            {
                lock (Sync)
                {
                    var cfg = RiceServer.Config;
                    // jogadores na cidade (fora de salas da Battle Zone e treinos)
                    var seen = new Dictionary<int, List<Player>>();
                    foreach (var area in RiceServer.GetAreas())
                    {
                        if (area.ID >= 900) continue;
                        if (Rice.Server.Packets.Game.NeoCity.IsNeoArea(area.ID)) continue;   // Neo City: sem trafego
                        foreach (var p in area.GetPlayers())
                        {
                            var ch = p.ActiveCharacter;
                            if (ch == null || !ch.HasPosition) continue;
                            if (Rice.Server.Packets.Game.UnderCity.InRun(ch)) continue;   // sem trafego na Undercity
                            if (ch.InNeoBase) continue;                                    // base ROAD da Neo City: sem trafego
                            DateTime lc;
                            if (area.ID != 0 && (!cityCheck.TryGetValue(ch.CID, out lc) || (DateTime.Now - lc).TotalSeconds > 10))
                            {
                                cityCheck[ch.CID] = DateTime.Now;
                                int dc = DetectCity(ch.City, ch.LastX, ch.LastY);
                                if (dc != ch.City && dc > 0)
                                {
                                    Log.WriteLine("Trafego: {0} esta na cidade {1} (pela posicao; antes {2}).", ch.Name, dc, ch.City);
                                    ch.City = dc;
                                }
                            }
                            List<Player> list;
                            if (!seen.TryGetValue(ch.City, out list)) seen[ch.City] = list = new List<Player>();
                            list.Add(p);
                        }
                    }
                    foreach (var kv in seen)
                    {
                        Traffic tr;
                        if (!running.TryGetValue(kv.Key, out tr))
                        {
                            var city = Load(kv.Key);
                            if (city == null || city.Spawn.Count == 0) continue;
                            tr = running[kv.Key] = NewTraffic(city, Math.Max(0, cfg.CityTrafficCars));
                            Log.WriteLine("Trafego {0}: {1} carros.", city.Name, tr.Cars.Count);
                        }
                        Step(tr);
                        tr.LastSeen = DateTime.Now;
                        foreach (var p in kv.Value)
                        {
                            try { SendNear(tr, p, p.ActiveCharacter.LastX, p.ActiveCharacter.LastY); }
                            catch (Exception ex) { Log.WriteError("Trafego envio: " + ex.Message); }
                        }
                    }
                    foreach (var k in running.Keys.ToList())
                        if ((DateTime.Now - running[k].LastSeen).TotalSeconds > 120) running.Remove(k);
                }
            }
            catch (Exception ex)
            {
                // onde foi (1a linha da pilha), para achar o "estouro" que aparece ao trocar de corredor
                string at = ex.StackTrace != null ? ex.StackTrace.Split('\n')[0].Trim() : "";
                Log.WriteError("Trafego: " + ex.Message + " " + at);
            }
        }
    }
}
