using HarmonyLib;
using Verse;
using Multiplayer.API;
using System.Collections.Generic;
using RimWorld;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Multiplayer.Client.Patches;
using UnityEngine;

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

// Need_Food.TicksStarving compares TicksGame with a timestamp of the pawn's own clock (its map's or, in caravans, the
// world's). The alerts run at the current map's time, which with async time made animals on other maps look starving
// (or not) depending on which map is viewed. Interface only: these alerts don't run in the simulation.
[HarmonyPatch]
public static class AnimalHungerAlerts_OwnTime_Patch
{
	static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.PropertyGetter(typeof(Alert_StarvationAnimals), nameof(Alert_StarvationAnimals.StarvingAnimals));
		yield return AccessTools.Method(typeof(Alert_PennedAnimalHungry), nameof(Alert_PennedAnimalHungry.CalculateTargets));
		yield return AccessTools.Method(typeof(Alert_HitchedAnimalHungryNoFood), nameof(Alert_HitchedAnimalHungryNoFood.CalculateTargets));
	}

	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase original)
	{
		var ticksStarving = AccessTools.PropertyGetter(typeof(Need_Food), nameof(Need_Food.TicksStarving));
		var replacement = AccessTools.Method(typeof(AnimalHungerAlerts_OwnTime_Patch), nameof(TicksStarving));
		var replaced = false;

		foreach (var ci in insts)
		{
			if (ci.Calls(ticksStarving))
			{
				ci.opcode = OpCodes.Call;
				ci.operand = replacement;
				replaced = true;
			}

			yield return ci;
		}

		if (!replaced)
			Log.Error($"Multiplayer: Need_Food.TicksStarving not found in {original.DeclaringType?.Name}.{original.Name}");
	}

	static int TicksStarving(Need_Food food)
	{
		if (Multiplayer.Client == null || !Multiplayer.InInterface)
			return food.TicksStarving;

		return Mathf.Max(0, StatWorkerGetValuePatch.TicksGameFor(Find.TickManager.TicksGame, food.pawn) - food.lastNonStarvingTick);
	}
}
