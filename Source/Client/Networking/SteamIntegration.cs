using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Steamworks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class SteamIntegration
    {
        // Callbacks stored in static fields so they don't get garbage collected
        private static Callback<P2PSessionRequest_t> sessionReq;
        private static Callback<P2PSessionConnectFail_t> p2pFail;
        private static Callback<FriendRichPresenceUpdate_t> friendRchpUpdate;
        private static Callback<GameRichPresenceJoinRequested_t> gameJoinReq;
        private static Callback<PersonaStateChange_t> personaChange;

        public static AppId_t RimWorldAppId;

        public const string SteamConnectStart = " -mpserver=";

        public static void InitCallbacks()
        {
            RimWorldAppId = SteamUtils.GetAppID();

            sessionReq = Callback<P2PSessionRequest_t>.Create(req =>
            {
                var session = Multiplayer.session;
                if (session?.localServerSettings != null && session.localServerSettings.steam && !session.pendingSteam.Contains(req.m_steamIDRemote))
                {
                    if (Multiplayer.settings.autoAcceptSteam)
                        SteamNetworking.AcceptP2PSessionWithUser(req.m_steamIDRemote);
                    else
                        session.pendingSteam.Add(req.m_steamIDRemote);

                    session.knownUsers.Add(req.m_steamIDRemote);
                    session.NotifyChat();

                    SteamFriends.RequestUserInformation(req.m_steamIDRemote, true);
                }
            });

            friendRchpUpdate = Callback<FriendRichPresenceUpdate_t>.Create(update =>
            {
            });

            gameJoinReq = Callback<GameRichPresenceJoinRequested_t>.Create(req =>
            {
            });

            personaChange = Callback<PersonaStateChange_t>.Create(change =>
            {
            });

            p2pFail = Callback<P2PSessionConnectFail_t>.Create(fail =>
            {
                var session = Multiplayer.session;
                if (session == null) return;

                var remoteId = fail.m_steamIDRemote;
                var error = (EP2PSessionError)fail.m_eP2PSessionError;

                if (Multiplayer.Client is SteamBaseConn clientConn && clientConn.remoteId == remoteId)
                    clientConn.OnError(error);

                var server = Multiplayer.LocalServer;
                if (server == null) return;

                server.Enqueue(() =>
                {
                    var conn = server.playerManager.Players.Select(p => p.conn).OfType<SteamBaseConn>().FirstOrDefault(c => c.remoteId == remoteId);
                    if (conn != null)
                        conn.OnError(error);
                });
            });
        }

        public static IEnumerable<SteamPacket> ReadPackets(int recvChannel)
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, recvChannel))
            {
                byte[] data = new byte[size];

                if (!SteamNetworking.ReadP2PPacket(data, size, out uint sizeRead, out CSteamID remote, recvChannel)) continue;
                if (data.Length <= 0) continue;

                var reader = new ByteReader(data);
                byte flags = reader.ReadByte();
                bool joinPacket = (flags & 1) > 0;
                bool reliable = (flags & 2) > 0;
                bool hasChannel = (flags & 4) > 0;
                ushort channel = hasChannel ? reader.ReadUShort() : (ushort)0;

                yield return new SteamPacket() {
                    remote = remote, data = reader, joinPacket = joinPacket, reliable = reliable, channel = channel
                };
            }
        }

        public static void ServerSteamNetTick(MultiplayerServer server)
        {
            foreach (var packet in ReadPackets(0))
            {
                var playerManager = server.playerManager;
                var player = playerManager.Players.FirstOrDefault(p => p.conn is SteamBaseConn conn && conn.remoteId == packet.remote);

                if (packet.joinPacket && player == null)
                {
                    ConnectionBase conn = new SteamServerConn(packet.remote, packet.channel);

                    var preConnect = playerManager.OnPreConnect(packet.remote);
                    if (preConnect != null)
                    {
                        conn.Close(preConnect.Value);
                        continue;
                    }

                    conn.State = ConnectionStateEnum.ServerJoining;
                    player = playerManager.OnConnected(conn);
                    player.type = PlayerType.Steam;

                    player.steamId = (ulong)packet.remote;
                    player.steamPersonaName = SteamFriends.GetFriendPersonaName(packet.remote);
                    if (player.steamPersonaName.Length == 0)
                        player.steamPersonaName = "[unknown]";

                    conn.Send(Packets.Server_SteamAccept);
                }

                if (!packet.joinPacket && player != null)
                {
                    player.HandleReceive(packet.data, packet.reliable);
                }
            }
        }

        private static Stopwatch lastSteamUpdate = Stopwatch.StartNew();
        private static bool lastSteam;

        public static void UpdateRichPresence()
        {
            if (lastSteamUpdate.ElapsedMilliseconds < 1000) return;

            bool steam = Multiplayer.session?.localServerSettings?.steam ?? false;

            if (steam != lastSteam)
            {
                if (steam)
                    SteamFriends.SetRichPresence("connect", $"{SteamConnectStart}{SteamUser.GetSteamID()}");
                else
                    // Null and empty string mentioned in the docs don't seem to work
                    SteamFriends.SetRichPresence("connect", "nil");

                lastSteam = steam;
            }

            lastSteamUpdate.Restart();
        }
    }

    public struct SteamPacket
    {
        public CSteamID remote;
        public ByteReader data;
        public bool joinPacket;
        public bool reliable;
        public ushort channel;
    }

    public static class SteamImages
    {
        public static Dictionary<int, Texture2D> cache = new();

        // Remember to flip it
        public static Texture2D GetTexture(int id)
        {
            if (cache.TryGetValue(id, out Texture2D tex))
                return tex;

            if (!SteamUtils.GetImageSize(id, out uint width, out uint height))
            {
                cache[id] = null;
                return null;
            }

            uint sizeInBytes = width * height * 4;
            byte[] data = new byte[sizeInBytes];

            if (!SteamUtils.GetImageRGBA(id, data, (int)sizeInBytes))
            {
                cache[id] = null;
                return null;
            }

            tex = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(data);
            tex.Apply();

            cache[id] = tex;

            return tex;
        }
    }

}
