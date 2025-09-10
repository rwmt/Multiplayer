using HarmonyLib;
using Verse;
using Multiplayer.API;
using System.Collections.Generic;
using RimWorld;

namespace Multiplayer.Client;

// Fixing "slaves unattended" warning when other faction has slaves
[HarmonyPatch(typeof(SlaveRebellionUtility), nameof(SlaveRebellionUtility.IsUnattendedByColonists))]
public static class Patch_SlaveRebellionUtility_IsUnattendedByColonists
{
	public static bool Prefix(Map map, ref bool __result)
	{
		if (!MP.IsInMultiplayer) return true;

		foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
		{
			if (slave.Faction == Faction.OfPlayer)
			{
				foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
				{
					if (!pawn.IsSlave && !pawn.Downed && !pawn.Dead)
					{
						// Player faction free pawn can attend the slaves
						__result = false;
						return false;
					}
				}
				// Player faction slaves unattended
				__result = true;
				return false;
			}
		}
		// No player faction slaves
		__result = false;
		return false;
	}
}

// Also fix for the "Alert_SlavesUnsuppressed" as it doesnt check faction
[HarmonyPatch(typeof(Alert_SlavesUnsuppressed), nameof(Alert_SlavesUnsuppressed.Targets), MethodType.Getter)]
public static class Patch_Alert_SlavesUnsuppressed_Targets
{
	public static bool Prefix(ref List<Pawn> __result)
	{
		if (!MP.IsInMultiplayer) return true;

		var result = new List<Pawn>();

		foreach (var map in Find.Maps)
		{
			foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
			{
				if (pawn.IsSlave &&
					!pawn.Suspended &&
					pawn.Faction == Faction.OfPlayer &&
					pawn.needs.TryGetNeed(out Need_Suppression need_Suppression) &&
					need_Suppression.IsHigh)
				{
					result.Add(pawn);
				}
			}
		}

		__result = result;
		return false;
	}

}
