using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TutorSystem), nameof(TutorSystem.AdaptiveTrainingEnabled), MethodType.Getter)]
    static class DisableAdaptiveLearningPatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    static class AdaptiveLearning_PrefsPatch
    {
        [MpPostfix(typeof(Prefs), "get_" + nameof(Prefs.AdaptiveTrainingEnabled))]
        static void Getter_Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = true;
        }

        [MpPrefix(typeof(Prefs), "set_" + nameof(Prefs.AdaptiveTrainingEnabled))]
        static bool Setter_Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch]
    static class CancelDuringSkipping
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Prefs), "get_" + nameof(Prefs.VolumeGame));
            yield return AccessTools.Method(typeof(Prefs), nameof(Prefs.Save));
        }

        static bool Prefix() => !TickPatch.Skipping;
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.MaxNumberOfPlayerSettlements), MethodType.Getter)]
    static class MaxColoniesPatch
    {
        static void Postfix(ref int __result)
        {
            if (Multiplayer.Client != null)
                __result = 5;
        }
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Getter)]
    static class RunInBackgroundPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = true;
        }
    }

    [HarmonyPatch]
    static class PrefGettersInMultiplayer
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Prefs), "get_" + nameof(Prefs.PauseOnError));
            yield return AccessTools.Method(typeof(Prefs), "get_" + nameof(Prefs.AutomaticPauseMode));
        }
        static bool Prefix() => Multiplayer.Client == null;
    }

    // Force PauseOnLoad off in Multiplayer: misaligned settings cause immediate desyncs on load
    [HarmonyPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnLoad))]
    static class PauseOnErrorGetter
    {
        static bool Prefix(ref bool __result) {
            if (Multiplayer.Client != null) {
                __result = false;
                return false;
            }

            return true;
        }
    }


    [HarmonyPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnLoad))]
    static class PauseOnErrorSetter
    {
        static void Prefix(ref bool value) {
            if (Multiplayer.Client != null) {
                value = false; // force parameter to false
            }
        }
    }

    [HarmonyPatch]
    static class PrefSettersInMultiplayer
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Prefs), "set_" + nameof(Prefs.PauseOnError));
            yield return AccessTools.Method(typeof(Prefs), "set_" + nameof(Prefs.AutomaticPauseMode));
            yield return AccessTools.Method(typeof(Prefs), "set_" + nameof(Prefs.MaxNumberOfPlayerSettlements));
            yield return AccessTools.Method(typeof(Prefs), "set_" + nameof(Prefs.RunInBackground));
        }
        static bool Prefix() => Multiplayer.Client == null;
    }

}
