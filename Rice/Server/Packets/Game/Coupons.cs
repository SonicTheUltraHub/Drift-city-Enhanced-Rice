using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Livro de cupons (carros de cupom: Panda, Tokio, Eric...).
    // O cliente conta sozinho batalhas, entregas, perseguicoes, combos, tempo dirigindo, drifts, manobras, pulos,
    // contramao e Undercity (XiStrCouponForVar, 40 bytes) e manda 1010 quando muda; o servidor guarda e devolve
    // no 1011 -> 1012 ao entrar. Quando a meta chega o cliente marca o cupom (1015), abre (1017) e pega (1019):
    // o servidor entrega o item do cupom (i_n_c0001...) e a loja vende o carro gastando o cupom.
    // Contadores do servidor (1021, u32 tipo + f32 valor): 14 = tentativas de Rush Time (FondoX).
    // Tudo fica em coupons.json (ao lado do Rice.exe).
    public static class Coupons
    {
        public class Coupon { public int type, index, count, minutes, car, dbtype, dbidx; public string name, item; }

        class Data
        {
            public string Vars;                                                   // 40 bytes em base64
            public Dictionary<string, int> Stamps = new Dictionary<string, int>(); // "tipo:indice" -> 1 marcado, 2 aberto, 3 pego
            public Dictionary<int, float> Params = new Dictionary<int, float>();
        }

        public const int RushTry = 14;
        const int Marked = 1, Opened = 2, Taken = 3;
        const string FilePath = "coupons.json";
        static readonly object Sync = new object();
        static Dictionary<string, Data> all;
        static List<Coupon> table;

        public static List<Coupon> Table()
        {
            if (table != null) return table;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "Coupons.json");
                string json = null;
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Coupons.json"))
                        if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                table = json != null ? JsonConvert.DeserializeObject<List<Coupon>>(json) : new List<Coupon>();
            }
            catch (Exception ex) { Log.WriteError("Coupons.json: " + ex.Message); table = new List<Coupon>(); }
            return table;
        }

        // item de cupom do carro (null = nao e carro de cupom)
        public static string CouponItemFor(int carType)
        {
            var c = Table().FirstOrDefault(x => x.car == carType && !string.IsNullOrEmpty(x.item));
            return c != null ? c.item : null;
        }

        static Data Of(ulong cid)
        {
            if (all == null)
            {
                all = new Dictionary<string, Data>();
                try
                {
                    if (CharFiles.Exists(FilePath))
                        all = JsonConvert.DeserializeObject<Dictionary<string, Data>>(CharFiles.ReadAllText(FilePath)) ?? new Dictionary<string, Data>();
                }
                catch (Exception ex) { Log.WriteError("coupons.json invalido: " + ex.Message); }
            }
            Data d;
            if (!all.TryGetValue(cid.ToString(), out d)) { d = new Data(); all[cid.ToString()] = d; }
            if (d.Stamps == null) d.Stamps = new Dictionary<string, int>();
            if (d.Params == null) d.Params = new Dictionary<int, float>();
            return d;
        }

        static void Save()
        {
            try { CharFiles.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented)); }
            catch (Exception ex) { Log.WriteError("coupons.json: " + ex.Message); }
        }

        static Rice.Game.Character Ch(RicePacket p)
        {
            return p.Sender != null && p.Sender.Player != null ? p.Sender.Player.ActiveCharacter : null;
        }

        static void SendParam(RiceClient client, int ctype, float value)
        {
            var pk = new RicePacket(1021);
            pk.Writer.Write((uint) ctype);
            pk.Writer.Write(value);
            client.Send(pk);
        }

        // 1010 UpdateCouponForVar: os contadores novos do cliente
        [RicePacket(1010, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void UpdateVars(RicePacket packet)
        {
            var c = Ch(packet);
            if (c == null || !RiceServer.Config.CouponsEnabled) return;
            byte[] v = packet.Reader.ReadBytes(40);
            if (v.Length < 40) return;
            lock (Sync) { Of(c.CID).Vars = Convert.ToBase64String(v); Save(); }
        }

        // 1011 GetCouponForVar -> 1012 (os 40 bytes guardados) + 1021 de cada contador do servidor
        [RicePacket(1011, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void GetVars(RicePacket packet)
        {
            var c = Ch(packet);
            if (c == null || !RiceServer.Config.CouponsEnabled) return;
            byte[] v = null;
            Dictionary<int, float> pars;
            lock (Sync)
            {
                var d = Of(c.CID);
                try { if (!string.IsNullOrEmpty(d.Vars)) v = Convert.FromBase64String(d.Vars); } catch (Exception) { }
                pars = new Dictionary<int, float>(d.Params);
            }
            if (v == null || v.Length != 40) v = new byte[40];
            var pk = new RicePacket(1012);
            pk.Writer.Write(v);
            packet.Sender.Send(pk);
            foreach (var kv in pars.OrderBy(k => k.Key))
                SendParam(packet.Sender, kv.Key, kv.Value);
        }

        // 1013 GetCouponStampList -> 1014: u32 n + n x (i32 tipo, u32 indice, u32 estado)
        [RicePacket(1013, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void StampList(RicePacket packet)
        {
            var c = Ch(packet);
            if (c == null) return;
            var list = new List<int[]>();
            if (RiceServer.Config.CouponsEnabled)
                lock (Sync)
                    foreach (var kv in Of(c.CID).Stamps)
                    {
                        var parts = kv.Key.Split(':');
                        int t, i;
                        if (parts.Length == 2 && int.TryParse(parts[0], out t) && int.TryParse(parts[1], out i))
                            list.Add(new[] { t, i, kv.Value });
                    }
            var pk = new RicePacket(1014);
            pk.Writer.Write((uint) list.Count);
            foreach (var e in list.OrderBy(x => x[0]).ThenBy(x => x[1]))
            {
                pk.Writer.Write(e[0]);
                pk.Writer.Write((uint) e[1]);
                pk.Writer.Write((uint) e[2]);
            }
            packet.Sender.Send(pk);
        }

        static Coupon Read(RicePacket packet, out uint type, out int index)
        {
            type = packet.Reader.ReadUInt32();
            index = packet.Reader.ReadInt32();
            uint t = type; int i = index;
            return Table().FirstOrDefault(x => x.type == t && x.index == i);
        }

        static void Echo(RicePacket packet, ushort id, uint type, int index)
        {
            var pk = new RicePacket(id);
            pk.Writer.Write(type);
            pk.Writer.Write(index);
            packet.Sender.Send(pk);
        }

        // 1015 MarkCoupon -> 1016: a meta chegou (o cliente decide; os contadores do servidor sao conferidos aqui)
        [RicePacket(1015, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Mark(RicePacket packet)
        {
            var c = Ch(packet);
            uint type; int index;
            var cp = Read(packet, out type, out index);
            if (c == null || cp == null || !RiceServer.Config.CouponsEnabled) return;
            string key = type + ":" + index;
            lock (Sync)
            {
                var d = Of(c.CID);
                if (d.Stamps.ContainsKey(key)) return;
                if (cp.dbtype == RushTry || cp.dbtype == 11 || cp.dbtype == 15)
                {
                    float have; d.Params.TryGetValue(cp.dbtype, out have);
                    if (have < cp.count) { Log.WriteLine("Cupom {0}: ainda nao ({1}/{2}).", cp.name, have, cp.count); return; }
                }
                d.Stamps[key] = Marked;
                Save();
            }
            Log.WriteLine("Cupom {0} ({1}) marcado para {2}.", cp.name, cp.item, c.Name);
            Echo(packet, 1016, type, index);
        }

        // 1017 OpenCoupon -> 1018
        [RicePacket(1017, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Open(RicePacket packet)
        {
            var c = Ch(packet);
            uint type; int index;
            var cp = Read(packet, out type, out index);
            if (c == null || cp == null) return;
            string key = type + ":" + index;
            lock (Sync)
            {
                var d = Of(c.CID);
                int st;
                if (!d.Stamps.TryGetValue(key, out st) || st != Marked) return;
                d.Stamps[key] = Opened;
                Save();
            }
            Echo(packet, 1018, type, index);
        }

        // 1019 GetCoupon -> 1020 + o item do cupom
        [RicePacket(1019, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Take(RicePacket packet)
        {
            var c = Ch(packet);
            uint type; int index;
            var cp = Read(packet, out type, out index);
            if (c == null || cp == null || string.IsNullOrEmpty(cp.item)) return;
            string key = type + ":" + index;
            lock (Sync)
            {
                int st;
                if (!Of(c.CID).Stamps.TryGetValue(key, out st) || (st != Marked && st != Opened)) return;
            }
            Rice.Game.Item it;
            if (!c.GrantItem(cp.item, 1, out it))
            {
                Log.WriteError("Cupom: falha ao entregar " + cp.item);
                return;
            }
            lock (Sync) { Of(c.CID).Stamps[key] = Taken; Save(); }
            Log.WriteLine("Cupom {0}: {1} pegou {2}.", cp.name, c.Name, cp.item);
            Echo(packet, 1020, type, index);
            c.FlushModInfo(packet.Sender);
            Chat.SendSystemMessage(packet.Sender, "Cupom " + cp.name + " no inventario: compre o carro na loja com ele.");
        }

        // contador do servidor (Rush Time etc.) sobe e vai para o cliente
        public static void AddParam(Rice.Game.Character c, RiceClient client, int ctype, float delta)
        {
            if (c == null || !RiceServer.Config.CouponsEnabled) return;
            float v;
            lock (Sync)
            {
                var d = Of(c.CID);
                float cur; d.Params.TryGetValue(ctype, out cur);
                v = cur + delta;
                d.Params[ctype] = v;
                Save();
            }
            if (client != null) SendParam(client, ctype, v);
        }
    }
}
