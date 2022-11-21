using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches
{
    // todo letter timeouts and async time

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LetterStackUpdate))]
    static class CloseLettersDuringSimulating
    {
        // Close info letters during simulation and for the arbiter instance
        static void Postfix(LetterStack __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!TickPatch.Simulating && !Multiplayer.arbiterInstance) return;

            for (int i = __instance.letters.Count - 1; i >= 0; i--)
            {
                var letter = __instance.letters[i];
                if (letter is ChoiceLetter choice && choice.Choices.Any(c => c.action?.Method == choice.Option_Close.action.Method) && Time.time - letter.arrivalTime > 4)
                    __instance.RemoveLetter(letter);
            }
        }
    }

    [HarmonyPatch]
    static class CloseDialogsForExpiredLetters
    {
        public static Dictionary<Type, (MethodInfo method, FastInvokeHandler handler)> defaultChoices = new();

        public static void RegisterDefaultLetterChoice(MethodInfo method, Type letterType = null)
        {
            if (method == null)
                return;
            if (letterType == null)
            {
                if (method.DeclaringType == null)
                    return;
                letterType = method.DeclaringType;
            }

            var handler = MethodInvoker.GetHandler(method);
            defaultChoices[letterType] = (method, handler);
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(ChoiceLetter), nameof(ChoiceLetter.OpenLetter));
            yield return AccessTools.DeclaredMethod(typeof(ChoiceLetter_GrowthMoment), nameof(ChoiceLetter_GrowthMoment.OpenLetter)); // Not a subtype of ChoiceLetter, only LetterWithTimeout
        }

        static bool Prefix(LetterWithTimeout __instance)
        {
            // The letter is about to be force-shown by LetterStack.LetterStackTick because of expiry
            if (Multiplayer.Ticking
                && __instance.TimeoutActive
                && __instance.disappearAtTick == Find.TickManager.TicksGame + 1
                && defaultChoices.TryGetValue(__instance.GetType(), out var tuple))
            {
                if (tuple.method.IsStatic)
                    tuple.handler(null, __instance);
                else
                    tuple.handler(__instance);

                return false;
            }

            return true;
        }

        static void Postfix(LetterWithTimeout __instance)
        {
            var wasArchived = __instance.ArchivedOnly;
            var window = Find.WindowStack.WindowOfType<Dialog_NodeTreeWithFactionInfo>();

            if (window != null)
                window.innerWindowOnGUICached = (_ =>
                {
                    if (__instance.ArchivedOnly != wasArchived)
                        window.Close();
                }) + window.innerWindowOnGUICached;
        }
    }

    // ChoiceLetter_BabyBirth and ChoiceLetter_BabyToChild can be dismissed with right-click, which causes issues in MP if some of the players do it
    [HarmonyPatch(typeof(Letter), nameof(Letter.CanDismissWithRightClick), MethodType.Getter)]
    static class DontDismissBabyLetters
    {
        static bool Prefix(Letter __instance, ref bool __result)
        {
            if (__instance is ChoiceLetter_BabyBirth or ChoiceLetter_BabyToChild)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    // The button to apply the choices from growth moment dialog will close the letter right after applying the changes, which puts it into archive.
    // When reading sync data we check for the letter in the letter stack, and not archive - which fails to read the letter.
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.RemoveLetter))]
    static class DontRemoveGrowthMomentLetter
    {
        static bool Prefix(Letter let) =>
            Multiplayer.Client == null ||
            !IsDrawingGrowthMomentDialog.isDrawing;
    }

    [HarmonyPatch(typeof(Dialog_GrowthMomentChoices), nameof(Dialog_GrowthMomentChoices.DoWindowContents))]
    static class IsDrawingGrowthMomentDialog
    {
        public static bool isDrawing = false;

        static void Prefix() => isDrawing = true;

        static void Postfix() => isDrawing = false;
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.OpenAutomaticLetters))]
    static class DontAutoOpenLettersOnTimeout
    {
        static bool Prefix() => Multiplayer.Client == null;
    }
}
