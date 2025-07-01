using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
static class MakeCancelPsychicRitualButtonRed
{
    static void Prefix(string label, ref bool __state)
    {
        if (PsychicRitualBeginProxy.drawing == null) return;
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
            PsychicRitualBeginProxy.drawing.Session?.Remove();
            __result = Widgets.DraggableResult.Idle;
        }
    }
}

[HarmonyPatch(typeof(PsychicRitualGizmo), nameof(PsychicRitualGizmo.InitializePsychicRitual))]
static class CancelDialogBeginPsychicRitual
{
    static bool Prefix(PsychicRitualDef_InvocationCircle psychicRitualDef, Thing target)
    {
        if (Multiplayer.Client == null)
            return true;

        PsychicRitualSession.OpenOrCreateSession(psychicRitualDef, target);
        return false;
    }
}
