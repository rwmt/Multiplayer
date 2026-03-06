using System;
using System.Collections.Generic;
using Ionic.Zlib;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [PacketHandlerClass(inheritHandlers: true)]
    public class ClientPlayingState(ConnectionBase connection) : ClientBaseState(connection)
    {
        [TypedPacketHandler]
        public void HandleCommand(ServerCommandPacket packet)
        {
            Session.ScheduleCommand(packet.ToCommand());
            Multiplayer.session.receivedCmds++;
            Multiplayer.session.ProcessTimeControl();
        }

        [TypedPacketHandler]
        public void HandlePlayerList(ServerPlayerListPacket packet)
        {
            if (packet.action == PlayerListAction.Add)
            {
                foreach (var info in packet.players)
                {
                    if (!Multiplayer.session.players.Any(p => p.id == info.id || p.username == info.username))
                    {
                        ServerLog.Log($"PlayerList: Adding player {info.id}:{info.username}");
                        Multiplayer.session.players.Add(PlayerInfo.FromNet(info));
                    }
                    else
                    {
                        ServerLog.Error($"PlayerList: Adding player {info.id}:{info.username} - player already exists");
                    }
                }
            }
            else if (packet.action == PlayerListAction.Remove)
            {
                ServerLog.Log($"PlayerList: Removing player with id {packet.playerId}");
                var matches = Multiplayer.session.players.RemoveAll(p => p.id == packet.playerId);
                if (matches > 1)
                {
                    ServerLog.Error($"PlayerList: Removing player with id {packet.playerId} -- occurred {matches} times. This should not happen");
                }
            }
            else if (packet.action == PlayerListAction.List)
            {
                ServerLog.Log($"PlayerList: Received player list with {packet.players.Length} entries");

                Multiplayer.session.players.Clear();
                foreach (var info in packet.players)
                {
                    ServerLog.Log($"PlayerList: Adding player from list {info.id}:{info.username}");
                    Multiplayer.session.players.Add(PlayerInfo.FromNet(info));
                }
            }
            else if (packet.action == PlayerListAction.Latencies)
            {
                foreach (var latency in packet.latencies)
                {
                    var player = Multiplayer.session.GetPlayerInfo(latency.playerId);
                    if (player == null)
                    {
                        ServerLog.Log($"PlayerList: Received latency info for unknown player with id {latency.playerId}");
                        continue;
                    }
                    player.latency = latency.latency;
                    player.ticksBehind = latency.ticksBehind;
                    player.simulating = latency.simulating;
                    player.frameTime = latency.frameTime;
                }
            }
            else if (packet.action == PlayerListAction.Status)
            {
                var player = Multiplayer.session.GetPlayerInfo(packet.playerId);
                if (player == null)
                {
                    ServerLog.Log($"PlayerList: Received player status ({packet.status}) for unknown player with id {packet.playerId}");
                }
                else
                {
                    player.status = packet.status;
                }
            }
        }

        [TypedPacketHandler]
        public void HandleChat(ServerChatPacket packet) => Multiplayer.session.AddMsg(packet.msg);

        [TypedPacketHandler]
        public void HandleCursor(ServerCursorPacket packet)
        {
            var player = Multiplayer.session.GetPlayerInfo(packet.playerId);
            if (player == null) return;

            var data = packet.data;
            if (data.seq < player.cursorSeq && player.cursorSeq - data.seq < 128) return;

            player.map = data.map;
            if (data.map == byte.MaxValue) return;

            player.cursorSeq = data.seq;
            player.lastCursor = player.cursor;
            player.lastDelta = Multiplayer.clock.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(data.x, 0, data.z);
            player.updatedAt = Multiplayer.clock.ElapsedMillisDouble();
            player.cursorIcon = data.icon;

            player.dragStart = data.HasDrag ? new Vector3(data.dragX, 0, data.dragZ) : PlayerInfo.Invalid;
        }

        [TypedPacketHandler]
        public void HandleSelected(ServerSelectedPacket packet)
        {
            var player = Multiplayer.session.GetPlayerInfo(packet.playerId);
            if (player == null) return;

            var data = packet.data;
            if (data.reset) player.selectedThings.Clear();

            foreach (var id in data.newlySelectedIds)
                player.selectedThings[id] = Time.realtimeSinceStartup;

            foreach (var id in data.unselectedIds)
                player.selectedThings.Remove(id);
        }

        [TypedPacketHandler]
        public void HandlePing(ServerPingLocPacket packet) => Session.locationPings.ReceivePing(packet);

        [PacketHandler(Packets.Server_MapResponse)]
        public void HandleMapResponse(ByteReader data)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            Session.dataSnapshot.MapCmds[mapId] = mapCmds;

            byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            Session.dataSnapshot.MapData[mapId] = mapData;

            //ClientJoiningState.ReloadGame(TickPatch.tickUntil, Find.Maps.Select(m => m.uniqueID).Concat(mapId).ToList());
            // todo Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
        }

        [TypedPacketHandler]
        public void HandleNotification(ServerNotificationPacket packet)
        {
            var namedArgs = Array.ConvertAll(packet.args, s => (NamedArgument)s);
            var msg = packet.key.Translate(namedArgs);
            Messages.Message(msg, MessageTypeDefOf.SilentInput, false);
            ServerLog.Log($"Notification: {msg} ({packet.key}, {packet.args.Join(", ")})");
        }

        [TypedPacketHandler]
        public void HandleDesyncCheck(ServerSyncInfoPacket packet) =>
            Multiplayer.game?.sync.AddClientOpinionAndCheckDesync(ClientSyncOpinion.FromNet(packet.SyncOpinion));

        [TypedPacketHandler]
        public void HandleFreeze(ServerFreezePacket packet)
        {
            TickPatch.serverFrozen = packet.frozen;
            TickPatch.frozenAt = packet.gameTimer;
        }

        [TypedPacketHandler]
        public void HandleTraces(ServerTracesPacket packet)
        {
            if (packet.mode == ServerTracesPacket.Mode.Request)
            {
                var info = Multiplayer.game.sync.knownClientOpinions.FirstOrDefault(b => b.startTick == packet.tick);
                var response = info?.GetFormattedStackTracesForRange(packet.diffAt) ?? "Traces not available";

                connection.Send(new ClientTracesPacket
                    {
                        playerId = packet.playerId,
                        rawTraces = GZipStream.CompressString(response),
                        rawJittedMethods = GZipStream.CompressString(JittedMethods.GetJittedMethodsString())
                    });
            }
            else if (packet.mode == ServerTracesPacket.Mode.Transfer)
            {
                Multiplayer.session.desyncTracesFromHost = GZipStream.UncompressString(packet.rawTraces);
                Multiplayer.session.jittedMethodsFromHost = GZipStream.UncompressString(packet.rawJittedMethods);
            }
        }

        [TypedPacketHandler]
        public void HandleDebug(ServerDebugPacket _) => Rejoiner.DoRejoin();

        [TypedPacketHandler]
        public void HandleSetFaction(ServerSetFactionPacket packet)
        {
            var playerId = packet.playerId;
            var factionId = packet.factionId;
            Session.GetPlayerInfo(playerId).factionId = factionId;

            if (Session.playerId == playerId)
            {
                Multiplayer.game.ChangeRealPlayerFaction(factionId);
                Session.myFactionId = factionId;
            }
        }
    }

}
