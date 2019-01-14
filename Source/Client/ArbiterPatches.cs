using Harmony;
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
    [HarmonyPatch(typeof(GUI), nameof(GUI.skin), MethodType.Getter)]
    static class GUISkinArbiter_Patch
    {
        static bool Prefix(ref GUISkin __result)
        {
            if (!Multiplayer.arbiterInstance) return true;
            __result = ScriptableObject.CreateInstance<GUISkin>();
            return false;
        }
    }

    [MpPatch(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions))]
    [MpPatch(typeof(PortraitsCache), nameof(PortraitsCache.Get))]
    static class RenderTextureCreatePatch
    {
        static MethodInfo IsCreated = AccessTools.Method(typeof(RenderTexture), "IsCreated");
        static FieldInfo ArbiterField = AccessTools.Field(typeof(Multiplayer), nameof(Multiplayer.arbiterInstance));

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

    [MpPatch(typeof(WaterInfo), nameof(WaterInfo.SetTextures))]
    [MpPatch(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions))]
    [MpPatch(typeof(Prefs), nameof(Prefs.Save))]
    [MpPatch(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode))]
    [MpPatch(typeof(Section), nameof(Section.RegenerateAllLayers))]
    [MpPatch(typeof(Section), nameof(Section.RegenerateLayers))]
    [MpPatch(typeof(SectionLayer), nameof(SectionLayer.DrawLayer))]
    [MpPatch(typeof(Map), nameof(Map.MapUpdate))]
    static class CancelForArbiter
    {
        static bool Prefix() => !Multiplayer.arbiterInstance;
    }

    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    static class CancelWorldRendererCtor
    {
        static bool Prefix() => !Multiplayer.arbiterInstance;

        static void Postfix(WorldRenderer __instance)
        {
            if (Multiplayer.arbiterInstance)
                __instance.layers = new List<WorldLayer>();
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LetterStackUpdate))]
    static class CloseLetters
    {
        static void Postfix(LetterStack __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!TickPatch.Skipping && !Multiplayer.arbiterInstance) return;

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
            if (Multiplayer.arbiterInstance && LongEventHandler.currentEvent != null)
                LongEventHandler.currentEvent.alreadyDisplayed = true;
        }
    }
}
