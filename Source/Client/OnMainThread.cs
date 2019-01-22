using Multiplayer.Common;
using RimWorld.Planet;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class OnMainThread : MonoBehaviour
    {
        public static ActionQueue queue = new ActionQueue();

        public static int cachedAtTime;
        public static byte[] cachedGameData;
        public static Dictionary<int, byte[]> cachedMapData = new Dictionary<int, byte[]>();

        // Global cmds are -1
        public static Dictionary<int, List<ScheduledCommand>> cachedMapCmds = new Dictionary<int, List<ScheduledCommand>>();

        public void Update()
        {
            Multiplayer.session?.netClient?.PollEvents();

            queue.RunQueue();

            if (SteamManager.Initialized)
                SteamIntegration.UpdateRichPresence();

            if (Multiplayer.Client == null) return;

            UpdateSync();

            if (!Multiplayer.arbiterInstance && Application.isFocused && !TickPatch.Skipping && !Multiplayer.session.desynced)
                SendVisuals();

            if (Multiplayer.Client is SteamBaseConn steamConn && SteamManager.Initialized)
                foreach (var packet in SteamIntegration.ReadPackets())
                    if (steamConn.remoteId == packet.remote)
                        Multiplayer.HandleReceive(packet.data, packet.reliable);
        }

        private void SendVisuals()
        {
            if (Time.realtimeSinceStartup - lastCursorSend > 0.05f)
            {
                lastCursorSend = Time.realtimeSinceStartup;
                SendCursor();
            }

            if (Time.realtimeSinceStartup - lastSelectedSend > 0.2f)
            {
                lastSelectedSend = Time.realtimeSinceStartup;
                SendSelected();
            }
        }

        private byte cursorSeq;
        private float lastCursorSend;

        private void SendCursor()
        {
            var writer = new ByteWriter();
            writer.WriteByte(cursorSeq++);

            if (Find.CurrentMap != null && !WorldRendererUtility.WorldRenderedNow)
            {
                writer.WriteByte((byte)Find.CurrentMap.Index);

                var icon = Find.MapUI?.designatorManager?.SelectedDesignator?.icon;
                int iconId = icon == null ? 0 : !Multiplayer.icons.Contains(icon) ? 0 : Multiplayer.icons.IndexOf(icon);
                writer.WriteByte((byte)iconId);

                writer.WriteVectorXZ(UI.MouseMapPosition());

                if (Find.Selector.dragBox.IsValidAndActive)
                    writer.WriteVectorXZ(Find.Selector.dragBox.start);
                else
                    writer.WriteShort(-1);
            }
            else
            {
                writer.WriteByte(byte.MaxValue);
            }

            Multiplayer.Client.Send(Packets.Client_Cursor, writer.ToArray(), reliable: false);
        }

        private HashSet<int> lastSelected = new HashSet<int>();
        private float lastSelectedSend;
        private int lastMap;

        private void SendSelected()
        {
            if (Current.ProgramState != ProgramState.Playing) return;

            var writer = new ByteWriter();

            int mapId = Find.CurrentMap?.Index ?? -1;
            if (WorldRendererUtility.WorldRenderedNow) mapId = -1;

            bool reset = false;

            if (mapId != lastMap)
            {
                reset = true;
                lastMap = mapId;
                lastSelected.Clear();
            }

            var selected = new HashSet<int>(Find.Selector.selected.OfType<Thing>().Select(t => t.thingIDNumber));

            var add = new List<int>(selected.Except(lastSelected));
            var remove = new List<int>(lastSelected.Except(selected));

            if (!reset && add.Count == 0 && remove.Count == 0) return;

            writer.WriteBool(reset);
            writer.WritePrefixedInts(add);
            writer.WritePrefixedInts(remove);

            lastSelected = selected;

            Multiplayer.Client.Send(Packets.Client_Selected, writer.ToArray());
        }

        private void UpdateSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (f.inGameLoop) continue;

                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (CheckShouldRemove(f, k, data))
                        return true;

                    if (!data.sent && Utils.MillisNow - data.timestamp > 200)
                    {
                        f.DoSync(k.first, data.toSend, k.second);
                        data.sent = true;
                        data.timestamp = Utils.MillisNow;
                    }

                    return false;
                });
            }
        }

        public static bool CheckShouldRemove(SyncField field, Pair<object, object> target, BufferData data)
        {
            if (data.sent && Equals(data.toSend, data.actualValue))
                return true;

            object currentValue = target.first.GetPropertyOrField(field.memberPath, target.second);

            if (!Equals(currentValue, data.actualValue))
            {
                if (data.sent)
                    return true;
                else
                    data.actualValue = currentValue;
            }

            return false;
        }

        public void OnApplicationQuit()
        {
            StopMultiplayer();
        }

        public static void StopMultiplayer()
        {
            if (Multiplayer.session != null)
            {
                Multiplayer.session.Stop();
                Multiplayer.session = null;
                Prefs.Apply();
            }

            Multiplayer.game = null;

            TickPatch.ClearSkipping();
            TickPatch.Timer = 0;
            TickPatch.tickUntil = 0;
            TickPatch.accumulator = 0;

            Find.WindowStack?.WindowOfType<ServerBrowser>()?.Cleanup(true);

            foreach (var entry in Sync.bufferedChanges)
                entry.Value.Clear();

            ClearCaches();

            if (Multiplayer.arbiterInstance)
            {
                Multiplayer.arbiterInstance = false;
                Application.Quit();
            }
        }

        public static void ClearCaches()
        {
            cachedAtTime = 0;
            cachedGameData = null;
            cachedMapData.Clear();
            cachedMapCmds.Clear();
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public static void ScheduleCommand(ScheduledCommand cmd)
        {
            MpLog.Log($"Cmd: {cmd.type}, faction: {cmd.factionId}, map: {cmd.mapId}, ticks: {cmd.ticks}");
            cachedMapCmds.GetOrAddNew(cmd.mapId).Add(cmd);

            if (Current.ProgramState != ProgramState.Playing) return;

            if (cmd.mapId == ScheduledCommand.Global)
                Multiplayer.WorldComp.cmds.Enqueue(cmd);
            else
                cmd.GetMap()?.AsyncTime().cmds.Enqueue(cmd);
        }
    }

}

