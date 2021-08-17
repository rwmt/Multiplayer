using Multiplayer.Client.Networking;
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
    public class OnMainThread : MonoBehaviour
    {
        public static ActionQueue queue = new ActionQueue();

        public void Update()
        {
            Multiplayer.session?.netClient?.PollEvents();

            queue.RunQueue();

            if (SteamManager.Initialized)
                SteamIntegration.UpdateRichPresence();

            if (Multiplayer.Client == null) return;

            SyncUtil.UpdateSync();

            if (!Multiplayer.arbiterInstance && Application.isFocused && !TickPatch.Simulating && !Multiplayer.session.desynced)
                SendVisuals();

            if (Multiplayer.Client is SteamBaseConn steamConn && SteamManager.Initialized)
                foreach (var packet in SteamIntegration.ReadPackets(steamConn.recvChannel))
                    // Note: receive can lead to disconnection
                    if (steamConn.remoteId == packet.remote && Multiplayer.Client != null)
                        ClientUtil.HandleReceive(packet.data, packet.reliable);
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
                int iconId = icon == null ? 0 : !MultiplayerData.icons.Contains(icon) ? 0 : MultiplayerData.icons.IndexOf(icon);
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

        public void OnApplicationQuit()
        {
            Multiplayer.StopMultiplayer();
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }
    }

}

