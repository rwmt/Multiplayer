using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.VolumeGame))]
    [MpPatch(typeof(Prefs), nameof(Prefs.Save))]
    static class CancelDuringSkipping
    {
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

    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnLoad))]
    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnError))]
    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.AutomaticPauseMode))]
    static class PrefGettersInMultiplayer
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnLoad))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnError))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.AutomaticPauseMode))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.MaxNumberOfPlayerSettlements))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.RunInBackground))]
    static class PrefSettersInMultiplayer
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

}
