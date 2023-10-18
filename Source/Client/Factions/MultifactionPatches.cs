using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches;

[HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.GetGizmos))]
static class DisableDraftGizmo
{
    static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn_DraftController __instance)
    {
        return __instance.pawn.Faction == Faction.OfPlayer ? gizmos : Enumerable.Empty<Gizmo>();
    }
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
static class PawnChangeRelationGizmo
{
    static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
    {
        foreach (var gizmo in gizmos)
            yield return gizmo;

        if (Multiplayer.Client == null || Multiplayer.RealPlayerFaction == Multiplayer.WorldComp.spectatorFaction)
            yield break;

        if (__instance.Faction is { IsPlayer: true } &&__instance.Faction != Faction.OfPlayer)
        {
            var otherFaction = __instance.Faction;

            yield return new Command_Action
            {
                defaultLabel = "Change faction relation",
                icon = MultiplayerStatic.ChangeRelationIcon,
                action = () =>
                {
                    List<FloatMenuOption> list = new List<FloatMenuOption>();
                    for (int i = 0; i <= 2; i++)
                    {
                        var kind = (FactionRelationKind)i;
                        list.Add(new FloatMenuOption(kind.ToString(), () => { SetFactionRelation(otherFaction, kind); }));
                    }

                    Find.WindowStack.Add(new FloatMenu(list));
                }
            };
        }
    }

    [SyncMethod]
    static void SetFactionRelation(Faction other, FactionRelationKind kind)
    {
        Faction.OfPlayer.SetRelation(new FactionRelation(other, kind));
    }
}

[HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
static class CheckDefeatedPatch
{
    static bool Prefix(Settlement factionBase)
    {
        return factionBase.Faction is not { IsPlayer: true };
    }
}

[HarmonyPatch(typeof(MapParent), nameof(MapParent.CheckRemoveMapNow))]
static class CheckRemoveMapNowPatch
{
    static bool Prefix(MapParent __instance)
    {
        return __instance.Faction is not { IsPlayer: true };
    }
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.Attackable), MethodType.Getter)]
static class SettlementAttackablePatch
{
    static bool Prefix(Settlement __instance) => __instance.Faction is not { IsPlayer: true };
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.Material), MethodType.Getter)]
static class SettlementNullFactionPatch1
{
    static bool Prefix(Settlement __instance, ref Material __result)
    {
        if (__instance.factionInt == null)
        {
            __result = BaseContent.BadMat;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.ExpandingIcon), MethodType.Getter)]
static class SettlementNullFactionPatch2
{
    static bool Prefix(Settlement __instance, ref Texture2D __result)
    {
        if (__instance.factionInt == null)
        {
            __result = BaseContent.BadTex;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(GoodwillSituationManager), nameof(GoodwillSituationManager.GetNaturalGoodwill))]
static class GetNaturalGoodwillPatch
{
    static bool Prefix(Faction other)
    {
        return other is not { IsPlayer: true };
    }
}

[HarmonyPatch(typeof(GoodwillSituationManager), nameof(GoodwillSituationManager.GetMaxGoodwill))]
static class GetMaxGoodwillPatch
{
    static bool Prefix(Faction other)
    {
        return other is not { IsPlayer: true };
    }
}

[HarmonyPatch(typeof(ScenPart_StartingAnimal), nameof(ScenPart_StartingAnimal.PetWeight))]
static class StartingAnimalPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        var playerFactionField = AccessTools.Field(typeof(GameInitData), nameof(GameInitData.playerFaction));
        var factionOfPlayer = FactionOfPlayer;

        foreach (var inst in insts)
        {
            yield return inst;

            if (inst.operand == playerFactionField)
                yield return new CodeInstruction(OpCodes.Call, factionOfPlayer.Method);
        }
    }

    static Faction FactionOfPlayer(Faction f)
    {
        return Faction.OfPlayer;
    }
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.IsColonist), MethodType.Getter)]
static class PawnIsColonistPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        var isPlayerMethodGetter = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.IsPlayer));
        var factionIsPlayer = FactionIsPlayer;

        foreach (var inst in insts)
        {
            if (inst.operand == isPlayerMethodGetter)
                inst.operand = factionIsPlayer.Method;

            yield return inst;
        }
    }

    static bool FactionIsPlayer(Faction f)
    {
        return f == Find.FactionManager.OfPlayer;
    }
}

[HarmonyPatch(typeof(GetOrGenerateMapUtility), nameof(GetOrGenerateMapUtility.GetOrGenerateMap), new []{ typeof(int), typeof(IntVec3), typeof(WorldObjectDef) })]
static class MapGenFactionPatch
{
    static void Prefix(int tile)
    {
        var mapParent = Find.WorldObjects.MapParentAt(tile);
        if (Multiplayer.Client != null && mapParent == null)
            Log.Warning($"Couldn't set the faction context for map gen at {tile}: no world object");

        FactionContext.Push(mapParent?.Faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter), new[] { typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool) })]
static class CaravanEnterFactionPatch
{
    static void Prefix(Caravan caravan)
    {
        FactionContext.Push(caravan.Faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
static class WealthRecountFactionPatch
{
    static void Prefix(WealthWatcher __instance)
    {
        FactionContext.Push(__instance.map.ParentFaction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents))]
static class ConfigureStartingPawns_DoWindowContents_Patch
{
    static void Prefix(ref ProgramState __state)
    {
        __state = Current.ProgramState;
        Current.programStateInt = ProgramState.Entry;
    }

    static void Finalizer(ProgramState __state)
    {
        Current.programStateInt = __state;
    }
}

[HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.RandomizeCurPawn))]
static class ConfigureStartingPawns_RandomizeCurPawn_Patch
{
    static void Prefix(ref ProgramState __state)
    {
        __state = Current.ProgramState;
        Current.programStateInt = ProgramState.Entry;
    }

    static void Finalizer(ProgramState __state)
    {
        Current.programStateInt = __state;
    }
}

[HarmonyPatch(typeof(LifeStageWorker_HumanlikeAdult), nameof(LifeStageWorker_HumanlikeAdult.Notify_LifeStageStarted))]
static class LifeStageWorker_Patch
{
    static bool Prefix()
    {
        // Corresponds to "Current.ProgramState == ProgramState.Playing" check in Notify_LifeStageStarted
        return !ScribeUtil.loading;
    }
}

[HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.GetGenerationRequest))]
static class StartingPawnUtility_GetGenerationRequest_Patch
{
    static void Postfix(ref PawnGenerationRequest __result)
    {
        if (Multiplayer.Client != null)
            __result.CanGeneratePawnRelations = false;
    }
}

[HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.DefaultStartingPawnRequest), MethodType.Getter)]
static class StartingPawnUtility_DefaultStartingPawnRequest_Patch
{
    static void Postfix(ref PawnGenerationRequest __result)
    {
        if (Multiplayer.Client != null)
            __result.CanGeneratePawnRelations = false;
    }
}
