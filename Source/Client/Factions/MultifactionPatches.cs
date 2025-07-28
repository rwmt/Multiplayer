using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Factions;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static HarmonyLib.AccessTools;

namespace Multiplayer.Client.Patches;

[HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.DoRow))]
public static class MainTabWindow_QuestsDoRowPatch
{
    public static void Prefix(ref Rect rect, Quest quest)
    {
        if(Multiplayer.Client != null && !Multiplayer.settings.hideOtherPlayersQuests)
        {
            Faction playerFaction;
            if (quest.TryGetPlayerFaction(out playerFaction))
            {
                Rect iconRect = new Rect(rect.x + 2f, rect.y + 2f, 4f, rect.height - 4f);
                Widgets.DrawBoxSolid(iconRect, playerFaction.Color);
                rect.xMin += 8f;
                TooltipHandler.TipRegion(rect,"MpQuestDesc".Translate(playerFaction.Name, playerFaction == Faction.OfPlayer ? ". (you)" : "."));
            }
            else
            {
                TooltipHandler.TipRegion(rect, "MpPublicQuest".Translate());
            }
        }
    }
}

[HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.ShouldListNow))]
public static class MainTabWindow_QuestsShouldListNowPatch
{
    static bool Prefix(Quest quest, ref bool __result)
    {
        if (Multiplayer.Client != null && Multiplayer.settings.hideOtherPlayersQuests)
        {
            Faction playerFaction;
            if (quest.TryGetPlayerFaction(out playerFaction) && playerFaction != Faction.OfPlayer)
            {
                __result = false;
                return false;
            }
        }
        return true;
    }
}

[HarmonyPatch(typeof(WorldObject), nameof(WorldObject.ShowRelatedQuests), MethodType.Getter)]
static class WorldObjectShowRelatedQuestsPatch
{
    static void Postfix(ref bool __result)
    {
        if (Multiplayer.Client != null && Multiplayer.RealPlayerFaction == Multiplayer.WorldComp.spectatorFaction)
            __result = false;
    }
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.ShowRelatedQuests), MethodType.Getter)]
static class SettlementShowRelatedQuestsPatch
{
    static void Postfix(ref bool __result)
    {
        if (Multiplayer.Client != null && Multiplayer.RealPlayerFaction == Multiplayer.WorldComp.spectatorFaction)
            __result = false;
    }
}

[HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.PaneTopY), MethodType.Getter)]
static class WorldInspectPanePaneTopYPatch
{
    static void Postfix(ref float __result)
    {
        if (Multiplayer.Client != null && Multiplayer.RealPlayerFaction == Multiplayer.WorldComp.spectatorFaction)
            __result += 35f;
    }
}

[HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.DoRewardsPrefsButton))]
public static class MainTabWindow_QuestsDoRewardsPrefsButtonPatch
{
    public static bool Prefix(MainTabWindow_Quests __instance, Rect rect, out Rect __result)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
        {
            __result = rect;
            return true;
        }
        rect.yMin = rect.yMax - 40f;

        float buttonHeight = 40f;

        float rewardButtonWidth = rect.width * 0.5f - 5f;
        float toggleButtonWidth = rect.width - rewardButtonWidth - 10f;

        Rect rewardRect = new Rect(rect.x, rect.yMax - buttonHeight, rewardButtonWidth, buttonHeight);
        Text.Font = GameFont.Small;
        if (Widgets.ButtonText(rewardRect, "ChooseRewards".Translate(), true, true, true))
        {
            Find.WindowStack.Add(new Dialog_RewardPrefsConfig());
        }

        Rect toggleRect = new Rect(rewardRect.xMax + 10f, rewardRect.y, toggleButtonWidth, buttonHeight);
        bool value = Multiplayer.settings.hideOtherPlayersQuests;
        string label = value ? "MpHideQuestsOn".Translate() : "MpHideQuestsOff".Translate();
        if (Widgets.ButtonText(toggleRect, label, true, true))
        {
            Multiplayer.settings.hideOtherPlayersQuests = !value;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }
        TooltipHandler.TipRegion(toggleRect, "MpHideQuestsDesc".Translate());

        __result = rect;
        return false;
    }
}

[HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.CheckRecacheEntries))]
public static class ColonistBarCheckRecacheEntriesPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        var findMapsGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.Maps));
        foreach (var inst in insts)
        {
            if (inst.Calls(findMapsGetter))
            {
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(ColonistBarCheckRecacheEntriesPatch), nameof(GetFilteredMaps)));
            }
            else
            {
                yield return inst;
            }
        }
    }

    public static IEnumerable<Map> GetFilteredMaps()
    {
        if (Multiplayer.Client == null || !Multiplayer.settings.hideOtherPlayersInColonistBar)
            return Find.Maps;

        return Find.Maps.Where(map =>
            map.mapPawns.FreeColonistsSpawned.Any() ||
            map.Parent?.Faction == Faction.OfPlayer);
    }
}

