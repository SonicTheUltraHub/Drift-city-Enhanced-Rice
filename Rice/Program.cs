using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server;
using Rice.Server.Core;

namespace Rice
{
    class Program
    {
        static readonly object CrashSync = new object();
        public static void CrashLog(string what, Exception ex)
        {
            try
            {
                string text = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}: {2}{3}{3}", DateTime.Now, what, ex, Environment.NewLine);
                lock (CrashSync) System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "erros.log"), text);
                Log.WriteError("{0} (gravado no erros.log): {1}", what, ex != null ? ex.Message : "?");
            }
            catch (Exception) { }
        }

        // CloseGameWithServer: o servidor fechou ("sair", o X da janela ou Ctrl+C) -> o jogo fecha CloseGameDelaySeconds depois
        // (sem o servidor o jogo fica parado). Fechar o servidor pelo Gerenciador de Tarefas nao avisa ninguem.
        delegate bool ConsoleCtrlHandler(int ctrlType);
        [System.Runtime.InteropServices.DllImport("Kernel32")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);
        static ConsoleCtrlHandler ctrlHandler;              // guardado: o coletor de lixo nao pode levar
        static int gameClosed;
        static void CloseGame(Config cfg)
        {
            if (cfg == null || !cfg.CloseGameWithServer || System.Threading.Interlocked.Exchange(ref gameClosed, 1) == 1) return;
            try
            {
                var games = System.Diagnostics.Process.GetProcessesByName("DriftCity");
                if (games.Length == 0) return;
                Log.WriteLine("O servidor fechou: o jogo fecha em {0} s.", cfg.CloseGameDelaySeconds);
                System.Threading.Thread.Sleep(Math.Max(0, Math.Min(4, cfg.CloseGameDelaySeconds)) * 1000);
                foreach (var g in games) try { if (!g.HasExited) g.Kill(); } catch (Exception) { }
            }
            catch (Exception) { }
        }

        // CloseWithGame: depois que o jogo (DriftCity.exe) abriu, se ele ficar fechado por CloseWithGameSeconds o servidor
        // salva os bots e fecha sozinho (o resto ja e gravado na hora). Sem o jogo ter aberto, o servidor fica aberto.
        static void WatchGame(Config cfg)
        {
            bool seen = false;
            DateTime gone = DateTime.MinValue;
            while (true)
            {
                System.Threading.Thread.Sleep(3000);
                bool running;
                try { running = System.Diagnostics.Process.GetProcessesByName("DriftCity").Length > 0; } catch (Exception) { continue; }
                if (running)
                {
                    if (!seen) Log.WriteLine("O jogo abriu: o servidor fecha sozinho quando o jogo fechar (CloseWithGame).");
                    seen = true; gone = DateTime.MinValue; continue;
                }
                if (!seen) continue;
                if (gone == DateTime.MinValue) { gone = DateTime.UtcNow; continue; }
                if ((DateTime.UtcNow - gone).TotalSeconds < Math.Max(3, cfg.CloseWithGameSeconds)) continue;
                Log.WriteLine("O jogo fechou: salvando e fechando o servidor.");
                System.Threading.Interlocked.Exchange(ref gameClosed, 1);
                try { Rice.Server.Packets.Game.Bots.Save(); } catch (Exception) { }
                try { Rice.Server.Packets.Game.Battle.SyncClientAiTable(true); } catch (Exception) { }   // corredores novos: certo na proxima vez
                System.Threading.Thread.Sleep(1000);
                Environment.Exit(0);
            }
        }

        static void Main(string[] args)
        {
            Console.Title = "Rice";
            Console.Clear();
            // erro que escapou: grava no erros.log (pasta do Rice.exe) e o servidor continua (App.config: legacyUnhandledExceptionPolicy)
            AppDomain.CurrentDomain.UnhandledException += (s, e) => CrashLog("Erro sem tratamento", e.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { CrashLog("Erro numa tarefa", e.Exception); e.SetObserved(); };

            Paths.Migrate();
            Config config = Config.Load();
            Lang.Language = config.Language;

            // Rice.exe menu  -> menu para mexer nos saves, bots e configuracoes (com o servidor fechado)
            if (args.Length > 0 && args[0].Equals("menu", StringComparison.OrdinalIgnoreCase))
            {
                Database.Initialize(config);
                Database.Start();
                CharFiles.Migrate();
                Menu.Run(config);
                return;
            }

            // Rice.exe ia  -> (Start.bat, antes de abrir o jogo) grava o nome e o nivel dos seus corredores nas linhas
            // deles da tabela de IA do Init_us.agt (o jogo mostra esse nome na corrida)
            if (args.Length > 0 && args[0].Equals("ia", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Database.Initialize(config);
                    RiceServer.Config = config;
                    Database.Start();
                    CharFiles.Migrate();
                    Rice.Server.Packets.Game.Battle.SyncClientAiTable(false);
                }
                catch (Exception ex) { Console.WriteLine("Tabela de IA: " + ex.Message); }
                return;
            }

            Database.Initialize(config);
            Resources.Initialize(config);
            RiceServer.Initialize(config);
            Admin.Initialize(config);

            Database.Start();
            CharFiles.Migrate();
            RiceServer.Start();
            // o X da janela do servidor, Ctrl+C, sair do Windows: fecha o jogo junto
            ctrlHandler = t => { if (t == 0 || t == 1 || t == 2 || t == 5 || t == 6) CloseGame(config); return false; };
            try { SetConsoleCtrlHandler(ctrlHandler, true); } catch (Exception) { }
            if (config.CloseWithGame)
                new System.Threading.Thread(() => WatchGame(config)) { IsBackground = true, Name = "fecha com o jogo" }.Start();
            // o servidor fica aberto ate digitar "sair" (antes um Enter sem querer na janela fechava o servidor)
            while (true)
            {
                string line = Console.ReadLine();
                if (line == null) { System.Threading.Thread.Sleep(1000); continue; }
                line = line.Trim().ToLowerInvariant();
                if (line == "sair" || line == "exit" || line == "quit") break;
                if (line.Length > 0) Log.WriteLine("Digite \"sair\" para fechar o servidor.");
            }
            CloseGame(config);
        }
    }
}
