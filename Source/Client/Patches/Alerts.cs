using HarmonyLib;
using Verse;
using System.Collections.Generic;
using RimWorld;
using System.Linq;

namespace Multiplayer.Client;

// Add faction check to "slaves unattended" warning
[HarmonyPatch(typeof(SlaveRebellionUtility), nameof(SlaveRebellionUtility.IsUnattendedByColonists))]
public static class SlaveRebellionUtility_IsUnattendedByColonists_Patch
{
	[HarmonyPostfix]
	public static void Postfix(Map map, ref bool __result)
	{
		if (Multiplayer.Client == null) return;

		// If any slave is from player's faction
		__result = __result && map.mapPawns.SlavesOfColonySpawned
			.Any(slave => slave.Faction == Faction.OfPlayer);
	}
}

// Add faction check to "slaves unsuppressed" warning
[HarmonyPatch(typeof(Alert_SlavesUnsuppressed), nameof(Alert_SlavesUnsuppressed.Targets), MethodType.Getter)]
public static class Alert_SlavesUnsuppressed_Targets_Patch
{
	[HarmonyPostfix]
	public static void Postfix(ref List<Pawn> __result)
	{
		if (Multiplayer.Client == null) return;

		__result = __result.Where(pawn => pawn.Faction == Faction.OfPlayer).ToList();
	}
}

// Fixing "Abandoned baby" warning , often appearing for other players babies, when they are beign fed. 
// You will not get abandoned warning: (baby from other faction without the same faction adult)
// for (other) player owned babies at all, unless it's happened directly on YOUR base.
// Example: someone catapulted their baby on your map. Your own babies get a different set of warning, which works fine.
[HarmonyPatch(typeof(Alert_AbandonedBaby), nameof(Alert_AbandonedBaby.AbandonedBabies))]
public static class Alert_AbandonedBaby_AbandonedBabies_Patch
{
	[HarmonyPostfix]
	public static void Postfix(ref List<Pawn> __result)
	{
		if (Multiplayer.Client == null) return;

		__result = __result.Where(pawn => !pawn.Faction.IsPlayer || pawn.MapHeld.ParentFaction == Faction.OfPlayer).ToList();
	}

}

// Vanilla uses (mostly) "needs.food.TicksStarving" to determine
// if animals are hungry/starving. Which in async results in
// constant warnings, when viewed from another, higher tick map.
// We ensure that animals in the list are actually starving.
[HarmonyPatch(typeof(Alert_StarvationAnimals), nameof(Alert_StarvationAnimals.StarvingAnimals), MethodType.Getter)]
public static class Alert_StarvationAnimals_StarvingAnimals_Patch
{
	[HarmonyPostfix]
	public static void Postfix(Alert_StarvationAnimals __instance, ref List<Pawn> __result)
	{
		if (Multiplayer.Client == null || !Multiplayer.GameComp.asyncTime)
			return;

		for (int i = __instance.starvingAnimalsResult.Count - 1; i >= 0; i--)
		{
			Pawn animal = __instance.starvingAnimalsResult[i];

			if (!animal.needs.food.Starving)
				__instance.starvingAnimalsResult.RemoveAt(i);
		}
		
		__result = __instance.starvingAnimalsResult;
	}

}

// Exact same idea as in "Patch_Alert_StarvationAnimals",
// but this class is written slightly differently
[HarmonyPatch(typeof(Alert_PennedAnimalHungry), nameof(Alert_PennedAnimalHungry.CalculateTargets))]
public static class Alert_PennedAnimalHungry_CalculateTargets_Patch
{
	[HarmonyPostfix]
	public static void Postfix(Alert_PennedAnimalHungry __instance)
	{
		if (Multiplayer.Client == null || !Multiplayer.GameComp.asyncTime)
			return;

		for (int i = __instance.targets.Count - 1; i >= 0; i--)
		{
			Pawn animal = __instance.targets[i].Pawn;

			if (!animal.needs.food.Starving)
			{
				__instance.targets.RemoveAt(i);
				__instance.pawnNames.RemoveAt(i);
			}

		}

	}

}