[HarmonyPatch(typeof(WorldRoutePlanner), nameof(WorldRoutePlanner.DoRoutePlannerButton))]
public static class WorldRoutePlannerPatch
{
    static bool Prefix()
    {
        return Multiplayer.Client == null || Multiplayer.RealPlayerFaction != Multiplayer.WorldComp.spectatorFaction;
    }
}

[HarmonyPatch(typeof(WorldGizmoUtility), nameof(WorldGizmoUtility.TryGetCaravanGizmo))]
public static class WorldGizmoUtilityTryGetCaravanGizmoPatch
{
    static bool Prefix(ref Gizmo gizmo, ref bool __result)
    {
        if (Multiplayer.Client != null && Multiplayer.RealPlayerFaction == Multiplayer.WorldComp.spectatorFaction)
        {
            gizmo = null;
            __result = false;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.DoButtons))]
static class MainButtonsRootDoButtonsPatch
{
    private static bool IsSpectator =>
      Multiplayer.Client != null &&
      Multiplayer.RealPlayerFaction == Multiplayer.WorldComp.spectatorFaction;

    private static readonly string[] SpectatorButtons = { "Menu", "World" };

    private static readonly FieldRef<MainButtonsRoot, List<MainButtonDef>> AllButtonsRef = AccessTools.FieldRefAccess<MainButtonsRoot, List<MainButtonDef>>("allButtonsInOrder");

    private static readonly Dictionary<string, int> SpectatorButtonOrder = SpectatorButtons.Select((name, idx) => new { name, idx }).ToDictionary(x => x.name, x => x.idx);

    [HarmonyPrefix]
    private static bool ReplaceOriginalDrawing(MainButtonsRoot __instance)
    {
        if (!IsSpectator)
            return true;

        try
        {
            var allButtons = AllButtonsRef(__instance);
            if (allButtons == null)
                return true;

            var toDraw = SpectatorButtons
                .Select(name => allButtons.Find(b => b.defName == name))
                .Where(b => b?.Worker.Visible ?? false)
                .ToList();

            DrawCornerButtons(toDraw);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"[MainButtonsRootPatch] ReplaceOriginalDrawing failed: {ex}");
            return true;
        }
    }

    [HarmonyPostfix]
    private static void FilterButtons(MainButtonsRoot __instance)
    {
        try
        {
            var allButtons = AllButtonsRef(__instance);
            if (allButtons == null)
                return;

            if (IsSpectator)
            {
                // Keep only SpectatorButtons, sorted by our map
                var filtered = allButtons
                    .Where(b => SpectatorButtonOrder.ContainsKey(b.defName))
                    .OrderBy(b => SpectatorButtonOrder[b.defName])
                    .ToList();

                AllButtonsRef(__instance) = filtered;
            }
            else
            {
                // Restore original full list in normal mode
                var original = DefDatabase<MainButtonDef>.AllDefsListForReading
                    .Where(b => b.order >= 0)
                    .OrderBy(b => b.order)
                    .ToList();

                AllButtonsRef(__instance) = original;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[MainButtonsRootPatch] FilterButtons failed: {ex}");
        }
    }

    private static void DrawCornerButtons(List<MainButtonDef> buttons)
    {
        const float buttonHeight = 35f;
        const float defaultPadding = 20f;
        const float spacing = 0f;

        float x = UI.screenWidth;
        float y = UI.screenHeight - buttonHeight;

        foreach (var btn in buttons)
        {
            float padding = btn.defName == "World" ? 120f : defaultPadding;
            float width = btn.ShortenedLabelCapWidth + padding;

            x -= width;
            btn.Worker.DoButton(new Rect(x, y, width, buttonHeight));
            x -= spacing;
        }
    }
}


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
                    List<FloatMenuOption> list = new List<FloatMenuOption>
                    {
                        new(FactionRelationKind.Hostile.ToString(), () =>
                        {
                            SetRelation(otherFaction, FactionRelationKind.Hostile);
                        }),
                        new(FactionRelationKind.Neutral.ToString(), () =>
                        {
                            SetRelation(otherFaction, FactionRelationKind.Neutral);
                        })
                    };

                    Find.WindowStack.Add(new FloatMenu(list));
                }
            };
        }
    }

    [SyncMethod]
    static void SetRelation(Faction other, FactionRelationKind kind)
    {
        Faction.OfPlayer.SetRelation(new FactionRelation(other, kind));

        foreach (Map map in Find.Maps)
            map.attackTargetsCache.Notify_FactionHostilityChanged(Faction.OfPlayer, other);
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
        if (Multiplayer.Client == null) return true;
        if (__instance is Camp) return true;

        bool parentBelongsToNonPlayerFaction = __instance.Faction is not { IsPlayer: true };

        return parentBelongsToNonPlayerFaction;
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

            if (inst.operand as FieldInfo == playerFactionField)
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
            if (inst.operand as MethodInfo == isPlayerMethodGetter)
                inst.operand = factionIsPlayer.Method;

            yield return inst;
        }
    }

    static bool FactionIsPlayer(Faction f)
    {
        return f == Find.FactionManager.OfPlayer;
    }
}

