using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client;

// Set of patches that forces events to play for a correct player on a correct map

// First patch is for the "SettlementDefeatUtility.IsDefeated", in vanilla it's purpose
// is to fire events on non-settled enemy faction bases, that player had defeated. 
// But it doesn account situation where "enemy base" is actually an other player base,
// who is friendly with you. Which (I assume) is the most common situation for MP.

[HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.IsDefeated), typeof(Map), typeof(Faction))]
public static class Patch_SettlementDefeatUtility_IsDefeated
{
	static bool Prefix(Faction faction, ref bool __result)
	{
		// We run original if's not MP, or faction owning the map is not any player
		if (!MP.IsInMultiplayer || !faction.IsPlayer)
			return true;

		// We skip checking if "enemy" is defeated on any player base (beacuse other players could be friendly)
		__result = false;
		return false;
	}
}

// Second patch forbids from firing an event on an incorrect map.
// It's uses mostly vanilla logic for incorrect event by sending th "true"
// as an output for shutdowning events. I left the debug log line to see how many 
// incorrect events are "lost", but game storyteller should understand correctly
// that these events were not fired, and will adapt by firing more (correct) events.

[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
public static class Patch_IncidentWorker_TryExecute_ForMultifactionTriggering
{
	static bool Prefix(IncidentParms parms, ref bool __result)
	{
		// We make some sanity checks and assume, that empty (without any colonists)
		// factionless maps (such as dungeones) are impossible and instantly abandoned.
		if (!MP.IsInMultiplayer ||
			parms.target is not Map map ||
			map.ParentFaction == Faction.OfPlayer ||
			map.mapPawns.AnyColonistSpawned)
		{
			//Log.Message($"[Multiplayer] Incident greenlit");
			return true;
		}

		// Skip incidents if we couldn't do it
		Log.Warning($"[Multiplayer] Incident shutdown on a {map}");
		__result = true;
		return false;
	}
}

// This is (currently) purely debug patch. I used it to see for which faction
// event was supposed to fire (and thus for whitch faction evetn letter will arrive).
// I will leave its here just in case. It could be useful, as some event keep firing
// for the "Spectator" player, but I didn't notice any gameplay effects from that.

//[HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string), typeof(int), typeof(bool))]
//static class LetterStackReceiveFactionDebug
//{
//	// todo the letter might get culled from the archive if it isn't in the stack and Sync depends on the archive
//	static void Prefix()
//	{
//		Log.Message($"[StkMPPatch] Current Incident Faction: {Faction.OfPlayer}");
//	}
//}