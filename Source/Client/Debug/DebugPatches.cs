using System;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

#if DEBUG
namespace Multiplayer.Client.Patches
{
    public static class DebugPatches
    {
        public static void Init()
        {
            /*harmony.Patch(
                AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)),
                new HarmonyMethod(typeof(MultiplayerMod), nameof(Prefixfactionman))
            );

            harmony.Patch(
                AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.IsPlayer)),
                new HarmonyMethod(typeof(MultiplayerMod), nameof(Prefixfactionman))
            );*/
        }

        static void Prefixfactionman()
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                string trace = new StackTrace().ToString();
                if (!trace.Contains("SetInitialPsyfocusLevel") &&
                    !trace.Contains("Pawn_NeedsTracker.ShouldHaveNeed") &&
                    !trace.Contains("FactionManager.ExposeData"))
                    Log.Message($"factionman call {trace}", true);
            }
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    static class SetupQuickTestPatch
    {
        public static bool marker;

        static void Prefix() => marker = true;

        static void Postfix()
        {
            Find.GameInitData.mapSize = 250;
            marker = false;
        }
    }

    [HarmonyPatch(typeof(GameInitData), nameof(GameInitData.ChooseRandomStartingTile))]
    static class RandomStartingTilePatch
    {
        static void Postfix()
        {
            if (SetupQuickTestPatch.marker)
            {
                Find.GameInitData.startingTile = 501;
                Find.WorldGrid[Find.GameInitData.startingTile].hilliness = Hilliness.SmallHills;
            }
        }
    }

    [HarmonyPatch(typeof(GenText), nameof(GenText.RandomSeedString))]
    static class GrammarRandomStringPatch
    {
        static void Postfix(ref string __result)
        {
            if (SetupQuickTestPatch.marker)
                __result = "multiplayer";
        }
    }


    [HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))]
    static class GizmoDrawDebugInfo
    {
        static MethodInfo GizmoOnGUI = AccessTools.Method(typeof(Gizmo), nameof(Gizmo.GizmoOnGUI),
            new[] { typeof(Vector2), typeof(float), typeof(GizmoRenderParms) });

        static MethodInfo GizmoOnGUIShrunk = AccessTools.Method(typeof(Command), nameof(Command.GizmoOnGUIShrunk),
            new[] { typeof(Vector2), typeof(float), typeof(GizmoRenderParms) });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == GizmoOnGUI)
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(GizmoDrawDebugInfo), nameof(GizmoOnGUIProxy)));
                else if (inst.operand == GizmoOnGUIShrunk)
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(GizmoDrawDebugInfo), nameof(GizmoOnGUIShrunkProxy)));
                else
                    yield return inst;
            }
        }

        static GizmoResult GizmoOnGUIProxy(Gizmo gizmo, Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            ShowDebugInfo(gizmo, topLeft, maxWidth, parms);
            return gizmo.GizmoOnGUI(topLeft, maxWidth, parms);
        }

        static GizmoResult GizmoOnGUIShrunkProxy(Command cmd, Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            ShowDebugInfo(cmd, topLeft, maxWidth, parms);
            return cmd.GizmoOnGUIShrunk(topLeft, maxWidth, parms);
        }

        static void ShowDebugInfo(Gizmo gizmo, Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            var info = gizmo.GetType().ToString();

            if (gizmo is Command_Action action)
                info += $"\n\n{MpUtil.DelegateMethodInfo(action.action?.Method)}";

            if (gizmo is Command_Toggle toggle)
                info += $"\n\n{MpUtil.DelegateMethodInfo(toggle.toggleAction?.Method)}";

            TooltipHandler.TipRegion(
                new Rect(topLeft, new Vector2(gizmo.GetWidth(maxWidth), 75f)),
                info
            );
        }
    }

    [HarmonyPatch(typeof(FloatMenuOption), nameof(FloatMenuOption.DoGUI))]
    static class FloatMenuDrawDebugInfo
    {
        static void Postfix(FloatMenuOption __instance, Rect rect)
        {
            TooltipHandler.TipRegion(rect, MpUtil.DelegateMethodInfo(__instance.action?.Method));
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(PatchClassProcessor), "ProcessPatchJob")]
    static class HarmonyMeasurePatchTime
    {
        static void Prefix(ref double __state)
        {
            __state = Multiplayer.harmonyWatch.ElapsedMillisDouble();
            Multiplayer.harmonyWatch.Start();
        }

        static void Postfix(object job, double __state)
        {
            Multiplayer.harmonyWatch.Stop();
            //var original = (MethodBase)Traverse.Create(job).Field("original").GetValue();
            //var took = Multiplayer.harmonyWatch.ElapsedMillisDouble() - __state;
            //if (took > 15)
            //    Log.Message($"{took} ms: Patching {original.MethodDesc()}");
        }
    }

    [EarlyPatch]
    [HarmonyPatch]
    static class FixNewlineLogging
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) });
            yield return AccessTools.Method(typeof(Log), nameof(Log.Warning), new[] { typeof(string) });
            yield return AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) });
        }

        static void Prefix(ref string text)
        {
            // On Windows, Debug.Log used by Verse.Log replaces \n with \r\n
            // Without this patch printing \r\n results in \r\r\n
            if (Native.Windows)
                text = text?.Replace("\r\n", "\n");
        }
    }


    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), typeof(Rect), typeof(string))]
    static class HighlightLabels
    {
        static void Prefix(Rect rect)
        {
            if (Input.GetKey(KeyCode.End))
                Widgets.DrawBox(rect);
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll))]
    static class TimeStaticCtors
    {
        static bool Prefix()
        {
            foreach (Type type in GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>())
            {
                DeepProfiler.Start($"Static ctor: {type.FullName}");

                try
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
                catch (Exception ex)
                {
                    Log.Error(string.Concat("Error in static constructor of ", type, ": ", ex));
                }
                finally
                {
                    DeepProfiler.End();
                }
            }

            StaticConstructorOnStartupUtility.coreStaticAssetsLoaded = true;

            return false;
        }
    }

    [EarlyPatch]
    [HarmonyPatch(typeof(GlobalTextureAtlasManager), nameof(GlobalTextureAtlasManager.BakeStaticAtlases))]
    static class NoAtlases
    {
        static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class DontShowSteamMissing
    {
        static bool Prefix(Window window)
        {
            if (window is Dialog_MessageBox msg && msg.text == "SteamClientMissing".Translate())
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(ResolutionUtility), nameof(ResolutionUtility.Update))]
    static class DontUpdateResolution
    {
        // ResolutionUtility.Update changes the resolution when it's too small and saves Prefs
        // This is annoying because it throws an exception when two clients write to the same file
        static bool Prefix() => false;
    }

}
#endif
