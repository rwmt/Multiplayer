using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Factions;

[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
static class UIRootPrefix
{
    static void Postfix()
    {
        if (Multiplayer.Client != null && Multiplayer.RealPlayerFaction != null && Find.FactionManager != null)
            Find.FactionManager.ofPlayer = Multiplayer.RealPlayerFaction;

        Layouter.BeginFrame();

        if (Multiplayer.Client != null &&
            Multiplayer.RealPlayerFaction == Multiplayer.WorldComp.spectatorFaction &&
            !TickPatch.Simulating &&
            !Multiplayer.IsReplay &&
            Find.WindowStack.WindowOfType<Page>() == null
        )
            Find.WindowStack.ImmediateWindow(
                "MpWindowFaction".GetHashCode(),
                new Rect(0, UI.screenHeight / 2f - 400 / 2f, 300, 350),
                WindowLayer.Super,
                () =>
                {
                    FactionSidebar.DrawFactionSidebar(new Rect(0, 0, 300, 350).ContractedBy(15));
                }
            );
    }
}
