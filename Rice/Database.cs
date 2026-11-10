using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Data.SqlServerCe;
using System.Linq;
using Rice.Server.Database.Models;

namespace Rice
{
    public static class DatabaseExtensions
    {
        public static DbCommand CreateTextCommand(this DbConnection dbconn, string query)
        {
            DbCommand command = dbconn.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = query;
            return command;
        }

        public static void AddParameter(this DbCommand command, string name, object value)
        {
            DbParameter param = command.CreateParameter();
            param.ParameterName = name;
            param.Value = value;
            command.Parameters.Add(param);
        }
    }
    
    public class RiceContext : DbContext
    {
        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<Character> Characters { get; set; }
        public virtual DbSet<QuestState> QuestStates { get; set; }
        public virtual DbSet<Item> Items { get; set; }
        public virtual DbSet<Vehicle> Vehicles { get; set; }

        public RiceContext(DbConnection connection, bool ownsConnection = false) : base(connection, ownsConnection) { }
    }

    public static class Database
    {
        static DbConnection conn;
        static Config cfg;

        public static void Initialize(Config config)
        {
            cfg = config;
            conn = new SqlCeConnection("Data Source=" + Paths.Db);
        }

        public static void Start()
        {
            Log.WriteLine("Connecting to database.");

            try
            {
                using (var rc = new RiceContext(conn))
                {
                    if (rc.Database.CreateIfNotExists())
                    {
                        Log.WriteLine("Database did not exist, created.");
                        
                        // Conta offline unica (login: admin / senha: admin), SEM corredores.
                        // Os corredores sao criados no jogo, comecando do zero.
                        var user = new User
                        {
                            Name = "admin",
                            PasswordHash = Utilities.MD5("admin"),
                            CreateIP = "127.0.0.1",
                            Status = 1,
                            Credits = 1000
                        };

                        rc.Users.Add(user);
                        rc.SaveChanges();
                    }

                    // A conta admin sempre existe (com o perfil upado).
                    EnsureAdminAccount(rc);
                    // Garante que o perfil upado exista (tambem em bancos antigos).
                    EnsureUppedProfile(rc);
                    // Conta normal (sem o perfil upado); na primeira vez leva os seus corredores da conta admin.
                    EnsureNormalAccount(rc);
                }
                conn.Open();
                Log.WriteLine("Connected to database.");
            }
            catch (SqlCeException ex)
            {
                Log.WriteError($"Connection failed: {ex.Message}");
            }
        }

        static User AdminUser(RiceContext rc)
        {
            string an = string.IsNullOrEmpty(cfg.AdminAccount) ? "admin" : cfg.AdminAccount;
            return rc.Users.FirstOrDefault(u => u.Name == an) ?? rc.Users.OrderBy(u => u.ID).FirstOrDefault();
        }

        private static void EnsureAdminAccount(RiceContext rc)
        {
            string an = string.IsNullOrEmpty(cfg.AdminAccount) ? "admin" : cfg.AdminAccount;
            if (rc.Users.Any(u => u.Name == an)) return;
            // apagou a conta admin (pelo menu): ela volta do padrao (o perfil upado e criado logo depois)
            rc.Users.Add(new User { Name = an, PasswordHash = Utilities.MD5(an), CreateIP = "127.0.0.1", Status = 1, Credits = 1000 });
            rc.SaveChanges();
            Log.WriteLine("Conta admin '" + an + "' criada.");
        }

        private static void EnsureNormalAccount(RiceContext rc)
        {
            string nn = cfg.NormalAccount;
            if (string.IsNullOrEmpty(nn) || rc.Users.Any(u => u.Name == nn)) return;
            var admin = AdminUser(rc);
            if (admin == null || admin.Name == nn) return;
            // so cria a conta normal para levar corredores antigos que estavam na admin (se voce apagou ela pelo
            // menu, nao volta sozinha)
            long aid0 = admin.ID;
            string upped0 = cfg.UppedProfileName ?? "";
            if (!rc.Characters.Any(c => c.UID == aid0 && c.Name != upped0)) return;
            var user = new User { Name = nn, PasswordHash = Utilities.MD5(nn), CreateIP = "127.0.0.1", Status = 1, Credits = admin.Credits };
            rc.Users.Add(user);
            rc.SaveChanges();
            // os corredores que voce ja tinha (menos o upado) passam para a conta normal
            long aid = admin.ID;
            string upped = cfg.UppedProfileName ?? "";
            int moved = 0;
            foreach (var c in rc.Characters.Where(c => c.UID == aid).ToList())
            {
                if (string.Equals(c.Name, upped, StringComparison.OrdinalIgnoreCase)) continue;
                c.UID = user.ID; moved++;
            }
            rc.SaveChanges();
            Log.WriteLine("Conta normal '" + nn + "' criada; " + moved + " corredor(es) da conta admin passaram para ela.");
        }

        private static void EnsureUppedProfile(RiceContext rc)
        {
            string name = cfg.UppedProfileName;
            if (string.IsNullOrEmpty(name))
                return;

            if (rc.Characters.Any(c => c.Name == name))
                return;

            var owner = AdminUser(rc);
            if (owner == null)
                return;

            long ownerId = owner.ID;
            if (rc.Characters.Count(c => c.UID == ownerId) >= cfg.MaxPlayerProfiles)
            {
                Log.WriteError("Sem slot livre para criar o perfil '" + name + "'. Apague o db.sdf para recomecar.");
                return;
            }

            var upped = new Character
            {
                UID = ownerId,
                Name = name,
                Mito = 12345678910,
                Avatar = 2,
                Level = 123,
                Experience = 123,
                City = 1,
                TID = -1
            };

            var car = new Vehicle
            {
                CarID = 1,
                AuctionCount = 0,
                CarType = 88,
                Color = 0,
                Grade = 9,
                Kms = 200,
                Mitron = 5550f
            };

            upped.Vehicles = new List<Vehicle> {car};
            upped.CurrentCarID = car.CarID;

            rc.Characters.Add(upped);
            rc.SaveChanges();
            Log.WriteLine("Perfil upado '" + name + "' criado.");
        }

        private static DbConnection GetConnection()
        {
            if (conn.State == ConnectionState.Broken)
                conn.Close();
            if (conn.State == ConnectionState.Closed)
                Start();
            return conn;
        }

        public static RiceContext GetContext() => new RiceContext(GetConnection());
    }
}
