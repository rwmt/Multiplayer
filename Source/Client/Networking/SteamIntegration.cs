using System;
using System.Collections.Generic;
using System.Diagnostics;
using Multiplayer.Client.Networking;
using Multiplayer.Client.Windows;
using RimWorld;
using Steamworks;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class SteamIntegration
    {
        // Callbacks stored in static fields so they don't get garbage collected
        private static Callback<P2PSessionRequest_t> sessionReq;
        private static Callback<FriendRichPresenceUpdate_t> friendRchpUpdate;
        private static Callback<GameRichPresenceJoinRequested_t> gameJoinReq;
        private static Callback<PersonaStateChange_t> personaChange;

        public static AppId_t RimWorldAppId;

        private const string SteamConnectStart = " -mpserver=";

        public static void InitCallbacks()
        {
            RimWorldAppId = SteamUtils.GetAppID();

            sessionReq = Callback<P2PSessionRequest_t>.Create(req =>
            {
                var session = Multiplayer.session;
                if (Multiplayer.LocalServer?.settings.steam == true && !session.pendingSteam.Contains(req.m_steamIDRemote))
                {
                    if (Multiplayer.settings.autoAcceptSteam)
                        SteamNetworking.AcceptP2PSessionWithUser(req.m_steamIDRemote);
                    else
                    {
                        session.pendingSteam.Add(req.m_steamIDRemote);
                        PendingPlayerWindow.EnqueueJoinRequest(req.m_steamIDRemote, (joinReq, accepted) =>
                        {
                            if(joinReq.steamId.HasValue && accepted) AcceptPlayerJoinRequest(joinReq.steamId.Value);
                        });
                    }
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
                if (Current.Game == null)
                {
                    ClientUtil.TrySteamConnectWithWindow(req.m_steamIDFriend, false);
                }
                else
                {
                    Messages.Message("MpQuitBeforeAcceptInvite".Translate(), MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            });

            personaChange = Callback<PersonaStateChange_t>.Create(change =>
            {
            });
        }

        public static void AcceptPlayerJoinRequest(CSteamID id)
        {
            SteamNetworking.AcceptP2PSessionWithUser(id);
            Multiplayer.session.pendingSteam.Remove(id);

            Messages.Message("MpSteamAccepted".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }

        private static Stopwatch lastSteamUpdate = Stopwatch.StartNew();
        private static bool lastLocalSteam; // running a server with steam networking
        private static CSteamID? lastRemoteSteam; // connected to a server with steam networking

        public static void UpdateRichPresence()
        {
            if (lastSteamUpdate.ElapsedMilliseconds < 1000) return;

            var localSteam = Multiplayer.LocalServer?.settings.steam ?? false;
            var remoteSteam = (Multiplayer.Client as SteamClientConn)?.remoteId;
            if (localSteam != lastLocalSteam || remoteSteam != lastRemoteSteam)
            {
                string connect;
                if (localSteam) connect = SteamUser.GetSteamID().ToString();
                else if (remoteSteam != null) connect = remoteSteam.ToString();
                else connect = null;

                // Null and empty string mentioned in the docs doesn't seem to work
                SteamFriends.SetRichPresence("connect", connect != null ? $"{SteamConnectStart}{connect}" : "nil");

                lastLocalSteam = localSteam;
                lastRemoteSteam = remoteSteam;
            }

            lastSteamUpdate.Restart();
        }

        /// Gets the Steam ID of the user hosting the server that the friend is now playing on.
        public static CSteamID GetConnectHostId(CSteamID friend)
        {
            string connectValue = SteamFriends.GetFriendRichPresence(friend, "connect");
            if (connectValue?.StartsWith(SteamConnectStart, StringComparison.OrdinalIgnoreCase) == true &&
                ulong.TryParse(connectValue[SteamConnectStart.Length..], out ulong hostId))
            {
                return (CSteamID)hostId;
            }

            return CSteamID.Nil;
        }
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
