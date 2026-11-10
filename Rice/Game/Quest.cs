using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using Rice.Server;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;
using Models = Rice.Server.Database.Models;

namespace Rice.Game
{
    public class Quest
    {
        public ulong CID;
        public int QID;
        public int State;
        public int Progress;
        public ushort FailCount;

        public QuestTableEntry QuestInfo;

        private Quest(int qid)
        {
            QID = qid;
            QuestInfo = QuestTable.Find(QID);
        }

        private Quest(Models.QuestState questState)
        {
            QID = questState.QID;
            QuestInfo = QuestTable.Find(QID);

            CID = (ulong) questState.CID;

            State = questState.State;
            Progress = questState.Progress;
            FailCount = (ushort) questState.FailCount;
        }

        public static List<Quest> Retrieve(ulong cid)
        {
            using (var rc = Database.GetContext())
            {
                var quests = rc.QuestStates.Where(q => q.CID == (long) cid).ToList();
                return quests.Select(q => new Quest(q)).ToList();
            }
        }

        public static Quest Retrieve(ulong cid, int qid)
        {
            using (var rc = Database.GetContext())
            {
                var questState = rc.QuestStates.FirstOrDefault(q => q.CID == (long)cid && q.QID == qid);
                return questState != null ? new Quest(questState) : null;
            }
        }

        public static Quest CreateEntry(ulong cid, int qid)
        {
            using (var rc = Database.GetContext())
            {
                long ownerId = (long) cid;
                if (rc.Characters.Find(ownerId) == null)
                {
                    Log.WriteError($"Could not find character {cid} for Quest.CreateEntry");
                    return null;
                }

                rc.QuestStates.Add(new Models.QuestState
                {
                    QID = qid,
                    CID = ownerId,
                    State = 0,
                    Progress = 0,
                    FailCount = 0
                });
                rc.SaveChanges();
                Log.WriteLine("Missao {0} iniciada por CID {1} (registro criado).", qid, cid);
                return new Quest(qid) { CID = cid };
            }
        }

        // Atualiza o registro da missao no banco; se ele nao existir (por qualquer motivo), cria.
        private bool Save(Action<Models.QuestState> change)
        {
            using (var rc = Database.GetContext())
            {
                long ownerId = (long) CID;
                int questId = QID;
                var questState = rc.QuestStates.FirstOrDefault(q => q.CID == ownerId && q.QID == questId);
                if (questState == null)
                {
                    Log.WriteLine("Registro da missao {0} (CID {1}) nao existia; criando.", questId, ownerId);
                    questState = new Models.QuestState
                    {
                        CID = ownerId,
                        QID = questId,
                        State = State,
                        Progress = Progress,
                        FailCount = (short) FailCount
                    };
                    rc.QuestStates.Add(questState);
                }

                change(questState);
                rc.SaveChanges();
                return true;
            }
        }

        public bool IncrementProgress()
        {
            Progress++;
            return Save(q => q.Progress = Progress);
        }

        public bool SetState(int state)
        {
            State = state;
            return Save(q => q.State = state);
        }

        public bool Fail(bool gaveUp = false)
        {
            State = gaveUp ? 4 : 3;
            FailCount++;
            return Save(q =>
            {
                q.State = State;
                q.FailCount = (short) FailCount;
            });
        }
    }
}
