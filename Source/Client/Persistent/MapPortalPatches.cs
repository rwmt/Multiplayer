using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
static class MakeMapPortalCancelButtonRed
{
    static void Prefix(string label, ref bool __state)
    {
        if (MapPortalProxy.drawing == null) return;
        if (label != "CancelButton".Translate()) return;

        GUI.color = new Color(1f, 0.3f, 0.35f);
        __state = true;
    }

    static void Postfix(bool __state, ref Widgets.DraggableResult __result)
    {
        if (!__state) return;

        GUI.color = Color.white;
        if (__result.AnyPressed())
        {
            MapPortalProxy.drawing.Session?.Remove();
            __result = Widgets.DraggableResult.Idle;
        }
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
static class MapPortalHandleReset
{
    static void Prefix(string label, ref bool __state)
    {
        if (MapPortalProxy.drawing == null) return;
        if (label != "ResetButton".Translate()) return;

        __state = true;
    }

    static void Postfix(bool __state, ref Widgets.DraggableResult __result)
    {
        if (!__state) return;

        if (__result.AnyPressed())
        {
            MapPortalProxy.drawing.Session?.Reset();
            __result = Widgets.DraggableResult.Idle;
        }
    }
}

[HarmonyPatch(typeof(Dialog_EnterPortal), nameof(Dialog_EnterPortal.TryAccept))]
static class TryAcceptMapPortal
{
    static bool Prefix(Dialog_EnterPortal __instance)
    {
        if (Multiplayer.InInterface && __instance is MapPortalProxy mapPortal)
        {
            mapPortal.Session?.TryAccept();
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Dialog_EnterPortal), nameof(Dialog_EnterPortal.AddToTransferables))]
static class CancelMapPortalAddItems
{
    static bool Prefix(Dialog_EnterPortal __instance)
    {
        if (__instance is MapPortalProxy { itemsReady: true } mp)
        {
            // Sets the transferables list back to the session list
            // as it gets reset in CalculateAndRecacheTransferables
            mp.transferables = mp.Session.transferables;
            return false;
        }

        return true;
    }
}

static class OpenMapPortalSessionDialog
{
    [MpPrefix(typeof(MapPortal), nameof(MapPortal.GetGizmos), 0)]
    static bool Prefix(MapPortal __instance)
    {
        if (Multiplayer.Client == null)
            return true;

        MapPortalSession.OpenOrCreateSession(__instance);
        return false;
    }
}
