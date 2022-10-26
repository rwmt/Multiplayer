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

    [HarmonyPatch(typeof(ChoiceLetter), nameof(ChoiceLetter.OpenLetter))]
    static class CloseDialogsForExpiredLetters
    {
        public static Dictionary<Type, FastInvokeHandler> rejectMethods = new();
        internal static FastInvokeHandler choseBabyColonist;
        internal static FastInvokeHandler choseBabySlave;

        static bool Prefix(ChoiceLetter __instance)
        {
            // The letter is about to be force-shown by LetterStack.LetterStackTick because of expiry
            if (Multiplayer.Ticking
                && __instance.TimeoutActive
                && __instance.disappearAtTick == Find.TickManager.TicksGame + 1)
            {
                if (rejectMethods.TryGetValue(__instance.GetType(), out var method))
                {
                    method.Invoke(__instance);
                    return false;
                }
                // Special case - no predefined reject option. Keep the current state (as either colonist or slave)
                if (__instance is ChoiceLetter_BabyToChild babyToChild)
                {
                    if (babyToChild.bornSlave)
                        choseBabySlave.Invoke(babyToChild);
                    else
                        choseBabyColonist.Invoke(babyToChild);

                    return false;
                }
            }

            return true;
        }

        static void Postfix(ChoiceLetter __instance)
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
}
