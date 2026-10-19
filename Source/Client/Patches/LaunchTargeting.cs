using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Patches;

// While a player picks a destination for transport pods or a shuttle, the game keeps running: another player can
// launch the group or cancel its loading meanwhile. The world targeter's callbacks then throw every frame and the
// targeter can't be closed (#365). Stop it once the launchable is gone. UI only: nothing here changes game state.
[HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.StartChoosingDestination))]
static class TrackLaunchTargeting
{
    public static CompLaunchable launchable;
    public static Func<GlobalTargetInfo, bool> targeterAction;
    public static bool startedSpawned;

    static void Postfix(CompLaunchable __instance)
    {
        if (Multiplayer.Client == null || !Find.WorldTargeter.IsTargeting) return;

        launchable = __instance;
        targeterAction = Find.WorldTargeter.action;
        // A shuttle in a caravan isn't spawned
        startedSpawned = __instance.parent.Spawned;
    }

    public static void Clear()
    {
        launchable = null;
        targeterAction = null;
    }
}

[HarmonyPatch]
static class StopLaunchTargetingWhenGone
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(WorldTargeter), nameof(WorldTargeter.TargeterUpdate));
        yield return AccessTools.Method(typeof(WorldTargeter), nameof(WorldTargeter.TargeterOnGUI));
        yield return AccessTools.Method(typeof(WorldTargeter), nameof(WorldTargeter.ProcessInputEvents));
    }

    static void Prefix(WorldTargeter __instance)
    {
        var launchable = TrackLaunchTargeting.launchable;
        if (launchable == null) return;

        // Targeting ended or something else started targeting
        if (__instance.action == null || __instance.action != TrackLaunchTargeting.targeterAction)
        {
            TrackLaunchTargeting.Clear();
            return;
        }

        var parent = launchable.parent;
        if (parent.Destroyed ||
            TrackLaunchTargeting.startedSpawned && !parent.Spawned ||
            launchable.Transporter is not { LoadingInProgressOrReadyToLaunch: true })
        {
            TrackLaunchTargeting.Clear();
            __instance.StopTargeting();
        }
    }
}
