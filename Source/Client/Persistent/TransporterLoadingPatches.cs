using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent
{
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
    static class MakeCancelLoadingButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (TransporterLoadingProxy.drawing == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        static void Postfix(bool __state, ref bool __result)
        {
            if (!__state) return;

            GUI.color = Color.white;
            if (__result)
            {
                TransporterLoadingProxy.drawing.Session?.Remove();
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
    static class LoadPodsHandleReset
    {
        static void Prefix(string label, ref bool __state)
        {
            if (TransporterLoadingProxy.drawing == null) return;
            if (label != "ResetButton".Translate()) return;

            __state = true;
        }

        static void Postfix(bool __state, ref bool __result)
        {
            if (!__state) return;

            if (__result)
            {
                TransporterLoadingProxy.drawing.Session?.Reset();
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    static class CancelAddItems
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.AddPawnsToTransferables));
            yield return AccessTools.Method(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.AddItemsToTransferables));
        }

        static bool Prefix(Dialog_LoadTransporters __instance)
        {
            if (__instance is TransporterLoadingProxy mp && mp.itemsReady)
            {
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
            if (Multiplayer.ShouldSync && __instance is TransporterLoadingProxy dialog)
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
            if (Multiplayer.ShouldSync && __instance is TransporterLoadingProxy dialog)
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
    static class CancelDialogLoadTransportersCtor
    {
        static bool Prefix(Dialog_LoadTransporters __instance, Map map, List<CompTransporter> transporters)
        {
            if (__instance.GetType() != typeof(Dialog_LoadTransporters))
                return true;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                var comp = map.MpComp();
                TransporterLoading loading = comp.CreateTransporterLoadingSession(transporters);
                if (TickPatch.currentExecutingCmdIssuedBySelf)
                    loading.OpenWindow();
                return true;
            }

            return true;
        }
    }

    [HarmonyPatch]
    static class TransporterContents_DiscardToLoad
    {
        static MethodBase TargetMethod()
        {
            List<Type> nestedPrivateTypes = new List<Type>(typeof(ITab_ContentsTransporter).GetNestedTypes(BindingFlags.NonPublic));

            Type cType = nestedPrivateTypes.Find(t => t.Name.Equals("<>c__DisplayClass11_0"));

            return AccessTools.Method(cType, "<DoItemsLists>b__0");
        }

        static bool Prefix(int x)
        {
            if (Multiplayer.Client == null) return true;
            Messages.Message("MpNotAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
