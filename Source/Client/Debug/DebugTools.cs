using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;
using Multiplayer.Common;

using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    static class MpDebugTools
    {
        private static int currentPlayer;
        public static int currentHash;

        public static PlayerDebugState CurrentPlayerState =>
            Multiplayer.game.playerDebugState.GetOrAddNew(currentPlayer == -1 ? Multiplayer.session.playerId : currentPlayer);

        public static void HandleCmd(ByteReader data)
        {
            currentPlayer = data.ReadInt32();
            var source = (DebugSource)data.ReadInt32();
            int cursorX = data.ReadInt32();
            int cursorZ = data.ReadInt32();

            if (Multiplayer.MapContext != null)
                MouseCellPatch.result = new IntVec3(cursorX, 0, cursorZ);
            else
                MouseTilePatch.result = cursorX;

            currentHash = data.ReadInt32();
            var state = Multiplayer.game.playerDebugState.GetOrAddNew(currentPlayer);

            var prevTool = DebugTools.curTool;
            DebugTools.curTool = state.tool;

            List<object> prevSelected = Find.Selector.selected;
            List<WorldObject> prevWorldSelected = Find.WorldSelector.selected;

            Find.Selector.selected = new List<object>();
            Find.WorldSelector.selected = new List<WorldObject>();

            int selectedId = data.ReadInt32();

            if (Multiplayer.MapContext != null)
            {
                var thing = ThingsById.thingsById.GetValueSafe(selectedId);
                if (thing != null)
                    Find.Selector.selected.Add(thing);
            }
            else
            {
                var obj = Find.WorldObjects.AllWorldObjects.FirstOrDefault(w => w.ID == selectedId);
                if (obj != null)
                    Find.WorldSelector.selected.Add(obj);
            }

            Log.Message($"Debug tool {source} ({cursorX}, {cursorZ}) {currentHash}");

            try
            {
                if (source == DebugSource.ListingMap)
                {
                    new Dialog_DebugActionsMenu().DoListingItems();
                }
                else if (source == DebugSource.ListingWorld)
                {
                    new Dialog_DebugActionsMenu().DoListingItems();
                }
                else if (source == DebugSource.ListingPlay)
                {
                    new Dialog_DebugActionsMenu().DoListingItems();
                }
                else if (source == DebugSource.Lister)
                {
                    var options = (state.window as List<DebugMenuOption>) ?? new List<DebugMenuOption>();
                    new Dialog_DebugOptionListLister(options).DoListingItems();
                }
                else if (source == DebugSource.Tool)
                {
                    DebugTools.curTool?.clickAction();
                }
                else if (source == DebugSource.FloatMenu)
                {
                    (state.window as List<FloatMenuOption>)?.FirstOrDefault(o => o.Hash() == currentHash)?.action();
                }
            }
            finally
            {
                if (TickPatch.currentExecutingCmdIssuedBySelf && DebugTools.curTool != null && DebugTools.curTool != state.tool)
                {
                    var map = Multiplayer.MapContext;
                    prevTool = new DebugTool(DebugTools.curTool.label, () =>
                    {
                        SendCmd(DebugSource.Tool, 0, map);
                    }, DebugTools.curTool.onGUIAction);
                }

                state.tool = DebugTools.curTool;
                DebugTools.curTool = prevTool;

                MouseCellPatch.result = null;
                MouseTilePatch.result = null;
                Find.Selector.selected = prevSelected;
                Find.WorldSelector.selected = prevWorldSelected;

                currentHash = 0;
                currentPlayer = -1;
            }
        }

        public static void SendCmd(DebugSource source, int hash, Map map)
        {
            var writer = new LoggingByteWriter();
            writer.Log.Node($"Debug tool {source}, map {map.ToStringSafe()}");
            int cursorX = 0, cursorZ = 0;

            if (map != null)
            {
                cursorX = UI.MouseCell().x;
                cursorZ = UI.MouseCell().z;
            }
            else
            {
                cursorX = GenWorld.MouseTile(false);
            }

            writer.WriteInt32(Multiplayer.session.playerId);
            writer.WriteInt32((int)source);
            writer.WriteInt32(cursorX);
            writer.WriteInt32(cursorZ);
            writer.WriteInt32(hash);

            if (map != null)
                writer.WriteInt32(Find.Selector.SingleSelectedThing?.thingIDNumber ?? -1);
            else
                writer.WriteInt32(Find.WorldSelector.SingleSelectedObject?.ID ?? -1);

            Multiplayer.WriterLog.AddCurrentNode(writer);

            var mapId = map?.uniqueID ?? ScheduledCommand.Global;
            Multiplayer.Client.SendCommand(CommandType.DebugTools, mapId, writer.ToArray());
        }

        public static DebugSource ListingSource()
        {
            if (ListingWorldMarker.drawing)
                return DebugSource.ListingWorld;
            else if (ListingMapMarker.drawing)
                return DebugSource.ListingMap;
            else if (ListingPlayMarker.drawing)
                return DebugSource.ListingPlay;

            return DebugSource.None;
        }
    }

    public class PlayerDebugState
    {
        public object window;
        public DebugTool tool;

        public Map Map => Find.Maps.Find(m => m.uniqueID == mapId);
        public int mapId;
    }

    public enum DebugSource
    {
        None,
        ListingWorld,
        ListingMap,
        ListingPlay,
        Lister,
        Tool,
        FloatMenu,
    }

    [HarmonyPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems))]
    static class ListingPlayMarker
    {
        public static bool drawing;

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix() => drawing = true;

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix() => drawing = false;
    }

    [HarmonyPatch(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugToolWorld))]
    static class ListingWorldMarker
    {
        public static bool drawing;

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix() => drawing = true;

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix() => drawing = false;
    }

    [HarmonyPatch]
    static class ListingIncidentMarker
    {
        public static IIncidentTarget target;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DebugActionsIncidents), nameof(DebugActionsIncidents.DoIncidentDebugAction));
            yield return AccessTools.Method(typeof(DebugActionsIncidents), nameof(DebugActionsIncidents.DoIncidentWithPointsAction));
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(IIncidentTarget target) => ListingIncidentMarker.target = target;

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix() => target = null;
    }

    [HarmonyPatch]
    static class ListingMapMarker
    {
        public static bool drawing;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugToolMap));
            yield return AccessTools.Method(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugToolMapForPawns));
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix() => drawing = true;

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix() => drawing = false;
    }

    [HarmonyPatch]
    static class CancelDebugDrawing
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DoGap));
            yield return AccessTools.Method(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DoLabel));
        }

        static bool Prefix() => !Multiplayer.ExecutingCmds;
    }

    [HarmonyPatch(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugAction))]
    static class DebugActionPatch
    {
        static bool Prefix(Dialog_DebugOptionLister __instance, string label, ref Action action)
        {
            if (Multiplayer.Client == null) return true;
            if (Current.ProgramState == ProgramState.Playing && !Multiplayer.GameComp.debugMode) return true;

            int hash = Gen.HashCombineInt(
                GenText.StableStringHash(action.Method.MethodDesc()),
                GenText.StableStringHash(label)
            );

            if (Multiplayer.ExecutingCmds)
            {
                if (hash == MpDebugTools.currentHash)
                    action();

                return false;
            }

            if (__instance is Dialog_DebugActionsMenu)
            {
                var source = MpDebugTools.ListingSource();
                if (source == DebugSource.None) return true;

                Map map = source == DebugSource.ListingMap ? Find.CurrentMap : null;

                if (ListingIncidentMarker.target != null)
                    map = ListingIncidentMarker.target as Map;

                action = () => MpDebugTools.SendCmd(source, hash, map);
            }

            if (__instance is Dialog_DebugOptionListLister)
            {
                action = () => MpDebugTools.SendCmd(DebugSource.Lister, hash, MpDebugTools.CurrentPlayerState.Map);
            }

            return true;
        }
    }

    [HarmonyPatch]
    static class DebugToolPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugToolMap));
            yield return AccessTools.Method(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugToolWorld));
        }

        static bool Prefix(Dialog_DebugOptionLister __instance, string label, Action toolAction, ref Container<DebugTool>? __state)
        {
            if (Multiplayer.Client == null) return true;
            if (Current.ProgramState == ProgramState.Playing && !Multiplayer.GameComp.debugMode) return true;

            if (Multiplayer.ExecutingCmds)
            {
                int hash = Gen.HashCombineInt(GenText.StableStringHash(toolAction.Method.MethodDesc()), GenText.StableStringHash(label));
                if (hash == MpDebugTools.currentHash)
                    DebugTools.curTool = new DebugTool(label, toolAction);

                return false;
            }

            __state = DebugTools.curTool;

            return true;
        }

        static void Postfix(Dialog_DebugOptionLister __instance, string label, Action toolAction, Container<DebugTool>? __state)
        {
            // New tool chosen
            if (!__state.HasValue || DebugTools.curTool == __state?.Inner)
            {
                return;
            }

            int hash = Gen.HashCombineInt(GenText.StableStringHash(toolAction.Method.MethodDesc()), GenText.StableStringHash(label));

            if (__instance is Dialog_DebugActionsMenu)
            {
                var source = MpDebugTools.ListingSource();
                if (source == DebugSource.None) return;

                Map map = source == DebugSource.ListingMap ? Find.CurrentMap : null;

                MpDebugTools.SendCmd(source, hash, map);
                DebugTools.curTool = null;
            }

            else if (__instance is Dialog_DebugOptionListLister lister)
            {
                Map map = MpDebugTools.CurrentPlayerState.Map;
                if (ListingMapMarker.drawing)
                {
                    map = Find.CurrentMap;
                }
                MpDebugTools.SendCmd(DebugSource.Lister, hash, map);
                DebugTools.curTool = null;
            }
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class DebugListerAddPatch
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.Client == null) return true;
            if (!Multiplayer.ExecutingCmds) return true;
            if (!Multiplayer.GameComp.debugMode) return true;

            bool keepOpen = TickPatch.currentExecutingCmdIssuedBySelf;
            var map = Multiplayer.MapContext;

            if (window is Dialog_DebugOptionListLister lister)
            {
                MpDebugTools.CurrentPlayerState.window = lister.options;
                MpDebugTools.CurrentPlayerState.mapId = map?.uniqueID ?? -1;

                return keepOpen;
            }

            if (window is FloatMenu menu)
            {
                var options = menu.options;

                if (keepOpen)
                {
                    menu.options = new List<FloatMenuOption>();

                    foreach (var option in options)
                    {
                        var copy = new FloatMenuOption(option.labelInt, option.action);
                        int hash = copy.Hash();
                        copy.action = () => MpDebugTools.SendCmd(DebugSource.FloatMenu, hash, map);
                        menu.options.Add(copy);
                    }
                }

                MpDebugTools.CurrentPlayerState.window = options;
                return keepOpen;
            }

            return true;
        }

        public static int Hash(this FloatMenuOption opt)
        {
            return Gen.HashCombineInt(GenText.StableStringHash(opt.action.Method.MethodDesc()), GenText.StableStringHash(opt.labelInt));
        }
    }

}
