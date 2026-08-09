using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Factions;

// Safety nets for multifaction incident routing (the root fix is in
// StorytellerTargetsPatch): log anything that still slips through.

[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
static class SuppressCrossFactionIncidents
{
    static bool Prefix(IncidentWorker __instance, IncidentParms parms, ref bool __result)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return true;

        // Explicit player orders always execute: an incident fired inside a
        // synced command (e.g. a Call-Aid permit used while standing on
        // another faction's map) is deliberate, not a routing bug - only
        // storyteller/quest/tick-driven incidents keep the safety net
        if (TickPatch.currentExecutingCmdType == CommandType.Sync)
            return true;

        if (parms.target is Map { ParentFaction: { IsPlayer: true } owner } && owner != Faction.OfPlayer)
        {
            // QuestFactionOwnership now routes quest-fired incidents through
            // the owner's context - spectator context here is a residual gap.
            // Log it but let it through rather than eat a quest raid.
            if (Faction.OfPlayer == Multiplayer.WorldComp.spectatorFaction)
            {
                MpLog.Log(
                    $"Spectator-context incident {__instance.def?.defName} targeting map of " +
                    $"{owner.Name} - unrouted generation source, letting it through");
                return true;
            }

            MpLog.Error(
                $"Suppressed cross-faction incident {__instance.def?.defName} targeting map of " +
                $"{owner.Name} while executing as {Faction.OfPlayer?.Name} - report this, the " +
                "root-cause filter should have prevented it");
            // Report "fired" so the storyteller/queue doesn't retry it elsewhere
            __result = true;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Storyteller), nameof(Storyteller.TryFire))]
static class WarnSpectatorStorytellerFire
{
    static void Prefix(FiringIncident fi)
    {
        if (Multiplayer.Client != null && Multiplayer.GameComp.multifaction &&
            Faction.OfPlayer == Multiplayer.WorldComp.spectatorFaction)
            MpLog.Error($"Storyteller fired {fi?.def?.defName} in spectator faction context - " +
                        "an unrouted generation source");
    }
}
