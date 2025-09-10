using HarmonyLib;
using Verse;
using RimWorld;
using Multiplayer.API;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Notify_PawnEnteringDoor))]
public static class Patch_FogGrid_Notify_PawnEnteringDoor
{
	public static bool Prefix(FogGrid __instance, Building_Door door, Pawn pawn)
	{
		if (!MP.IsInMultiplayer) return true;

		if (IsPlayerControlledFaction(pawn.Faction) || IsPlayerControlledFaction(pawn.HostFaction))
			__instance.FloodUnfogAdjacent(door.Position, false);

		return false;
	}

	private static bool IsPlayerControlledFaction(Faction faction)
	{
		if (faction == null) return false;

		// Vanilla single-player
		if (faction == Faction.OfPlayer) return true;

		// Multiplayer: works for the original host faction FOR SURE, need more testing for clients
		return faction.IsPlayer;
	}

}
