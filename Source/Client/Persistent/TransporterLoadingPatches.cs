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
                TransporterLoading loading = comp.CreateTransporterLoadingSession(transporters);
                if (TickPatch.currentExecutingCmdIssuedBySelf)
                    loading.OpenWindow();
            }
        }
    }
}
