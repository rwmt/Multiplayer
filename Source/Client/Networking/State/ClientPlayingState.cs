using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class ClientPlayingState : ClientConnectionState
    {
        public ClientPlayingState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.Server_TimeControl)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt32();
            int cmdId = data.ReadInt32();

            if (TickPatch.tickUntil >= tickUntil) return;

            Multiplayer.session.remoteTickUntil = tickUntil;
            Multiplayer.session.remoteCmdId = cmdId;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_KeepAlive)]
        public void HandleKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

            connection.Send(Packets.Client_KeepAlive, id, (ticksBehind << 1) | (TickPatch.Simulating ? 1 : 0));
        }

        [PacketHandler(Packets.Server_Command)]
        public void HandleCommand(ByteReader data)
        {
            ScheduledCommand cmd = ScheduledCommand.Deserialize(data);
            cmd.issuedBySelf = data.ReadBool();
            Session.ScheduleCommand(cmd);

            Multiplayer.session.localCmdId++;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_PlayerList)]
        public void HandlePlayerList(ByteReader data)
        {
            var action = (PlayerListAction)data.ReadByte();

            if (action == PlayerListAction.Add)
            {
                var info = PlayerInfo.Read(data);
                if (!Multiplayer.session.players.Contains(info))
                    Multiplayer.session.players.Add(info);
            }
            else if (action == PlayerListAction.Remove)
            {
                int id = data.ReadInt32();
                Multiplayer.session.players.RemoveAll(p => p.id == id);
            }
            else if (action == PlayerListAction.List)
            {
                int count = data.ReadInt32();

                Multiplayer.session.players.Clear();
                for (int i = 0; i < count; i++)
                    Multiplayer.session.players.Add(PlayerInfo.Read(data));
            }
            else if (action == PlayerListAction.Latencies)
            {
                int count = data.ReadInt32();

                for (int i = 0; i < count; i++)
                {
                    var player = Multiplayer.session.players[i];
                    player.latency = data.ReadInt32();
                    player.ticksBehind = data.ReadInt32();
                }
            }
            else if (action == PlayerListAction.Status)
            {
                var id = data.ReadInt32();
                var status = (PlayerStatus)data.ReadByte();
                var player = Multiplayer.session.GetPlayerInfo(id);

                if (player != null)
                    player.status = status;
            }
        }

        [PacketHandler(Packets.Server_Chat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            Multiplayer.session.AddMsg(msg);
        }

        [PacketHandler(Packets.Server_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            int playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            byte seq = data.ReadByte();
            if (seq < player.cursorSeq && player.cursorSeq - seq < 128) return;

            byte map = data.ReadByte();
            player.map = map;

            if (map == byte.MaxValue) return;

            byte icon = data.ReadByte();
            float x = data.ReadShort() / 10f;
            float z = data.ReadShort() / 10f;

            player.cursorSeq = seq;
            player.lastCursor = player.cursor;
            player.lastDelta = Multiplayer.Clock.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(x, 0, z);
            player.updatedAt = Multiplayer.Clock.ElapsedMillisDouble();
            player.cursorIcon = icon;

            short dragXRaw = data.ReadShort();
            if (dragXRaw != -1)
            {
                float dragX = dragXRaw / 10f;
                float dragZ = data.ReadShort() / 10f;

                player.dragStart = new Vector3(dragX, 0, dragZ);
            }
            else
            {
                player.dragStart = PlayerInfo.Invalid;
            }
        }

        [PacketHandler(Packets.Server_Selected)]
        public void HandleSelected(ByteReader data)
        {
            int playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            bool reset = data.ReadBool();

            if (reset)
                player.selectedThings.Clear();

            int[] add = data.ReadPrefixedInts();
            for (int i = 0; i < add.Length; i++)
                player.selectedThings[add[i]] = Time.realtimeSinceStartup;

            int[] remove = data.ReadPrefixedInts();
            for (int i = 0; i < remove.Length; i++)
                player.selectedThings.Remove(remove[i]);
        }

        [PacketHandler(Packets.Server_MapResponse)]
        public void HandleMapResponse(ByteReader data)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            Session.cache.mapCmds[mapId] = mapCmds;

            byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            Session.cache.mapData[mapId] = mapData;

            //ClientJoiningState.ReloadGame(TickPatch.tickUntil, Find.Maps.Select(m => m.uniqueID).Concat(mapId).ToList());
            // todo Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
        }

        [PacketHandler(Packets.Server_Notification)]
        public void HandleNotification(ByteReader data)
        {
            string key = data.ReadString();
            string[] args = data.ReadPrefixedStrings();

            Messages.Message(key.Translate(Array.ConvertAll(args, s => (NamedArgument)s)), MessageTypeDefOf.SilentInput, false);
        }

        [PacketHandler(Packets.Server_SyncInfo)]
        [IsFragmented]
        public void HandleDesyncCheck(ByteReader data)
        {
            Multiplayer.game?.sync.AddClientOpinionAndCheckDesync(ClientSyncOpinion.Deserialize(data));
        }

        [PacketHandler(Packets.Server_Pause)]
        public void HandlePause(ByteReader data)
        {
            bool pause = data.ReadBool();
            // This packet doesn't get processed in time during a synchronous long event
        }

        [PacketHandler(Packets.Server_Debug)]
        public void HandleDebug(ByteReader data)
        {
            int tick = data.ReadInt32();
            int diffAt = data.ReadInt32();
            var info = Multiplayer.game.sync.knownClientOpinions.FirstOrDefault(b => b.startTick == tick);
            var side = Multiplayer.arbiterInstance ? "Arbiter" : "Host";

            Log.Message($"{info?.desyncStackTraces.Count} {side} traces {diffAt} / {Multiplayer.game.sync.knownClientOpinions.Select(o => o.startTick).Join()}");

            File.WriteAllText(
                MpUtil.RwDataFile($"MP_{side}Traces.txt"),
                info?.GetFormattedStackTracesForRange(diffAt) ?? "null"
            );
        }
    }

}
