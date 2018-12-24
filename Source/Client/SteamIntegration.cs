using Multiplayer.Common;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Multiplayer.Client
{
    public static class SteamIntegration
    {
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
                if (session?.localSettings != null && session.localSettings.steam && !session.pendingSteam.Contains(req.m_steamIDRemote))
                {
                    if (MultiplayerMod.settings.autoAcceptSteam)
                        SteamNetworking.AcceptP2PSessionWithUser(req.m_steamIDRemote);
                    else
                        session.pendingSteam.Add(req.m_steamIDRemote);

                    session.knownUsers.Add(req.m_steamIDRemote);
                    session.hasUnread = true;

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
                    var conn = server.players.Select(p => p.conn).OfType<SteamBaseConn>().FirstOrDefault(c => c.remoteId == remoteId);
                    if (conn != null)
                        conn.OnError(error);
                });
            });
        }

        public static IEnumerable<SteamPacket> ReadPackets()
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, 0))
            {
                byte[] data = new byte[size];
                SteamNetworking.ReadP2PPacket(data, size, out uint sizeRead, out CSteamID remote, 0);

                if (data.Length <= 0) continue;

                var reader = new ByteReader(data);
                byte info = reader.ReadByte();
                bool joinPacket = (info & 1) > 0;
                bool reliable = (info & 2) > 0;

                yield return new SteamPacket() { remote = remote, data = reader, joinPacket = joinPacket, reliable = reliable };
            }
        }

        public static void ClearChannel(int channel)
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, channel))
                SteamNetworking.ReadP2PPacket(new byte[size], size, out uint sizeRead, out CSteamID remote, channel);
        }

        public static void ServerSteamNetTick(MultiplayerServer server)
        {
            foreach (var packet in ReadPackets())
            {
                if (packet.joinPacket)
                    ClearChannel(0);

                var player = server.players.FirstOrDefault(p => p.conn is SteamBaseConn conn && conn.remoteId == packet.remote);

                if (packet.joinPacket && player == null)
                {
                    IConnection conn = new SteamServerConn(packet.remote);
                    conn.State = ConnectionStateEnum.ServerJoining;
                    player = server.OnConnected(conn);
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

        public static void UpdateRichPresence()
        {
            if (lastSteamUpdate.ElapsedMilliseconds < 1000) return;

            if (Multiplayer.session?.localSettings?.steam ?? false)
                SteamFriends.SetRichPresence("connect", $"{SteamConnectStart}{SteamUser.GetSteamID()}");
            else
                SteamFriends.SetRichPresence("connect", null);

            lastSteamUpdate.Restart();
        }
    }

    public struct SteamPacket
    {
        public CSteamID remote;
        public ByteReader data;
        public bool joinPacket;
        public bool reliable;
    }

    public static class SteamImages
    {
        public static Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();

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