[HarmonyPatch(typeof(Ideo), nameof(Ideo.RecacheColonistBelieverCount))]
static class RecacheColonistBelieverCountPatch
{
    private static MethodInfo allColonists = AccessTools.PropertyGetter(typeof(PawnsFinder), nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            if (inst.operand as MethodInfo == allColonists)
                inst.operand = AccessTools.Method(typeof(RecacheColonistBelieverCountPatch), nameof(ColonistsAllPlayerFactions));
            yield return inst;
        }
    }

    private static List<Pawn> colonistsAllFactions = new();

    private static List<Pawn> ColonistsAllPlayerFactions()
    {
        colonistsAllFactions.Clear();

        foreach (var p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
        {
            if (IsColonistAnyFaction(p) && p.HostFaction == null && !p.InCryptosleep)
                colonistsAllFactions.Add(p);
        }

        return colonistsAllFactions;
    }

    // Note: IsColonist is patched above to compare to Faction.OfPlayer
    public static bool IsColonistAnyFaction(Pawn p)
    {
        if (p.Faction is { IsPlayer: true } && p.RaceProps.Humanlike)
            return !p.IsSlave || p.guest.SlaveIsSecure;
        return false;
    }

    public static bool IsColonyMechAnyFaction(Pawn p)
    {
        if (ModsConfig.BiotechActive && p.RaceProps.IsMechanoid && p.Faction is { IsPlayer: true } && p.MentalStateDef == null)
            return p.HostFaction == null || p.IsSlave;
        return false;
    }

    public static bool IsColonySubhumanAnyFaction(Pawn p)
    {
        return ModsConfig.AnomalyActive && p.IsSubhuman && p.Faction is { IsPlayer: true };
    }
}

[HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
static class AnyPawnBlockingMapRemovalPatch
{
    private static MethodInfo isColonist = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist));
    private static MethodInfo isColonyMech = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMech));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            if (inst.operand as MethodInfo == isColonyMech)
                inst.operand = AccessTools.Method(typeof(RecacheColonistBelieverCountPatch), nameof(RecacheColonistBelieverCountPatch.IsColonyMechAnyFaction));

            yield return inst;
        }
    }

    static void Postfix(MapPawns __instance, ref bool __result)
    {
        for (int i = 0; i < __instance.pawnsSpawned.Count; i++)
        {
            var p = __instance.pawnsSpawned[i];
            if (p.Faction is { IsPlayer: true } || p.HostFaction is { IsPlayer: true })
            {
                Job curJob = p.CurJob;
                if (curJob is { exitMapOnArrival: true })
                {
                    __result = true;
                    break;
                }

                if (p.health.hediffSet.InLabor())
                {
                    __result = true;
                    break;
                }
            }
        }
    }
}

[HarmonyPatch(typeof(MapPawns), nameof(MapPawns.IsValidColonyPawn))]
static class IsValidColonyPawnPatch
{
    private static MethodInfo isColonist = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist));
    private static MethodInfo isColonySubhuman = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonySubhuman));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            if (inst.operand as MethodInfo == isColonist)
                inst.operand = AccessTools.Method(typeof(RecacheColonistBelieverCountPatch), nameof(RecacheColonistBelieverCountPatch.IsColonistAnyFaction));

            if (inst.operand as MethodInfo == isColonySubhuman)
                inst.operand = AccessTools.Method(typeof(RecacheColonistBelieverCountPatch), nameof(RecacheColonistBelieverCountPatch.IsColonySubhumanAnyFaction));

            yield return inst;
        }
    }
}

[HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.ValidatePawn))]
static class ValidatePawnPatch
{
    private static MethodInfo isFreeNonSlaveColonist = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsFreeNonSlaveColonist));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            if (inst.operand as MethodInfo == isFreeNonSlaveColonist)
                inst.operand = AccessTools.Method(typeof(ValidatePawnPatch), nameof(IsFreeNonSlaveColonistAnyFaction));

            yield return inst;
        }
    }

    public static bool IsFreeNonSlaveColonistAnyFaction(Pawn p)
    {
        return p.Faction is { IsPlayer: true } && p.RaceProps.Humanlike && p.HostFaction == null && !p.IsSlave;
    }
}

