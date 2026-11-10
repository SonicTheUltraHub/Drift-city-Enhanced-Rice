using System;
using System.Collections.Generic;
using Rice.Server.Core;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server.Database;
using Models = Rice.Server.Database.Models;

namespace Rice.Game
{
    public enum UserStatus : byte
    {
        Invalid = 0,
        Normal = 1,
        Banned = 2,
        Muted = 3
    }

    public class User
    {
        public ulong UID;
        public string Name;
        public string PasswordHash;
        public UserStatus Status;
        public string CreateIP;
        public uint Credits;

        private User(Models.User dbUser = null)
        {
            if (dbUser != null)
            {
                this.UID = (ulong)dbUser.ID;
                this.Name = dbUser.Name;
                this.PasswordHash = dbUser.PasswordHash;
                this.Status = (UserStatus)dbUser.Status;
                this.CreateIP = dbUser.CreateIP;
                this.Credits = (uint)dbUser.Credits;
            }
        }

        public static User Empty => new User { Status = UserStatus.Normal };

        public static User Retrieve(string username, string passwordHash)
        {
            Log.WriteLine("Username: {0}, PasswordHash: {1}", username, passwordHash);

            User user;
            
            using (var rc = Database.GetContext())
                user = new User(rc.Users.FirstOrDefault(u => u.Name == username && u.PasswordHash == passwordHash));

            return user;
        }

        // Modo offline: conta normal ou admin (pelo nome digitado no login ou AdminLogin)
        public static User RetrieveOffline(string typed, string pwhash = null)
        {
            var cfg = RiceServer.Config;
            string t = (typed ?? "").Trim();
            if (t.Length > 32) t = t.Substring(0, 32);
            bool admin = cfg.AdminLogin || (t.Length > 0 && !string.IsNullOrEmpty(cfg.AdminAccount)
                                             && string.Equals(t, cfg.AdminAccount, StringComparison.OrdinalIgnoreCase));
            string name = admin ? cfg.AdminAccount : (t.Length > 0 && cfg.AutoCreateAccounts ? t : cfg.NormalAccount);
            // nome novo: cria a conta na hora (vazia, como um jogador que acabou de se cadastrar)
            if (!admin && !string.IsNullOrEmpty(name))
            {
                using (var rc = Database.GetContext())
                {
                    string low = name.ToLower();
                    var ex = rc.Users.FirstOrDefault(x => x.Name.ToLower() == low);
                    if (ex != null) name = ex.Name;
                    else
                    {
                        rc.Users.Add(new Models.User { Name = name, PasswordHash = pwhash ?? Utilities.MD5(name), CreateIP = "127.0.0.1", Status = 1, Credits = 1000 });
                        rc.SaveChanges();
                        Log.WriteLine("Conta nova '{0}' criada (sem corredores).", name);
                    }
                }
            }
            if (!string.IsNullOrEmpty(name))
            {
                User u;
                using (var rc = Database.GetContext())
                {
                    var db = rc.Users.FirstOrDefault(x => x.Name == name);
                    u = db != null ? new User(db) : null;
                }
                if (u != null) { Log.WriteLine("Login offline na conta {0} ({1}).", name, admin ? "admin" : "normal"); return u; }
            }
            return RetrieveFirst();
        }

        // Modo offline: devolve a primeira (unica) conta do banco.
        public static User RetrieveFirst()
        {
            User user;

            using (var rc = Database.GetContext())
                user = new User(rc.Users.OrderBy(u => u.ID).FirstOrDefault());

            return user;
        }

        public static User Retrieve(string username)
        {
            User user;

            using (var rc = Database.GetContext())
                user = new User(rc.Users.FirstOrDefault(u => u.Name == username));

            return user;
        }

        public static User Retrieve(ulong uid)
        {
            User user;

            using (var rc = Database.GetContext())
                user = new User(rc.Users.Find((long)uid));

            return user;
        }
    }
}
