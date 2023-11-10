using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Multiplayer.Client.Experimental;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using static Verse.Widgets;

namespace Multiplayer.Client.Persistent
{
    public class RitualSession : SemiPersistentSession, IPausingWithDialog
    {
        public Map map;
        public RitualData data;

        public override Map Map => map;

        public RitualSession(Map map)
        {
            this.map = map;
        }

        public RitualSession(Map map, RitualData data)
        {
            this.map = map;
            this.data = data;
            this.data.assignments.session = this;
        }

        [SyncMethod]
        public void Remove()
        {
            map.MpComp().sessionManager.RemoveSession(this);
        }

        [SyncMethod]
        public void Start()
        {
            if (data.action != null && data.action(data.assignments))
                Remove();
        }

        public void OpenWindow(bool sound = true)
        {
            var dialog = new BeginRitualProxy(
                null,
                data.ritualLabel,
                data.ritual,
                data.target,
                map,
                data.action,
                data.organizer,
                data.obligation,
                null,
                data.confirmText,
                null,
                null,
                null,
                data.outcome,
                data.extraInfos,
                null
            )
            {
                assignments = data.assignments
            };

            if (!sound)
                dialog.soundAppear = null;

            Find.WindowStack.Add(dialog);
        }

        public override void Sync(SyncWorker sync)
        {
            if (sync.isWriting)
            {
                sync.Write(data);
            }
            else
            {
                data = sync.Read<RitualData>();
                data.assignments.session = this;
            }
        }

        public override bool IsCurrentlyPausing(Map map) => map == this.map;

        public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
        {
            return new FloatMenuOption("MpRitualSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                OpenWindow();
            });
        }
    }

    public class MpRitualAssignments : RitualRoleAssignments
    {
        public RitualSession session;
    }

    public class BeginRitualProxy : Dialog_BeginRitual, ISwitchToMap
    {
        public static BeginRitualProxy drawing;

        public RitualSession Session => map.MpComp().sessionManager.GetFirstOfType<RitualSession>();

        public BeginRitualProxy(string header, string ritualLabel, Precept_Ritual ritual, TargetInfo target, Map map, ActionCallback action, Pawn organizer, RitualObligation obligation, Func<Pawn, bool, bool, bool> filter = null, string confirmText = null, List<Pawn> requiredPawns = null, Dictionary<string, Pawn> forcedForRole = null, string ritualName = null, RitualOutcomeEffectDef outcome = null, List<string> extraInfoText = null, Pawn selectedPawn = null) : base(header, ritualLabel, ritual, target, map, action, organizer, obligation, filter, confirmText, requiredPawns, forcedForRole, ritualName, outcome, extraInfoText, selectedPawn)
        {
            soundClose = SoundDefOf.TabClose;
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

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
    static class MakeCancelRitualButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (BeginRitualProxy.drawing == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        static void Postfix(bool __state, ref DraggableResult __result)
        {
            if (!__state) return;

            GUI.color = Color.white;
            if (__result.AnyPressed())
            {
                BeginRitualProxy.drawing.Session?.Remove();
                __result = DraggableResult.Idle;
            }
        }
    }

    [HarmonyPatch]
    static class HandleStartRitual
    {
        static MethodBase TargetMethod()
        {
            return MpMethodUtil.GetLocalFunc(
                typeof(Dialog_BeginRitual),
                nameof(Dialog_BeginRitual.DoWindowContents),
                localFunc: "Start"
            );
        }

        static bool Prefix(Dialog_BeginRitual __instance)
        {
            if (__instance is BeginRitualProxy proxy)
            {
                proxy.Session.Start();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogBeginRitual
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.Client != null
                && window.GetType() == typeof(Dialog_BeginRitual) // Doesn't let BeginRitualProxy through
                && (Multiplayer.ExecutingCmds || Multiplayer.Ticking))
            {
                var dialog = (Dialog_BeginRitual)window;
                dialog.PostOpen(); // Completes initialization

                var comp = dialog.map.MpComp();
                var session = comp.sessionManager.GetFirstOfType<RitualSession>();

                if (session != null &&
                    (session.data.ritual != dialog.ritual ||
                     session.data.outcome != dialog.outcome))
                {
                    Messages.Message("MpAnotherRitualInProgress".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                if (session == null)
                {
                    var data = new RitualData
                    {
                        ritual = dialog.ritual,
                        target = dialog.target,
                        obligation = dialog.obligation,
                        outcome = dialog.outcome,
                        extraInfos = dialog.extraInfos,
                        action = dialog.action,
                        ritualLabel = dialog.ritualLabel,
                        confirmText = dialog.confirmText,
                        organizer = dialog.organizer,
                        assignments = MpUtil.ShallowCopy(dialog.assignments, new MpRitualAssignments())
                    };

                    session = comp.CreateRitualSession(data);
                }

                if (TickPatch.currentExecutingCmdIssuedBySelf)
                    session?.OpenWindow();

                return false;
            }

            return true;
        }
    }

    // We do not sync Ability.QueueCastingJob - instead syncing the delegate method queueing the job.
    // We do this to avoid confirmation dialogs from some of the abilities, like neuroquake and a few ones added in Biotech.
    // This causes an issue with abilities like throne speech or leader speech - the ritual dialog itself is treated as the confirmation dialog.
    // This patch exists to catch abilities that would create ritual "confirmation" dialogs, and instead syncs the call to QueueCastingJob instead.
    // There is no need to do this for global abilities as there are none that create rituals, and (at least up to 1.4) there are no other abilities that create non-confirmation dialogs
    [HarmonyPatch(typeof(Ability), nameof(Ability.QueueCastingJob), typeof(LocalTargetInfo), typeof(LocalTargetInfo))]
    static class RedirectRitualDialogCast
    {
        static bool Prefix(Ability __instance, LocalTargetInfo target, LocalTargetInfo destination)
        {
            if (Multiplayer.Client == null ||
                Multiplayer.ExecutingCmds ||
                Multiplayer.Ticking ||
                !__instance.EffectComps.Any(effect => effect is CompAbilityEffect_StartRitual))
                return true;

            SyncedOpenDialog(__instance, target, destination);
            return false;
        }

        [SyncMethod]
        static void SyncedOpenDialog(Ability ability, LocalTargetInfo target, LocalTargetInfo destination)
            => ability.QueueCastingJob(target, destination);
    }

    // Fixes the issue where the pawn list gets reset to default every time you re-open the dialog.
    // Additionally, due to the pawns being assigned to spectating first in PostOpen through RitualRoleAssignments.TryAssignSpectate,
    // a method which we sync, it called that method after the pawns had their forced assignments. This could cause situations like
    // no pawn being selected for the leader/throne speech ritual (being forced as a spectator instead). This fixes that issue as well.
    [HarmonyPatch(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.PostOpen))]
    static class PreventRepeatedRitualPostOpenCalls
    {
        static bool Prefix() => Multiplayer.Client == null || Multiplayer.ExecutingCmds;
    }


}
