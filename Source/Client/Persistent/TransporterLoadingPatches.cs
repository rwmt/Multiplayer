using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static Verse.Widgets;

namespace Multiplayer.Client.Persistent
{
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
    static class MakeCancelLoadingButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (TransporterLoadingProxy.drawing == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        static void Postfix(bool __state, ref DraggableResult __result)
        {
            if (!__state) return;

            GUI.color = Color.white;
            if (__result.AnyPressed())
            {
                TransporterLoadingProxy.drawing.Session?.Remove();
                __result = DraggableResult.Idle;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
    static class LoadPodsHandleReset
    {
        static void Prefix(string label, ref bool __state)
        {
            if (TransporterLoadingProxy.drawing == null) return;
            if (label != "ResetButton".Translate()) return;

            __state = true;
        }

        static void Postfix(bool __state, ref DraggableResult __result)
        {
            if (!__state) return;

            if (__result.AnyPressed())
            {
                TransporterLoadingProxy.drawing.Session?.Reset();
                __result = DraggableResult.Idle;
            }
        }
    }

    [HarmonyPatch]
    static class CancelAddItems
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.AddToTransferables));
            yield return AccessTools.Method(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.SetLoadedItemsToLoad));
        }

        static bool Prefix(Dialog_LoadTransporters __instance)
        {
            if (__instance is TransporterLoadingProxy mp && mp.itemsReady)
            {
                // Sets the transferables list back to the session list
                // as it gets reset in CalculateAndRecacheTransferables
                mp.transferables = mp.Session.transferables;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.TryAccept))]
    static class TryAcceptPodsPatch
    {
        static bool Prefix(Dialog_LoadTransporters __instance)
        {
            if (Multiplayer.InInterface && __instance is TransporterLoadingProxy dialog)
            {
                dialog.Session?.TryAccept();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.DebugTryLoadInstantly))]
    static class DebugTryLoadInstantlyPatch
    {
        static bool Prefix(Dialog_LoadTransporters __instance)
        {
            if (Multiplayer.InInterface && __instance is TransporterLoadingProxy dialog)
            {
                dialog.Session?.DebugTryLoadInstantly();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogLoadTransporters
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.MapContext != null && window.GetType() == typeof(Dialog_LoadTransporters))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(Map), typeof(List<CompTransporter>) })]
    static class DialogLoadTransportersCtorPatch
    {
        static void Prefix(Dialog_LoadTransporters __instance, Map map, List<CompTransporter> transporters)
        {
            if (__instance.GetType() != typeof(Dialog_LoadTransporters))
                return;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                var comp = map.MpComp();
                TransporterLoading loading = comp.CreateTransporterLoadingSession(Faction.OfPlayer, transporters);
                if (TickPatch.currentExecutingCmdIssuedBySelf)
                    loading.OpenWindow();
            }
        }
    }

    [HarmonyPatch()]
    static class DisableTransferCheckboxForOtherFactions
    {
        static MethodInfo TargetMethod() {
            return typeof(Widgets).GetMethod("Checkbox", [
                typeof(Vector2), typeof(bool).MakeByRefType(), typeof(float), typeof(bool), typeof(bool), typeof(Texture2D), typeof(Texture2D)
            ]);
        }

        static bool Prefix(Vector2 topLeft, bool checkOn, bool disabled)
        {
            if (TransporterLoadingProxy.drawing == null || TransporterLoadingProxy.drawing.Session?.faction == Multiplayer.RealPlayerFaction)
                return true;

            if (disabled)
                return true;

            Widgets.Checkbox(topLeft, ref checkOn, disabled: true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor))]
    static class DisableLoadingControlButtonsForOtherFactions
    {
        static bool Prefix(Rect rect, string label, ref bool __result)
        {
            if (TransporterLoadingProxy.drawing == null || TransporterLoadingProxy.drawing.Session?.faction == Multiplayer.RealPlayerFaction)
                return true;

            if (label != "ResetButton".Translate() && label != "CancelButton".Translate() && label != "AcceptButton".Translate())
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText))]
    [HarmonyPatch(new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor) })]
    static class DisableTransferCountButtonsForOtherFactions
    {
        static bool Prefix(Rect rect, string label, ref bool __result)
        {
            if (TransporterLoadingProxy.drawing == null || TransporterLoadingProxy.drawing.Session?.faction == Multiplayer.RealPlayerFaction)
                return true;

            if (label != "0" && label != "M<" && label != "<<" && label != "<" && label != ">" && label != ">>" && label != ">M")
                return true;

            GUI.color = Widgets.InactiveColor;
            Widgets.TextArea(rect, label, true);
            GUI.color = Color.white;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch()]
    static class DisableTransferCountTextBoxForOtherFactions
    {
        static MethodInfo TargetMethod() {
            return typeof(Widgets).GetMethod("TextFieldNumeric", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(typeof(int));
        }
        static bool Prefix(Rect rect, int val)
        {
            if (TransporterLoadingProxy.drawing == null || TransporterLoadingProxy.drawing.Session?.faction == Multiplayer.RealPlayerFaction)
                return true;

            GUI.color = Color.white;
            Widgets.TextArea(rect, val.ToString(), true);
            return false;
        }
    }
}
