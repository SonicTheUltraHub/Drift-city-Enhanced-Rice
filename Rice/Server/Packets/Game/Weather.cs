using System;
using System.Linq;
using System.Threading;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Clima (achado no DriftCity.exe 2016): 640 Cmd_Weather (i32 1..4) troca o ceu, a luz, a neblina e a chuva
    // com transicao de ~10 s. Ceus do cliente: skydom_fine_cloud (limpo), skydom_sunset_cloud (fim de tarde),
    // skydom_rain (chuva) e skydom_fog (neblina); a ordem dos numeros se descobre com !!clima 1..4.
    // Sem esse pacote o cliente fica sem clima definido (o ceu da Battle Zone ficava preto).
    public static class Weather
    {
        static Timer timer;
        static int sent = -1;
        static int manual;                       // !!clima N (0 = automatico)

        public static void Start()
        {
            if (timer != null) return;
            timer = new Timer(_ =>
            {
                try
                {
                    int w = Current();
                    if (w != sent) { sent = w; Broadcast(w); }
                }
                catch (Exception ex) { Log.WriteError("Clima: " + ex.Message); }
            }, null, 5000, 15000);
        }

        // Ciclo: WeatherOrder (1 limpo -> 4 fim de tarde -> 2 nublado) com WeatherMinutes cada. Em cada fase ha
        // WeatherRainChance de chover (3) por WeatherRainMinutes, comecando num momento aleatorio no meio da fase.
        static int phase = -1;
        static DateTime phaseEnd = DateTime.MinValue, rainStart = DateTime.MaxValue, rainEnd = DateTime.MinValue;
        static readonly Random rng = new Random();

        public static int Current()
        {
            var cfg = RiceServer.Config;
            if (manual >= 1 && manual <= 4) return manual;
            var order = cfg.WeatherOrder ?? new int[0];
            var mins = cfg.WeatherMinutes ?? new int[0];
            int n = Math.Min(order.Length, mins.Length);
            if (!cfg.WeatherCycle || n == 0) return Math.Max(1, Math.Min(4, cfg.WeatherDefault));
            lock (rng)
            {
                var now = DateTime.UtcNow;
                if (now >= phaseEnd)
                {
                    phase = (phase + 1) % n;
                    double len = Math.Max(1, mins[phase]);
                    phaseEnd = now.AddMinutes(len);
                    if (rng.NextDouble() < cfg.WeatherRainChance)
                    {
                        double rain = Math.Min(len, Math.Max(1, cfg.WeatherRainMinutes));
                        rainStart = now.AddMinutes(rng.NextDouble() * (len - rain));
                        rainEnd = rainStart.AddMinutes(rain);
                    }
                    else { rainStart = DateTime.MaxValue; rainEnd = DateTime.MinValue; }
                }
                if (now >= rainStart && now < rainEnd) return 3;
                return Math.Max(1, Math.Min(4, order[phase]));
            }
        }

        public static void SendTo(RiceClient c)
        {
            if (c == null || !RiceServer.Config.WeatherEnabled) return;
            var p = new RicePacket(640);
            p.Writer.Write(Current());
            c.Send(p);
        }

        static void Broadcast(int w)
        {
            if (!RiceServer.Config.WeatherEnabled) return;
            foreach (var c in RiceServer.Game.GetClients())
            {
                if (c.Player == null || c.Player.ActiveCharacter == null) continue;
                try { SendTo(c); } catch (Exception) { }
            }
            Log.WriteLine("Clima: {0}.", w);
        }

        public static string Command(string arg)
        {
            int n;
            if (!int.TryParse((arg ?? "").Trim(), out n) || n < 0 || n > 4)
                return "Uso: !!clima 1 a 4 (testar cada ceu) ou !!clima 0 (ciclo automatico). Agora: " + Current() + ".";
            manual = n;
            sent = Current();
            Broadcast(sent);
            return n == 0 ? "Clima automatico (ciclo). Agora: " + sent + "." : "Clima fixo em " + n + ". Use !!clima 0 para voltar ao ciclo.";
        }
    }
}
