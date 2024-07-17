using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using Verse;
using static RimWorld.Dialog_BeginRitual;

namespace Multiplayer.Client.Persistent
{
    public class MpRitualAssignments : RitualRoleAssignments
    {
        public RitualSession session;
    }

    public class RitualData : ISynchronizable
    {
        public Precept_Ritual ritual;
        public TargetInfo target;
        public RitualObligation obligation;
        public RitualOutcomeEffectDef outcome;
        public List<string> extraInfos;
        public ActionCallback action;
        public string ritualLabel;
        public string confirmText;
        public Pawn organizer;
        public MpRitualAssignments assignments;

        public void Sync(SyncWorker sync)
        {
            sync.Bind(ref ritual);
            sync.Bind(ref target);
            sync.Bind(ref obligation);
            sync.Bind(ref outcome);
            sync.Bind(ref extraInfos);

            if (sync is WritingSyncWorker writer1)
                DelegateSerialization.WriteDelegate(writer1.Writer, action);
            else if (sync is ReadingSyncWorker reader)
                action = (ActionCallback)DelegateSerialization.ReadDelegate(reader.Reader);

            sync.Bind(ref ritualLabel);
            sync.Bind(ref confirmText);
            sync.Bind(ref organizer);

            if (sync is WritingSyncWorker writer2)
                writer2.Bind(ref assignments, new SyncType(typeof(MpRitualAssignments)) { expose = true });
            else if (sync is ReadingSyncWorker reader)
                reader.Bind(ref assignments, new SyncType(typeof(MpRitualAssignments)) { expose = true });
        }
    }
}
