using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Common;

using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
            var path = data.ReadString();

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

            Log.Message($"Debug tool {source} ({cursorX}, {cursorZ}) {currentHash} {path}");

            try
            {
                if (source == DebugSource.Tree)
                {
                    var node = GetNode(path);
                    if (node != null)
                    {
                        node.Enter(null);
                        if (node.actionType is DebugActionType.ToolMap or DebugActionType.ToolWorld or DebugActionType.ToolMapForPawns)
                        {
                            DebugTools.curTool.clickAction();
                            DebugTools.curTool = null;
                        }
                    }
                }
                else if (source == DebugSource.Lister)
                {
                    var options = state.currentData as List<DebugMenuOption> ?? new List<DebugMenuOption>();
                    options.FirstOrDefault(o => o.Hash() == currentHash).method?.Invoke();
                }
                else if (source == DebugSource.Tool)
                {
                    DebugTools.curTool?.clickAction();
                }
                else if (source == DebugSource.FloatMenu)
                {
                    (state.currentData as List<FloatMenuOption>)?.FirstOrDefault(o => o.Hash() == currentHash)?.action();
                }
            }
            finally
            {
                if (TickPatch.currentExecutingCmdIssuedBySelf && DebugTools.curTool != null && DebugTools.curTool != state.tool)
                {
                    var map = Multiplayer.MapContext;
                    prevTool = new DebugTool(DebugTools.curTool.label, () =>
                    {
                        SendCmd(DebugSource.Tool, 0, null, map);
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

        public static void SendCmd(DebugSource source, int hash, string path, Map map)
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
            writer.WriteString(path);

            if (map != null)
                writer.WriteInt32(Find.Selector.SingleSelectedThing?.thingIDNumber ?? -1);
            else
                writer.WriteInt32(Find.WorldSelector.SingleSelectedObject?.ID ?? -1);

            Multiplayer.WriterLog.AddCurrentNode(writer);

            int mapId = map?.uniqueID ?? ScheduledCommand.Global;
            if (!Multiplayer.GhostMode)
                Multiplayer.Client.SendCommand(CommandType.DebugTools, mapId, writer.ToArray());
        }

        // From Dialog_Debug.GetNode
        public static DebugActionNode GetNode(string path)
        {
            Dialog_Debug.TrySetupNodeGraph();
            DebugActionNode curNode = Dialog_Debug.rootNode;
            string[] pathParts = path.Split('\\');
            for (int i = 0; i < pathParts.Length; i++)
            {
                DebugActionNode node = curNode.children.FirstOrDefault(n => n.LabelAndCategory() == pathParts[i]);
                if (node == null)
                    return null;
                curNode = node;
                curNode.TrySetupChildren();
            }
            return curNode;
        }

        public static string LabelAndCategory(this DebugActionNode node)
        {
            return $"{node.label}@{node.category}";
        }

        public static string NodePath(this DebugActionNode node)
        {
            if (node.parent is { IsRoot: false })
                return node.parent.NodePath() + "\\" + LabelAndCategory(node);

            return LabelAndCategory(node);
        }

        public static bool ShouldHandle()
        {
#if DEBUG
            return !Event.current.shift;
#else
            return true;
#endif
        }
    }

    public class PlayerDebugState
    {
        public object currentData;
        public DebugTool tool;
    }

    public enum DebugSource
    {
        Tree,
        Lister,
        Tool,
        FloatMenu,
    }

    [HarmonyPatch(typeof(DebugActionNode), nameof(DebugActionNode.Enter))]
    static class DebugActionNodeEnter
    {
        static void Prefix(DebugActionNode __instance)
        {
            if (Multiplayer.Client != null && __instance.action is {Target: not MpDebugAction})
                __instance.action = new MpDebugAction { node = __instance, original = __instance.action }.Action;
        }

        static void Postfix(DebugActionNode __instance)
        {
            // Other actionTypes get handled by the Prefix
            if (Multiplayer.Client != null && __instance.actionType == DebugActionType.ToolMapForPawns)
                DebugTools.curTool.clickAction = new MpDebugAction { node = __instance, original = DebugTools.curTool.clickAction }.Action;
        }

        class MpDebugAction
        {
            public DebugActionNode node;
            public Action original;

            public void Action()
            {
                if (Multiplayer.Client == null || Multiplayer.ExecutingCmds || !MpDebugTools.ShouldHandle())
                    original();
                else
                    MpDebugTools.SendCmd(
                        DebugSource.Tree,
                        0,
                        node.NodePath(),
                        WorldRendererUtility.WorldRenderedNow ? null : Find.CurrentMap
                    );
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
            if (!MpDebugTools.ShouldHandle()) return true;

            bool keepOpen = TickPatch.currentExecutingCmdIssuedBySelf;
            var map = Multiplayer.MapContext;

            if (window is Dialog_DebugOptionListLister lister)
            {
                var origOptions = lister.options;

                if (keepOpen)
                {
                    lister.options = new List<DebugMenuOption>();

                    foreach (var opt in origOptions)
                    {
                        int hash = opt.Hash();
                        lister.options.Add(new DebugMenuOption(
                            opt.label,
                            opt.mode,
                            () => MpDebugTools.SendCmd(DebugSource.Lister, hash, null, map)
                        ));
                    }
                }

                MpDebugTools.CurrentPlayerState.currentData = origOptions;
                return keepOpen;
            }

            if (window is FloatMenu menu)
            {
                var origOptions = menu.options;

                if (keepOpen)
                {
                    menu.options = new List<FloatMenuOption>();

                    foreach (var option in origOptions)
                    {
                        var copy = new FloatMenuOption(option.labelInt, option.action);
                        int hash = copy.Hash();
                        copy.action = () => MpDebugTools.SendCmd(DebugSource.FloatMenu, hash, null, map);
                        menu.options.Add(copy);
                    }
                }

                MpDebugTools.CurrentPlayerState.currentData = origOptions;
                return keepOpen;
            }

            return true;
        }

        public static int Hash(this FloatMenuOption opt)
        {
            return Gen.HashCombineInt(GenText.StableStringHash(opt.action.Method.MethodDesc()), GenText.StableStringHash(opt.labelInt));
        }

        public static int Hash(this DebugMenuOption opt)
        {
            return Gen.HashCombineInt(GenText.StableStringHash(opt.method.Method.MethodDesc()), GenText.StableStringHash(opt.label));
        }
    }

}
