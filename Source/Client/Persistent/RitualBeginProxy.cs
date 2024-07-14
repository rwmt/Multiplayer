using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent;

public class RitualBeginProxy : Dialog_BeginRitual, ISwitchToMap
{
    public static RitualBeginProxy drawing;

    public RitualSession Session => map.MpComp().sessionManager.GetFirstOfType<RitualSession>();

    // In the base type, the unused fields are used to create RitualRoleAssignments which already exist in an MP session
    // They are here to help keep the base constructor in sync with this one
    public RitualBeginProxy(
        RitualRoleAssignments assignments,
        string ritualLabel,
        Precept_Ritual ritual,
        TargetInfo target,
        Map map,
        ActionCallback action,
        Pawn organizer,
        RitualObligation obligation,
        PawnFilter filter = null,
        string okButtonText = null,
        // ReSharper disable once UnusedParameter.Local
        List<Pawn> requiredPawns = null,
        // ReSharper disable once UnusedParameter.Local
        Dictionary<string, Pawn> forcedForRole = null,
        RitualOutcomeEffectDef outcome = null,
        List<string> extraInfoText = null,
        // ReSharper disable once UnusedParameter.Local
        Pawn selectedPawn = null) :
        base(assignments, ritual, target, ritual?.outcomeEffect?.def ?? outcome)
    {
        this.obligation = obligation;
        this.filter = filter;
        this.organizer = organizer;
        this.map = map;
        this.action = action;
        ritualExplanation = ritual?.ritualExplanation;
        this.ritualLabel = ritualLabel;
        this.okButtonText = okButtonText ?? "OK".Translate();
        extraInfos = extraInfoText;

        soundClose = SoundDefOf.TabClose;

        // This gets cancelled in the base constructor if called from ticking/cmd in DontClearDialogBeginRitualCache
        // Note that: cachedRoles is a static field, cachedRoles is only used for UI drawing
        cachedRoles.Clear();
        if (ritual is { ideo: not null })
        {
            cachedRoles.AddRange(ritual.ideo.RolesListForReading.Where(r => !r.def.leaderRole));
            Precept_Role preceptRole = Faction.OfPlayer.ideos.PrimaryIdeo.RolesListForReading.FirstOrDefault(p => p.def.leaderRole);
            if (preceptRole != null)
                cachedRoles.Add(preceptRole);
            cachedRoles.SortBy(x => x.def.displayOrderInImpact);
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

            // Make space for the "Switch to map" button
            inRect.yMin += 20f;

            base.DoWindowContents(inRect);
        }
        finally
        {
            drawing = null;
        }
    }
}
