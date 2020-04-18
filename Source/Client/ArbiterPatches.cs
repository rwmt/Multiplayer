using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch]
    static class GUISkinArbiter_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GUI), "get_" + nameof(GUI.skin));
        }

        static bool Prefix(ref GUISkin __result)
        {
            if (!MultiplayerMod.arbiterInstance) return true;
            __result = ScriptableObject.CreateInstance<GUISkin>();
            return false;
        }
    }

    [HarmonyPatch]
    static class RenderTextureCreatePatch
    {
        static MethodInfo IsCreated = AccessTools.Method(typeof(RenderTexture), "IsCreated");
        static FieldInfo ArbiterField = AccessTools.Field(typeof(MultiplayerMod), nameof(MultiplayerMod.arbiterInstance));

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions));
            yield return AccessTools.Method(typeof(PortraitsCache), nameof(PortraitsCache.Get));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == IsCreated)
                {
                    yield return new CodeInstruction(OpCodes.Ldsfld, ArbiterField);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    [HarmonyPatch]
    static class CancelForArbiter
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WaterInfo), nameof(WaterInfo.SetTextures));
            yield return AccessTools.Method(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions));
            yield return AccessTools.Method(typeof(Prefs), nameof(Prefs.Save));
            yield return AccessTools.Method(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode));
            yield return AccessTools.Method(typeof(Section), nameof(Section.RegenerateAllLayers));
            yield return AccessTools.Method(typeof(Section), nameof(Section.RegenerateLayers));
            yield return AccessTools.Method(typeof(SectionLayer), nameof(SectionLayer.DrawLayer));
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapUpdate));
            yield return AccessTools.Method(typeof(GUIStyle), nameof(GUIStyle.CalcSize));
        }

        static bool Prefix() => !MultiplayerMod.arbiterInstance;
    }

    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    static class CancelWorldRendererCtor
    {
        static bool Prefix() => !MultiplayerMod.arbiterInstance;

        static void Postfix(WorldRenderer __instance)
        {
            if (MultiplayerMod.arbiterInstance)
                __instance.layers = new List<WorldLayer>();
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LetterStackUpdate))]
    static class CloseLetters
    {
        static void Postfix(LetterStack __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!TickPatch.Skipping && !MultiplayerMod.arbiterInstance) return;

            for (int i = __instance.letters.Count - 1; i >= 0; i--)
            {
                var letter = __instance.letters[i];
                if (letter is ChoiceLetter choice && choice.Choices.Any(c => c.action?.Method == choice.Option_Close.action.Method) && Time.time - letter.arrivalTime > 4)
                    __instance.RemoveLetter(letter);
            }
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    static class ArbiterLongEventPatch
    {
        static void Postfix()
        {
            if (MultiplayerMod.arbiterInstance && LongEventHandler.currentEvent != null)
                LongEventHandler.currentEvent.alreadyDisplayed = true;
        }
    }
}
