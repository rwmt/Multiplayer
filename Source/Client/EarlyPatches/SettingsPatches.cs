using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Patches;
using Verse;

namespace Multiplayer.Client.EarlyPatches
{
    [EarlyPatch]
    [HarmonyPatch]
    static class PrefGettersInMultiplayer
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.AutomaticPauseMode));
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.PauseOnLoad));
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.AdaptiveTrainingEnabled));
        }

        static bool Prefix() => Multiplayer.Client == null;
    }

    [EarlyPatch]
    [HarmonyPatch]
    static class PrefSettersInMultiplayer
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertySetter(typeof(Prefs), nameof(Prefs.AutomaticPauseMode));
            yield return AccessTools.PropertySetter(typeof(Prefs), nameof(Prefs.PauseOnLoad));
            yield return AccessTools.PropertySetter(typeof(Prefs), nameof(Prefs.AdaptiveTrainingEnabled));
            yield return AccessTools.PropertySetter(typeof(Prefs), nameof(Prefs.MaxNumberOfPlayerSettlements));
            yield return AccessTools.PropertySetter(typeof(Prefs), nameof(Prefs.RunInBackground));
        }

        static bool Prefix() => Multiplayer.Client == null;
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(PrefsData), nameof(PrefsData.Apply))]
    static class PrefsApplyInMultiplayer
    {
        static void Prefix(PrefsData __instance, out (bool, bool) __state)
        {
            __state.Item1 = Multiplayer.Client != null;
            __state.Item2 = __instance.runInBackground;
            if (!__state.Item1) return;
            __instance.runInBackground = true;
        }

        static void Postfix(PrefsData __instance, (bool, bool) __state)
        {
            if (!__state.Item1) return;
            __instance.runInBackground = __state.Item2;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.PreferredNames), MethodType.Getter)]
    static class PreferredNamesPatch
    {
        static List<string> empty = new();

        static void Postfix(ref List<string> __result)
        {
            if (Multiplayer.Client != null)
                __result = empty;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.MaxNumberOfPlayerSettlements), MethodType.Getter)]
    static class MaxColoniesPatch
    {
        static void Postfix(ref int __result)
        {
            if (Multiplayer.Client != null)
                __result = 5;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Getter)]
    static class RunInBackgroundPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = true;
        }
    }

    [EarlyPatch]
    [HarmonyPatch]
    static class CancelDuringSimulating
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.VolumeGame));
            yield return AccessTools.Method(typeof(Prefs), nameof(Prefs.Save));
        }

        static bool Prefix() => !TickPatch.Simulating;
    }
}
