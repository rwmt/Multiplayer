using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    // Sanguophage bloodfeed ability shows a confirmation if it'll cause a severe blood loss or kill the target, which we don't want to do.
    // Cancel syncing if the method is showing a confirmation dialog for situations like those. This does not apply if the confirmation dialog
    // is a begin ritual dialog, as we actually want to sync opening those.
    [HarmonyPatch(typeof(Verb_CastAbility), nameof(Verb_CastAbility.OrderForceTarget))]
    public class CancelTargetableAbilityWithConfirmation
    {
        static void Prefix(Verb_CastAbility __instance, LocalTargetInfo target, ref bool __state)
        {
            if (Multiplayer.Client == null ||
                Multiplayer.dontSync ||
                __instance.ability?.ConfirmationDialog(target, () => { }) is not { } dialog || // Use an empty action in case some method checks for null
                dialog is Dialog_BeginRitual) // Use an empty action in case some method checks for null
                return;

            __state = true;
            Multiplayer.dontSync = true;
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Multiplayer.dontSync = false;
        }
    }
}
