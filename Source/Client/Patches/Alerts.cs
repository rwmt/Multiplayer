using HarmonyLib;
using Verse;
using Multiplayer.API;
using System.Collections.Generic;
using RimWorld;
using System.Linq;

namespace Multiplayer.Client;

// Add faction check to "slaves unattended" warning
[HarmonyPatch(typeof(SlaveRebellionUtility), nameof(SlaveRebellionUtility.IsUnattendedByColonists))]
public static class Patch_SlaveRebellionUtility_IsUnattendedByColonists
{
	public static void Postfix(Map map, ref bool __result)
	{
		if (!MP.IsInMultiplayer) return;

		// If any slave is from player's faction
		__result = __result && map.mapPawns.SlavesOfColonySpawned
			.Any(slave => slave.Faction == Faction.OfPlayer);
	}
}

// Add faction check to "slaves unsuppressed" warning
[HarmonyPatch(typeof(Alert_SlavesUnsuppressed), nameof(Alert_SlavesUnsuppressed.Targets), MethodType.Getter)]
public static class Patch_Alert_SlavesUnsuppressed_Targets
{
	public static void Postfix(ref List<Pawn> __result)
	{
		if (!MP.IsInMultiplayer) return;

		__result = __result.Where(pawn => pawn.Faction == Faction.OfPlayer).ToList();
	}
}

// Fixing "Abandoned baby" warning , often appearing for other players babies, when they are beign fed. 
// You will not get abandoned warning: (baby from other faction without the same faction adult)
// for (other) player owned babies at all, unless it's happened directly on YOUR base.
// Example: someone catapulted their baby on your map. Your own babies get a different set of warning, which works fine.
[HarmonyPatch(typeof(Alert_AbandonedBaby), "AbandonedBabies")]
public static class Patch_Alert_AbandonedBaby_MultifactionWarning
{
	public static void Postfix(ref List<Pawn> __result)
	{
		if (!MP.IsInMultiplayer) return;

		__result = __result.Where(pawn => !pawn.Faction.IsPlayer || pawn.MapHeld.ParentFaction == Faction.OfPlayer).ToList();
	}
}
