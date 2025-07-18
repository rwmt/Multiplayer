using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch]
    static class GUISkinArbiter_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(GUI), nameof(GUI.skin));
        }

        static bool Prefix(ref GUISkin __result)
        {
            if (!Multiplayer.arbiterInstance) return true;
            __result = ScriptableObject.CreateInstance<GUISkin>();
            return false;
        }
    }

    [HarmonyPatch]
    static class RenderTextureCreatePatch
    {
        static MethodInfo IsCreated = AccessTools.Method(typeof(RenderTexture), nameof(RenderTexture.IsCreated));
        static FieldInfo ArbiterField = AccessTools.Field(typeof(Multiplayer), nameof(Multiplayer.arbiterInstance));

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

                if (inst.operand as MethodInfo == IsCreated)
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
            yield return AccessTools.Method(typeof(Section), nameof(Section.RegenerateDirtyLayers));
            yield return AccessTools.Method(typeof(SectionLayer), nameof(SectionLayer.DrawLayer));
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapUpdate));
            yield return AccessTools.Method(typeof(GUIStyle), nameof(GUIStyle.CalcSize));
        }

        static bool Prefix() => !Multiplayer.arbiterInstance;
    }

    // TODO test if this works.
    [HarmonyPatch(typeof(WorldGrid), (nameof(WorldGrid.InitializeGlobalLayers)))]
    static class NoWorldRenderLayersForArbiter
    {
        static bool Prefix() => !Multiplayer.arbiterInstance;

        static void Postfix(WorldGrid __instance)
        {
            if (Multiplayer.arbiterInstance)
                __instance.globalLayers.Clear();
        }
    }

    // TODO: Test if it works in 1.6, we may need a way to fully prevent the layers from being initialized
    [HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.InitializeLayer))]
    static class NoPlanetRenderLayersForArbiter
    {
        static void Postfix(PlanetLayer __instance)
        {
            if (Multiplayer.arbiterInstance)
                __instance.WorldDrawLayers.Clear();
        }
    }
}
