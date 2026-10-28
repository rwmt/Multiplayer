using System;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Patches;

// A Thing spawned outside the synced simulation - neither during ticking nor
// command execution - exists on this client only and desyncs the session much
// later, when pawn decisions around the divergent state first differ (the
// classic unsynced-mod-gizmo bug). Warn the moment it happens, once per def,
// with the stack that names the offender. A negative id means the thing was
// created in interface context (UniqueIdsPatch local id block) - caught
// regardless of where the spawn itself runs.
[HarmonyPatch(typeof(Thing))]
[HarmonyPatch(nameof(Thing.SpawnSetup))]
static class UnsyncedSpawnTripwire
{
    static void Postfix(Thing __instance, bool respawningAfterLoad)
    {
        // Motes/flecks and other no-id things are not simulation state; loading
        // respawns everything outside ticking legitimately
        if (Multiplayer.Client == null || respawningAfterLoad || !__instance.def.HasThingIDNumber)
            return;

        // InInterface (not raw flag checks) so map gen, reloading and long
        // events don't false-positive
        if (__instance.thingIDNumber >= 0 && !Multiplayer.InInterface)
            return;

        Log.WarningOnce(
            $"MP: {__instance.def.defName} (id {__instance.thingIDNumber}) spawned outside the synced simulation - " +
            $"an unsynced mod action is diverging this client. Stack: {Environment.StackTrace}",
            __instance.def.shortHash ^ 0x51DE);
    }
}
