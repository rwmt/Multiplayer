using HarmonyLib;
using Verse;
using RimWorld;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Notify_PawnEnteringDoor))]
public static class Patch_FogGrid_Faction
{
	static void Prefix(FogGrid __instance, Building_Door door, Pawn pawn)
	{
		FactionContext.Push(pawn.Faction);
	}

	static void Finalizer()
	{
		FactionContext.Pop();
	}
}
