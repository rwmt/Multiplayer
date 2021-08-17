using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.EarlyPatches
{
    [HarmonyPatch]
    static class PrefGettersInMultiplayer
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.PauseOnError));
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.AutomaticPauseMode));
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.PauseOnLoad));
        }

        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.PreferredNames), MethodType.Getter)]
    static class PreferredNamesPatch
    {
        static List<string> empty = new List<string>();

        static void Postfix(ref List<string> __result)
        {
            if (Multiplayer.Client != null)
                __result = empty;
        }
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
    static class CancelDuringSimulating
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.VolumeGame));
            yield return AccessTools.Method(typeof(Prefs), nameof(Prefs.Save));
        }

        static bool Prefix() => !TickPatch.Simulating;
    }

    [HarmonyPatch(typeof(TutorSystem), nameof(TutorSystem.AdaptiveTrainingEnabled), MethodType.Getter)]
    static class DisableAdaptiveLearningPatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

}
