using HarmonyLib;
using RimWorld.Planet;

namespace Multiplayer.Client.Patches;

public static class WorldObjectMethodPatches
{
    [HarmonyPriority(MpPriority.MpFirst)]
    public static void Prefix(WorldObject __instance)
    {
        if (Multiplayer.Client == null) return;

        if (__instance.def.canHaveFaction)
            FactionContext.Push(__instance.Faction);
    }

    [HarmonyPriority(MpPriority.MpLast)]
    public static void Finalizer(WorldObject __instance)
    {
        if (Multiplayer.Client == null) return;

        if (__instance.def.canHaveFaction)
            FactionContext.Pop();
    }
}
