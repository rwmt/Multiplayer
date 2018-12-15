using Multiplayer.Common;
using RimWorld.Planet;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                UpdateRichPresence();

            if (Multiplayer.Client == null) return;

            if (SteamManager.Initialized)
            {
                ReadSteamPackets(0); // Reliable
                ReadSteamPackets(1); // Unreliable
            }

            UpdateSync();

            if (!Multiplayer.arbiterInstance && Application.isFocused && Time.realtimeSinceStartup - lastCursorSend > 0.05f && TickPatch.skipTo < 0)
            {
                lastCursorSend = Time.realtimeSinceStartup;
                SendCursor();
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

                writer.WriteShort((short)(UI.MouseMapPosition().x * 10f));
                writer.WriteShort((short)(UI.MouseMapPosition().z * 10f));
            }
            else
            {
                writer.WriteByte(byte.MaxValue);
            }

            Multiplayer.Client.Send(Packets.Client_Cursor, writer.GetArray(), reliable: false);
        }

        private Stopwatch lastSteamUpdate = Stopwatch.StartNew();

        private void UpdateRichPresence()
        {
            if (lastSteamUpdate.ElapsedMilliseconds < 1000) return;

            if (Multiplayer.session?.localSettings?.steam ?? false)
                SteamFriends.SetRichPresence("connect", $"{Multiplayer.SteamConnectStart}{SteamUser.GetSteamID()}");
            else
                SteamFriends.SetRichPresence("connect", null);

            lastSteamUpdate.Restart();
        }

        private void ReadSteamPackets(int channel)
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, channel))
            {
                byte[] data = new byte[size];
                SteamNetworking.ReadP2PPacket(data, size, out uint sizeRead, out CSteamID remote, channel);
                HandleSteamPacket(remote, data, channel);
            }
        }

        private void HandleSteamPacket(CSteamID remote, byte[] data, int channel)
        {
            if (Multiplayer.Client is SteamConnection localConn && localConn.remoteId == remote)
                Multiplayer.HandleReceive(data, channel == 0);

            if (Multiplayer.LocalServer == null) return;

            Multiplayer.LocalServer.Enqueue(() =>
            {
                ServerPlayer player = Multiplayer.LocalServer.FindPlayer(p => p.conn is SteamConnection conn && conn.remoteId == remote);

                if (player == null)
                {
                    IConnection conn = new SteamConnection(remote);
                    conn.State = ConnectionStateEnum.ServerSteam;
                    player = Multiplayer.LocalServer.OnConnected(conn);
                    player.type = PlayerType.Steam;

                    player.steamId = (ulong)remote;
                    player.steamPersonaName = SteamFriends.GetFriendPersonaName(remote);
                    if (player.steamPersonaName.Length == 0)
                        player.steamPersonaName = "[unknown]";
                }

                player.HandleReceive(data, channel == 0);
            });
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

                    if (Utils.MillisNow - data.timestamp > 200)
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
            if (Equals(data.toSend, data.actualValue))
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
            }

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

