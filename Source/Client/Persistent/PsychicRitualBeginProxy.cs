using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Client.Persistent;

public class PsychicRitualBeginProxy : Dialog_BeginPsychicRitual, ISwitchToMap
{
    public static PsychicRitualBeginProxy drawing;

    public PsychicRitualSession Session => map.MpComp().sessionManager.GetFirstOfType<PsychicRitualSession>();

    public PsychicRitualBeginProxy(
        PsychicRitualDef def,
        PsychicRitualCandidatePool candidatePool,
        PsychicRitualRoleAssignments assignments,
        Map map) :
        base(def, candidatePool, assignments, map)
    {
        var session = Session;

        if (Session == null)
        {
            Log.Error("Trying to open a psychic ritual dialog proxy without session active");
            return;
        }

        try
        {
            // Ensure that InitializeCast call is seeded, use session and map IDs to get a somewhat random value.
            // We could also include current map tick as well, if needed.
            Rand.PushState(Gen.HashCombineInt(session.SessionId, map.uniqueID));

            // Recache the pending issues for the ritual.
            // Each time the ritual is started, this method is called. However,
            // in MP we can have multiple rituals active at a time, so ensure
            // that we recache if the ritual is valid on a specific map.
            // If there's ever issues with this, we may need to call this
            // in DoWindowContents, however it shouldn't be needed.
            def.InitializeCast(map);
        }
        finally
        {
            // Pop RNG state in finally to ensure no issues when an exception occurs.
            Rand.PopState();
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        drawing = this;

        try
        {
            var session = Session;

            if (session == null)
            {
                soundClose = SoundDefOf.Click;
                Close();
            }

            base.DoWindowContents(inRect);
        }
        finally
        {
            drawing = null;
        }
    }

    public override void Start()
    {
        if (CanBegin)
            Session?.Start();
    }
}
