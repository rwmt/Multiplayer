using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
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

    [HarmonyPatch(typeof(Targeter), nameof(Targeter.BeginTargeting), typeof(TargetingParameters), typeof(Action<LocalTargetInfo>), typeof(Pawn), typeof(Action), typeof(Texture2D))]
    static class CancelBeginTargeting
    {
        static bool Prefix()
        {
            if (TickPatch.currentExecutingCmdIssuedBySelf && AsyncTimeComp.executingCmdMap != null)
                AsyncTimeComp.keepTheMap = true;

            return !CancelFeedbackNotTargetedAtMe.Cancel;
        }
    }

    [HarmonyPatch]
    static class CancelMotesNotTargetedAtMe
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(float) });
            yield return AccessTools.Method(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote), new[] { typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float) });
        }

        static bool Prefix(ThingDef moteDef)
        {
            return !CancelFeedbackNotTargetedAtMe.Cancel;
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
        public static bool Cancel =>
            Multiplayer.Client != null &&
            Multiplayer.ExecutingCmds &&
            !TickPatch.currentExecutingCmdIssuedBySelf;

        static bool Prefix() => !Cancel;
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
                if (inst.operand == IsSelected)
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

}
