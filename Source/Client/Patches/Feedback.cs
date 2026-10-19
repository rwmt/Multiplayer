using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch]
    static class CancelFeedbackNotTargetedAtMe
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot));
            yield return AccessTools.Method(typeof(Command_SetPlantToGrow), nameof(Command_SetPlantToGrow.WarnAsAppropriate));
            yield return AccessTools.Method(typeof(TutorUtility), nameof(TutorUtility.DoModalDialogIfNotKnown));
            yield return AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TryHideWorld));
        }

        public static bool Cancel =>
            Multiplayer.Client != null &&
            Multiplayer.ExecutingCmds &&
            !TickPatch.currentExecutingCmdIssuedBySelf;

        static bool Prefix() => !Cancel;
    }

    [HarmonyPatch(typeof(Targeter), nameof(Targeter.BeginTargeting), typeof(TargetingParameters), typeof(Action<LocalTargetInfo>), typeof(Pawn), typeof(Action), typeof(Texture2D), typeof(bool))]
    static class CancelBeginTargeting
    {
        static bool Prefix()
        {
            if (TickPatch.currentExecutingCmdIssuedBySelf && AsyncTimeComp.executingCmdMap != null)
                AsyncTimeComp.keepTheMap = true;

            return !CancelFeedbackNotTargetedAtMe.Cancel;
        }
    }

    [HarmonyPatch(typeof(WorldTargeter), nameof(WorldTargeter.StopTargeting))]
    static class CancelCancelGlobalTargeting
    {
        static bool Prefix()
        {
            return !CancelFeedbackNotTargetedAtMe.Cancel;
        }
    }

    [HarmonyPatch]
    static class CancelMotesNotTargetedAtMe
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(float) });
            yield return AccessTools.Method(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float), typeof(bool), typeof(float) });
        }

        static bool Prefix(object[] __args, ThingDef moteDef, ref Mote __result)
        {
            // Catches ritual effect motes
            if (moteDef.mote.solidTime >= 99999)
                return true;

            if (!CancelFeedbackNotTargetedAtMe.Cancel)
                return true;

            // Callers may use the result and FixNullMotes makes it non-null for the issuer (unless out of bounds),
            // so return the same kind of result on the other clients. The mote is made (motes get no thing ID)
            // but not spawned.
            var cell = __args[0] switch
            {
                IntVec3 c => c,
                Vector3 v => v.ToIntVec3(),
                _ => IntVec3.Invalid
            };

            __result = __args[1] is Map map && cell.InBounds(map) ? ThingMaker.MakeThing(moteDef) as Mote : null;
            return false;
        }
    }

    [HarmonyPatch]
    static class CancelFlecksNotTargetedAtMe
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FleckManager), nameof(FleckManager.CreateFleck));
        }

        static bool Prefix(ref FleckCreationData fleckData)
        {
            if (fleckData.def == FleckDefOf.FeedbackGoto)
                return true;

            return !CancelFeedbackNotTargetedAtMe.Cancel;
        }
    }

    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(Message), typeof(bool) })]
    static class SilenceMessagesNotTargetedAtMe
    {
        static bool Prefix(bool historical)
        {
            bool cancel = Multiplayer.Client != null && !historical && Multiplayer.ExecutingCmds && !TickPatch.currentExecutingCmdIssuedBySelf;
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(DesignatorManager), nameof(DesignatorManager.Deselect))]
    static class CancelDesignatorDeselection
    {
        static bool stopCancel = false;

        public static bool Cancel =>
            Multiplayer.Client != null &&
            Multiplayer.ExecutingCmds &&
            !TickPatch.currentExecutingCmdIssuedBySelf;

        static bool Prefix(DesignatorManager __instance)
        {
            return stopCancel || !Cancel;
        }

        internal static void DisableCanceling()
        {
            stopCancel = false;
        }

        internal static void EnableCanceling()
        {
            stopCancel = true;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    static class AlwaysDeselectWhileDespawning
    {
        static MethodInfo IsSelected = AccessTools.Method(typeof(Selector), nameof(Selector.IsSelected));
        static MethodInfo DeselectOnDespawnMethod = AccessTools.Method(typeof(AlwaysDeselectWhileDespawning), nameof(DeselectOnDespawn));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand as MethodInfo == IsSelected)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, DeselectOnDespawnMethod);
                }

                yield return inst;
            }
        }

        static void DeselectOnDespawn(Thing t)
        {
            if (Multiplayer.Client == null || AsyncTimeComp.prevSelected == null) return;
            AsyncTimeComp.prevSelected.Remove(t);
        }
    }

    [HarmonyPatch(typeof(Bill), nameof(Bill.CreateNoPawnsWithSkillDialog))]
    static class CancelNoPawnWithSkillDialog
    {
        static bool Prefix() =>
            Multiplayer.Client == null ||
            TickPatch.currentExecutingCmdIssuedBySelf;
    }

    [HarmonyPatch]
    static class NoCameraJumpingDuringSimulating
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TrySelect));
            yield return AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TryJumpAndSelect));
            yield return AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TryJump), new[] {typeof(GlobalTargetInfo), typeof(CameraJumper.MovementMode)});
        }
        static bool Prefix() => !TickPatch.Simulating;
    }

    [HarmonyPatch(typeof(Selector), nameof(Selector.Deselect))]
    static class SelectorDeselectPatch
    {
        public static List<object> deselected;

        static void Prefix(object obj)
        {
            if (deselected != null)
                deselected.Add(obj);
        }
    }

    [HarmonyPatch(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.OrderToPod))]
    static class NoBiosculpterConfirmationSyncing
    {
        static bool Prefix(CompBiosculpterPod_Cycle cycle, Pawn pawn, Action giveJobAct)
        {
            if (Multiplayer.Client == null || cycle is not CompBiosculpterPod_HealingCycle healingCycle)
                return true; // Alternatively we could return false and invoke giveJobAct

            var healingDescriptionForPawn = healingCycle.GetHealingDescriptionForPawn(pawn);
            string text = healingDescriptionForPawn.NullOrEmpty()
                ? "BiosculpterNoCoditionsToHeal".Translate(pawn.Named("PAWN"), healingCycle.Props.label.Named("CYCLE")).Resolve()
                : "OnCompletionOfCycle".Translate(healingCycle.Props.label.Named("CYCLE")).Resolve() + ":\n\n" + healingDescriptionForPawn;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                text,
                giveJobAct,
                healingDescriptionForPawn.NullOrEmpty()));

            return false;
        }
    }

    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.OrderForceTarget))]
    static class NoTargetableCompUsableSyncing
    {
        static void Prefix(CompUsable __instance, ref bool __state)
        {
            if (Multiplayer.Client == null || Multiplayer.dontSync) return;

            // Hopefully, all mods implement `PlayerChoosesTarget` as either always being true or false.
            // If this is conditional then there could be some issues.
            if (__instance.parent.GetComps<CompTargetable>().Any(x => x.PlayerChoosesTarget))
            {
                Multiplayer.dontSync = true;
                __state = true;
            }
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Multiplayer.dontSync = false;
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove),
        nameof(FloatMenuOptionProvider_DraftedMove.PawnGotoAction))]
    static class DraftedMove_GotoFeedbackPatch
    {
        private static MethodInfo tryTakeOrderedJob =
            AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob));

        private static MethodInfo endCurrentJob =
            AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var inst in instructions)
            {
                if (inst.Calls(tryTakeOrderedJob)) inst.operand = ((Delegate)CustomTryTakeOrderedJob).Method;
                else if (inst.Calls(endCurrentJob)) inst.operand = ((Delegate)CustomEndCurrentJob).Method;
                yield return inst;
            }
        }

        [SyncMethod(exposeParameters = [1], context = SyncContext.QueueOrder_Down)]
        static bool CustomTryTakeOrderedJob(Pawn_JobTracker self, Job job, JobTag? tag = JobTag.Misc,
            bool requestQueueing = false)
        {
            if (self.TryTakeOrderedJob(job, tag, requestQueueing) && (TickPatch.currentExecutingCmdIssuedBySelf || Multiplayer.Client == null))
                FleckMaker.Static(job.targetA.Cell, self.pawn.Map, FleckDefOf.FeedbackGoto);
            return false;
        }

        // PawnGotoAction can also stop a pawn without going through TryTakeOrderedJob: when the pawn is
        // already standing on gotoLoc and its current job is Goto, it calls EndCurrentJob directly. That
        // call isn't synced, so the pawn stops only for the player who issued the order while it keeps
        // walking for everyone else, causing a desync. Sync it the same way as the TryTakeOrderedJob call.
        static void CustomEndCurrentJob(Pawn_JobTracker self, JobCondition condition,
            bool startNewJob = true, bool canReturnToPool = true)
        {
            // Vanilla only calls this while the current job is a Goto: remember which one
            SyncedEndGotoJob(self, condition, startNewJob, canReturnToPool, self.curJob?.loadID ?? -1);
        }

        [SyncMethod]
        static void SyncedEndGotoJob(Pawn_JobTracker self, JobCondition condition, bool startNewJob,
            bool canReturnToPool, int jobId)
        {
            // The command executes ticks after the click: end only the goto the order was aimed at, not a job or
            // another goto the pawn started in between. Not its position: the pawn keeps walking until then.
            if (self.curJob == null || self.curJob.loadID != jobId || self.curJob.def != JobDefOf.Goto)
                return;

            self.EndCurrentJob(condition, startNewJob, canReturnToPool);
        }
    }

    // Extended camera shakes (CameraShaker.DoShake(mag, durationTicks)) store TicksGame when requested and
    // CameraShaker.Update compares it with TicksGame again. Under async time these can be different clocks: a map's
    // when requested in its tick or command, the world's in the interface. A shake requested on a map ahead of the
    // world went on until the world caught up, possibly for good (#696). In MP both use TickPatch.Timer instead: it's
    // the same for every map and the world, and stops while the gravship cutscenes freeze the game, like TicksGame does
    // in a vanilla landing (paused). Camera shake is local UI, nothing in the simulation reads it.
    [HarmonyPatch(typeof(ExtendedShakeRequest), MethodType.Constructor, typeof(float), typeof(int))]
    static class ExtendedShakeRequestUsesTimer
    {
        static void Postfix(ExtendedShakeRequest __instance)
        {
            if (Multiplayer.Client != null)
                __instance.startTick = TickPatch.Timer;
        }
    }

    [HarmonyPatch(typeof(CameraShaker), nameof(CameraShaker.Update))]
    static class CameraShakerUpdateUsesTimer
    {
        private static readonly MethodInfo TicksGameGetter =
            AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TicksGame));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                // Consumes the TickManager instance like the getter
                if (inst.Calls(TicksGameGetter))
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraShakerUpdateUsesTimer), nameof(ShakeClock))).MoveLabelsFrom(inst);
                else
                    yield return inst;
            }
        }

        static int ShakeClock(TickManager tickManager) =>
            Multiplayer.Client != null ? TickPatch.Timer : tickManager.TicksGame;
    }

    // The requests are saved with the camera. Ones from a save which used TicksGame (or with a later timer) would
    // otherwise last until the timer reaches their start.
    [HarmonyPatch(typeof(CameraShaker), nameof(CameraShaker.Expose))]
    static class CameraShakerLoadClampsToTimer
    {
        static void Postfix(CameraShaker __instance)
        {
            if (Multiplayer.Client == null || Scribe.mode != LoadSaveMode.LoadingVars) return;

            foreach (var request in __instance.extendedShakeRequests)
                if (request != null && request.startTick > TickPatch.Timer)
                    request.startTick = TickPatch.Timer;
        }
    }

}
