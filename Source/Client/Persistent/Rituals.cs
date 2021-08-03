using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static RimWorld.Dialog_BeginRitual;

namespace Multiplayer.Client.Persistent
{
    public record RitualData(
        Precept_Ritual Ritual,
        TargetInfo Target,
        RitualObligation Obligation,
        RitualOutcomeEffectDef Outcome,
        List<string> ExtraInfos,
        ActionCallback Action,
        string RitualLabel,
        string ConfirmText,
        Pawn Organizer,
        MpRitualAssignments Assignments
    );

    public class RitualSession : IExposable, ISession
    {
        private Map map;

        private int sessionId;
        private RitualData data;

        public int SessionId => sessionId;

        public RitualSession(Map map)
        {
            this.map = map;
        }

        public RitualSession(Map map, RitualData data) : this(map)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();
            this.data = data;
            this.data.Assignments.map = map;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");

            data.Assignments.map = map;
        }

        public void OpenWindow(bool sound = true)
        {
            Find.Selector.ClearSelection();

            var dialog = MpUtil.NewObjectNoCtor<Dialog_BeginRitual>();
            if (!sound)
                dialog.soundAppear = null;
            dialog.doCloseX = true;

            dialog.ritual = data.Ritual;
            dialog.ritualExplanation = (data.Ritual != null) ? data.Ritual.ritualExplanation : null;
            dialog.target = data.Target;
            dialog.obligation = data.Obligation;
            dialog.outcome = data.Outcome;
            dialog.extraInfos = data.ExtraInfos;
            dialog.action = data.Action;
            dialog.ritualLabel = data.RitualLabel;
            dialog.confirmText = data.ConfirmText;
            dialog.organizer = data.Organizer;
            dialog.assignments = data.Assignments;

            Find.WindowStack.Add(dialog);
        }
    }

    public class MpRitualAssignments : RitualRoleAssignments
    {
        public Map map;
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogBeginRitual
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.MapContext != null && window.GetType() == typeof(Dialog_BeginRitual))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_BeginRitual), MethodType.Constructor)]
    static class CancelDialogBeginRitualCtor
    {
        static void Postfix(Dialog_BeginRitual __instance, Func<Pawn, bool, bool, bool> filter, Map map)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                // Completes initialization. Taken from Dialog_BeginRitual.PostOpen
                __instance.assignments.FillPawns(filter);

                var comp = map.MpComp();
                if (comp.ritualSession == null)
                    comp.CreateRitualSession(new RitualData(
                        __instance.ritual,
                        __instance.target,
                        __instance.obligation,
                        __instance.outcome,
                        __instance.extraInfos,
                        __instance.action,
                        __instance.ritualLabel,
                        __instance.confirmText,
                        __instance.organizer,
                        MpUtil.ShallowCopy(__instance.assignments, new MpRitualAssignments())
                    ));
            }
        }
    }

}
