using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class ClientPlayingState : ClientBaseState
    {
        public ClientPlayingState(ConnectionBase connection) : base(connection)
        {
        }

        [PacketHandler(Packets.Server_TimeControl)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt32();
            int sentCmds = data.ReadInt32();
            TickPatch.serverTimePerTick = data.ReadFloat();

            if (Multiplayer.session.remoteTickUntil >= tickUntil) return;

            Multiplayer.session.remoteTickUntil = tickUntil;
            Multiplayer.session.remoteSentCmds = sentCmds;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_KeepAlive)]
        public void HandleKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

            connection.Send(
                Packets.Client_KeepAlive,
                ByteWriter.GetBytes(id, ticksBehind, TickPatch.Simulating, TickPatch.workTicks),
                false
            );
        }

        [PacketHandler(Packets.Server_Command)]
        public void HandleCommand(ByteReader data)
        {
            ScheduledCommand cmd = ScheduledCommand.Deserialize(data);
            cmd.issuedBySelf = data.ReadBool();
            Session.ScheduleCommand(cmd);

            Multiplayer.session.receivedCmds++;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_CanRejoin)]
        public void HandleCanRejoin(ByteReader data)
        {
            MultiplayerSession.DoRejoin();
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
                    player.simulating = data.ReadBool();
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
            player.lastDelta = Multiplayer.clock.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(x, 0, z);
            player.updatedAt = Multiplayer.clock.ElapsedMillisDouble();
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

        [PacketHandler(Packets.Server_PingLocation)]
        public void HandlePing(ByteReader data)
        {
            int player = data.ReadInt32();
            int map = data.ReadInt32();
            int planetTile = data.ReadInt32();
            var loc = new Vector3(data.ReadFloat(), data.ReadFloat(), data.ReadFloat());

            Session.cursorAndPing.ReceivePing(player, map, planetTile, loc);
        }

        [PacketHandler(Packets.Server_MapResponse)]
        public void HandleMapResponse(ByteReader data)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            Session.dataSnapshot.mapCmds[mapId] = mapCmds;

            byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            Session.dataSnapshot.mapData[mapId] = mapData;

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

        [PacketHandler(Packets.Server_Freeze)]
        public void HandleFreze(ByteReader data)
        {
            bool frozen = data.ReadBool();
            int frozenAt = data.ReadInt32();

            TickPatch.serverFrozen = frozen;
            TickPatch.frozenAt = frozenAt;
        }

        [PacketHandler(Packets.Server_Traces)]
        [IsFragmented]
        public void HandleTraces(ByteReader data)
        {
            var type = (TracesPacket)data.ReadInt32();

            if (type == TracesPacket.Request)
            {
                var tick = data.ReadInt32();
                var diffAt = data.ReadInt32();
                var playerId = data.ReadInt32();

                var info = Multiplayer.game.sync.knownClientOpinions.FirstOrDefault(b => b.startTick == tick);
                var response = info?.GetFormattedStackTracesForRange(diffAt);

                connection.Send(Packets.Client_Traces, TracesPacket.Response, playerId, GZipStream.CompressString(response));
            }
            else if (type == TracesPacket.Transfer)
            {
                var traces = data.ReadPrefixedBytes();
                Multiplayer.session.desyncTracesFromHost = GZipStream.UncompressString(traces);
            }
        }

        [PacketHandler(Packets.Server_Debug)]
        public void HandleDebug(ByteReader data)
        {
        }

        [PacketHandler(Packets.Server_SetFaction)]
        public void HandleSetFaction(ByteReader data)
        {
            int player = data.ReadInt32();
            int factionId = data.ReadInt32();

            Session.GetPlayerInfo(player).factionId = factionId;

            if (Session.playerId == player)
                Multiplayer.game.ChangeRealPlayerFaction(factionId);
        }
    }

}
