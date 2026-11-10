using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Server.Core;
using Rice.Server.Structures;

namespace Rice.Server.Packets.Game
{
    public static class Quest
    {
        [RicePacket(272, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void MyQuestList(RicePacket packet)
        {
            var quests = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID);

            var ack = new RicePacket(273);
            ack.Writer.Write(quests.Count);
            foreach (var quest in quests)
            {
                ack.Writer.Write(quest.QID);
                ack.Writer.Write(quest.State);
                ack.Writer.Write(quest.Progress);
                ack.Writer.Write(quest.FailCount);
            }
            packet.Sender.Send(ack);
        }

        [RicePacket(262, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestStart(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var quest = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID, questId);
            quest = quest ?? Rice.Game.Quest.CreateEntry(packet.Sender.Player.ActiveCharacter.CID, questId);

            packet.Sender.Player.ActiveCharacter.Quest = quest;

            var ack = new RicePacket(263);
            ack.Writer.Write(quest.QID);
            ack.Writer.Write((uint) quest.FailCount);
            packet.Sender.Send(ack);
            // TODO: verify if player is eligible
        }

        [RicePacket(264, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestComplete(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();
            var totalTime = packet.Reader.ReadSingle();

            var quest = packet.Sender.Player.ActiveCharacter.Quest;
            if (quest == null || quest.QID != questId)
            {
                // voltou de um travamento: usa a missao que esta no banco
                quest = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID, questId);
                if (quest == null) { Log.WriteLine("QuestComplete {0}: missao nao encontrada (ignorado).", questId); return; }
                packet.Sender.Player.ActiveCharacter.Quest = quest;
            }

            quest.SetState(1);

            var ack = new RicePacket(265);
            ack.Writer.Write(questId);
            packet.Sender.Send(ack);
            // TODO: verify quest completion
        }

        [RicePacket(266, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestReward(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var quest = packet.Sender.Player.ActiveCharacter.Quest;
            if (quest == null || quest.QID != questId)
            {
                // voltou de um travamento: usa a missao que esta no banco
                quest = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID, questId);
                if (quest == null) { Log.WriteLine("QuestReward {0}: missao nao encontrada (ignorado).", questId); return; }
                packet.Sender.Player.ActiveCharacter.Quest = quest;
            }
            if (quest.State >= 2) { Log.WriteLine("QuestReward {0}: recompensa ja paga (ignorado).", questId); packet.Sender.Player.ActiveCharacter.Quest = null; return; }

            quest.SetState(2);
            var cfg = RiceServer.Config;
            string[] rewards = quest.QuestInfo != null ? quest.QuestInfo.GetRewards() : new string[0];
            packet.Sender.Player.ActiveCharacter.Quest = null;

            if (quest.QuestInfo == null)
                Log.WriteError($"Missao {questId} nao encontrada na tabela; usando recompensa padrao.");

            int expGotten = quest.QuestInfo != null ? quest.QuestInfo.Experience : cfg.UnknownQuestExp;
            int moneyGotten = quest.QuestInfo != null ? quest.QuestInfo.Mito : cfg.UnknownQuestMito;

            // Missoes secundarias (rq_*): o jogo mostra o dobro do que esta na tabela.
            if (quest.QuestInfo != null && quest.QuestInfo.ID != null && quest.QuestInfo.ID.StartsWith("rq_")
                && cfg.SecondaryQuestRewardMultiplier > 1)
            {
                expGotten *= cfg.SecondaryQuestRewardMultiplier;
                moneyGotten *= cfg.SecondaryQuestRewardMultiplier;
            }

            Log.WriteLine("Recompensa da missao {0} ({1}): {2} exp, {3} mito, {4} item(ns).",
                questId, quest.QuestInfo != null ? quest.QuestInfo.ID : "?", expGotten, moneyGotten, rewards.Length);

            packet.Sender.Player.ActiveCharacter.GrantExperience(expGotten, false);          // quest: sem os drinks
            packet.Sender.Player.ActiveCharacter.GrantMito((ulong)moneyGotten, true, false);

            try { Licenses.CountQuest(packet.Sender.Player.ActiveCharacter, quest.QuestInfo != null ? quest.QuestInfo.ID : null); } catch (Exception lex) { Log.WriteError("Licencas (missao): " + lex.Message); }
            var ack = new RicePacket(267);
            ack.Writer.Write(questId);
            ack.Writer.Write(expGotten);
            ack.Writer.Write(moneyGotten);
            ack.Writer.Write(packet.Sender.Player.ActiveCharacter.GetExpInfo());
            ack.Writer.Write(packet.Sender.Player.ActiveCharacter.Level);
            ack.Writer.Write((ushort) rewards.Length); // reward item num
            for (int i = 0; i < 3; i++)
            {
                // jesus christ i need to clean up this horrible hack
                for (; i < rewards.Length; i++)
                {
                    var itemID = rewards[i];
                    int tblIdx = Structures.Resources.ItemTable.Items.FindIndex(itm => itm.ID == itemID);
                    if (tblIdx == -1)
                    {
                        ack.Writer.Write(0);
                        continue;
                    }
                    Rice.Game.Item resultItem;
                    packet.Sender.Player.ActiveCharacter.GrantItem(itemID, 1, out resultItem);
                    ack.Writer.Write((uint) tblIdx);
                }
                if (i < 3)
                    ack.Writer.Write(0);
            }
            packet.Sender.Send(ack);

            // Envia ao inventario os itens de recompensa e atualiza os stats (nivel).
            packet.Sender.Player.ActiveCharacter.FlushModInfo(packet.Sender);
            // Avisos de recompensa de nivel (mito/docks/pecas), se subiu de nivel.
            packet.Sender.Player.ActiveCharacter.FlushNotices(packet.Sender);
            // TODO: use real quest data, update player info
        }

        [RicePacket(268, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestFail(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var quest = packet.Sender.Player.ActiveCharacter.Quest;
            // o jogo travou no meio da missao e voltou: o servidor nao tem a missao ativa. Em vez de derrubar, acha ela no
            // banco, marca a falha e responde para o jogo liberar
            if (quest == null || quest.QID != questId) quest = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID, questId);
            if (quest == null) Log.WriteLine("QuestFail {0}: missao nao estava ativa (liberada).", questId);
            else quest.Fail();
            packet.Sender.Player.ActiveCharacter.Quest = null;

            var ack = new RicePacket(269);
            ack.Writer.Write(questId);
            packet.Sender.Send(ack);
        }

        [RicePacket(270, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestGiveUp(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var quest = packet.Sender.Player.ActiveCharacter.Quest;
            if (quest == null || quest.QID != questId) quest = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID, questId);
            if (quest == null) Log.WriteLine("QuestGiveUp {0}: missao nao estava ativa (liberada).", questId);
            else quest.Fail(true);
            packet.Sender.Player.ActiveCharacter.Quest = null;

            var ack = new RicePacket(271);
            ack.Writer.Write(questId);
            packet.Sender.Send(ack);
        }

        [RicePacket(274, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestGoalPlace(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var quest = packet.Sender.Player.ActiveCharacter.Quest;
            if (quest == null || quest.QID != questId)
            {
                // voltou de um travamento: continua a missao que esta no banco (ou ignora)
                quest = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID, questId);
                if (quest == null) { Log.WriteLine("QuestGoalPlace {0}: missao nao estava ativa (ignorado).", questId); return; }
                packet.Sender.Player.ActiveCharacter.Quest = quest;
            }

            quest.IncrementProgress();
        }
    }
}