[HarmonyPatch(typeof(Faction), nameof(Faction.HasGoodwill), MethodType.Getter)]
static class PlayerFactionsHaveGoodwill
{
    static void Postfix(Faction __instance, ref bool __result)
    {
        if (__instance.IsPlayer)
            __result = true;
    }
}

[HarmonyPatch(typeof(GenHostility), nameof(GenHostility.IsActiveThreatToPlayer))]
static class IsActiveThreatToAnyPlayer
{
    static void Postfix(IAttackTarget target, ref bool __result)
    {
        foreach (var f in Find.FactionManager.AllFactions)
            if (f.IsPlayer)
                __result |= GenHostility.IsActiveThreatTo(target, f);
    }
}

[HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string), typeof(int), typeof(bool))]
static class LetterStackReceiveOnlyMyFaction
{
    // todo the letter might get culled from the archive if it isn't in the stack and Sync depends on the archive
    static void Postfix(LetterStack __instance, Letter let)
    {
        if (Multiplayer.RealPlayerFaction != Faction.OfPlayer)
            __instance.letters.Remove(let);
    }
}

[HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string), typeof(int), typeof(bool))]
static class LetterStackReceiveSoundOnlyMyFaction
{
    private static MethodInfo PlayOneShotOnCamera =
        typeof(SoundStarter).GetMethod(nameof(SoundStarter.PlayOneShotOnCamera));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            if (inst.operand as MethodInfo == PlayOneShotOnCamera)
                yield return new CodeInstruction(
                    OpCodes.Call,
                    SymbolExtensions.GetMethodInfo((SoundDef s, Map m) => PlaySoundReplacement(s, m)));
            else
                yield return inst;
        }
    }

    static void PlaySoundReplacement(SoundDef sound, Map map)
    {
        if (Multiplayer.RealPlayerFaction == Faction.OfPlayer)
            sound.PlayOneShotOnCamera(map);
    }
}

[HarmonyPatch(typeof(Apparel), nameof(Apparel.WornGraphicPath), MethodType.Getter)]
static class ApparelWornGraphicPathGetterPatch
{
    private static FieldInfo thingIDNumberField = AccessTools.Field(typeof(Thing), nameof(Thing.thingIDNumber));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            yield return inst;

            // This instruction is part of wornGraphicPaths[thingIDNumber % wornGraphicPaths.Count]
            // The function makes sure the id is positive
            if (inst.operand as FieldInfo == thingIDNumberField)
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(ApparelWornGraphicPathGetterPatch), nameof(MakeIdPositive)));
        }
    }

    private static int MakeIdPositive(int id)
    {
        return id < 0 ? -id : id;
    }
}

#region Map generation

[HarmonyPatch(typeof(GenStep_Monolith), nameof(GenStep_Monolith.GenerateMonolith))]
public static class GenStepMonolithPatch
{
    static bool Prefix()
    {
        if (Multiplayer.Client == null)
            return true;

        var anomalyComp = Current.Game.components.OfType<GameComponent_Anomaly>().FirstOrDefault();

        return anomalyComp?.monolith == null;
    }
}

[HarmonyPatch(typeof(ScenPart_ScatterThings), nameof(ScenPart_ScatterThings.GenerateIntoMap))]
static class ScenPartScatterThingsPatch
{
    static bool Prefix()
    {
        return Multiplayer.Client == null || FactionCreator.generatingMap;
    }
}

[HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod), nameof(ScenPart_PlayerPawnsArriveMethod.GenerateIntoMap))]
static class ScenPartPlayerPawnsArriveMethodPatch
{
    static bool Prefix()
    {
        return Multiplayer.Client == null || FactionCreator.generatingMap;
    }
}

#endregion

[HarmonyPatch(typeof(CharacterCardUtility), nameof(CharacterCardUtility.DoTopStack))]
static class CharacterCardUtilityDontDrawIdeoPlate
{
    private static FieldInfo classicModeField = AccessTools.Field(typeof(IdeoManager), nameof(IdeoManager.classicMode));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            yield return inst;

            // Don't draw the ideo plate while choosing starting pawns in multifaction
            if (inst.operand as FieldInfo == classicModeField)
            {
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(CharacterCardUtilityDontDrawIdeoPlate), nameof(DontDrawIdeoPlate)));
                yield return new CodeInstruction(OpCodes.Or);
            }
        }
    }

    private static bool DontDrawIdeoPlate(bool generating)
    {
        return Multiplayer.Client != null && generating;
    }
}
