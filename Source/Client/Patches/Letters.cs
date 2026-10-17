using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches
{
    // todo letter timeouts and async time

    /// <summary>
    /// In multifaction every client keeps the same letter stack, as the simulation reads it (expiring letters and their
    /// default choices, growth moment sessions, archive culling). Letters are only hidden in the UI from players of other
    /// factions, based on the faction (Faction.OfPlayer) they were received for.
    /// </summary>
    public static class MultifactionLetters
    {
        public static bool Active => Multiplayer.Client != null && Multiplayer.game?.gameComp?.multifaction == true;

        public static void RecordReceivingFaction(Letter let)
        {
            // Negative ids are interface-only letters
            if (!Active || let == null || let.ID < 0 || Faction.OfPlayer == null) return;

            // Delayed letters are received again later from the queue (in another context), keep the first faction
            var letterFactions = Multiplayer.game.worldComp?.letterFactions;
            if (letterFactions != null && !letterFactions.ContainsKey(let.ID))
                letterFactions[let.ID] = Faction.OfPlayer.loadID;
        }

        public static bool IsVisibleLocally(Letter let)
        {
            if (!Active || let == null) return true;

            var worldComp = Multiplayer.game.worldComp;
            if (worldComp == null || !worldComp.letterFactions.TryGetValue(let.ID, out var factionId))
                return true;

            // Letters received in a spectator context (e.g. world ticks) aren't specific to a player faction
            return factionId == Multiplayer.RealPlayerFaction?.loadID ||
                   factionId == worldComp.spectatorFaction?.loadID ||
                   Find.FactionManager.GetById(factionId) == null;
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string), typeof(int), typeof(bool))]
    static class RecordLetterFaction
    {
        // The letter being received, for the arrival sound
        public static Letter receiving;

        static void Prefix(Letter let, ref Letter __state)
        {
            if (Multiplayer.Client == null) return;

            MultifactionLetters.RecordReceivingFaction(let);
            __state = receiving;
            receiving = let;
        }

        static void Finalizer(Letter __state)
        {
            if (Multiplayer.Client == null) return;
            receiving = __state;
        }
    }

    // Draw only the letters of the local player's faction, the stack itself is kept the same on every client
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LettersOnGUI))]
    static class LettersOnGUIOnlyMyFaction
    {
        // The actual list while the filtered one is being drawn
        public static List<Letter> realLetters;
        private static List<Letter> visibleLetters = new();

        static void Prefix(LetterStack __instance, ref bool __state)
        {
            if (!MultifactionLetters.Active || realLetters != null) return;

            var letters = __instance.letters;
            if (letters.All(MultifactionLetters.IsVisibleLocally)) return;

            visibleLetters.Clear();
            foreach (var letter in letters)
                if (MultifactionLetters.IsVisibleLocally(letter))
                    visibleLetters.Add(letter);

            realLetters = letters;
            __instance.letters = visibleLetters;
            __state = true;
        }

        static void Finalizer(LetterStack __instance, bool __state)
        {
            if (!__state) return;

            var drawn = __instance.letters;
            __instance.letters = realLetters;
            realLetters = null;

            // Removals are applied by LetterStackRemoveDuringFilteredGUI, add letters received while drawing
            foreach (var letter in drawn)
                if (!__instance.letters.Contains(letter))
                    __instance.letters.Add(letter);

            visibleLetters.Clear();
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.RemoveLetter))]
    static class LetterStackRemoveDuringFilteredGUI
    {
        static void Postfix(LetterStack __instance, Letter let)
        {
            // Removed from the drawn list (e.g. dismissed with right click), remove it from the actual one too
            if (LettersOnGUIOnlyMyFaction.realLetters != null && !__instance.letters.Contains(let))
                LettersOnGUIOnlyMyFaction.realLetters.Remove(let);
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.Notify_LetterMouseover))]
    static class LetterMouseoverDuringFilteredGUI
    {
        static void Postfix(LetterStack __instance, Letter let)
        {
            // The index is used on the actual list in LetterStackUpdate
            if (LettersOnGUIOnlyMyFaction.realLetters != null)
                __instance.mouseoverLetterIndex = LettersOnGUIOnlyMyFaction.realLetters.IndexOf(let);
        }
    }

    // LetterStackTick force-opens letters about to expire on every client (the stack is the same everywhere). Letters of
    // other factions are still opened, as opening may change the simulation (e.g. NewQuestLetter removes itself from
    // the stack), but without their UI (windows, main tab switch).
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LetterStackTick))]
    static class OpenExpiringLettersForTheirFaction
    {
        public static bool suppressUI;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var openLetter = AccessTools.Method(typeof(Letter), nameof(Letter.OpenLetter));
            var replacement = AccessTools.Method(typeof(OpenExpiringLettersForTheirFaction), nameof(OpenLetter));

            foreach (var inst in insts)
            {
                if (inst.Calls(openLetter))
                {
                    inst.opcode = OpCodes.Call;
                    inst.operand = replacement;
                }

                yield return inst;
            }
        }

        static void OpenLetter(Letter letter)
        {
            if (MultifactionLetters.IsVisibleLocally(letter))
            {
                letter.OpenLetter();
                return;
            }

            suppressUI = true;
            try
            {
                letter.OpenLetter();
            }
            finally
            {
                suppressUI = false;
            }
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class SuppressExpiringLetterWindows
    {
        static bool Prefix() => !OpenExpiringLettersForTheirFaction.suppressUI;
    }

    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.SetCurrentTab))]
    static class SuppressExpiringLetterTabSwitch
    {
        static bool Prefix() => !OpenExpiringLettersForTheirFaction.suppressUI;
    }

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

        static bool Prefix(LetterWithTimeout __instance, ref bool __state)
        {
            // The letter is about to be force-shown by LetterStack.LetterStackTick because of expiry
            if (Multiplayer.Ticking
                && __instance.TimeoutActive
                && __instance.disappearAtTick == Find.TickManager.TicksGame + 1)
            {
                if (defaultChoices.TryGetValue(__instance.GetType(), out var tuple))
                {
                    if (tuple.method.IsStatic)
                        tuple.handler(null, __instance);
                    else
                        tuple.handler(__instance);

                    __state = true;
                    return false;
                }

                // Every client force-opens it (the stack is the same everywhere), only show it to the letter's faction.
                // Opening a ChoiceLetter only creates its dialog.
                if (!MultifactionLetters.IsVisibleLocally(__instance))
                {
                    __state = true;
                    return false;
                }
            }

            return true;
        }

        static void Postfix(LetterWithTimeout __instance, bool __state)
        {
            // The letter wasn't opened
            if (__state) return;

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
        public static bool isDrawing;

        static void Prefix() => isDrawing = true;

        static void Postfix() => isDrawing = false;
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.OpenAutomaticLetters))]
    static class DontAutoOpenLettersOnTimeout
    {
        static bool Prefix() => Multiplayer.Client == null;
    }
}
