using System;
using System.Collections.Generic;
using System.Diagnostics;
using LudeonTK;
using Multiplayer.Client.Networking;
using Multiplayer.Client.Util;
using Multiplayer.Client.Windows;
using Multiplayer.Common;
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
        private static Callback<AvatarImageLoaded_t> avatarLoaded;

        public static AppId_t RimWorldAppId;

        private const string SteamConnectStart = " -mpserver=";

        public static void InitCallbacks()
        {
            RimWorldAppId = SteamUtils.GetAppID();

            sessionReq = Callback<P2PSessionRequest_t>.Create(req =>
            {
                ServerLog.Log($"Received P2P session request from {req.m_steamIDRemote}");
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
                    ClientUtil.TryConnectWithWindow(ConnectorRegistry.Steam(req.m_steamIDFriend), false);
                }
                else
                {
                    Messages.Message("MpQuitBeforeAcceptInvite".Translate(), MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            });

            personaChange = Callback<PersonaStateChange_t>.Create(change =>
            {
                // When a persona's avatar changes, the avatar id changes too. It's not a problem for us because we
                // query the avatar id every frame. It'd be nice to remove the old avatar from the SteamImages cache,
                // but realistically it's not an issue. (Also, it'd require keeping track of the avatar's owner because
                // I don't think there's a way to query the old avatar id to easily remove it.)
            });

            avatarLoaded =
                Callback<AvatarImageLoaded_t>.Create(loaded => SteamImages.GetTexture(loaded.m_iImage, force: true));
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
        private static readonly Dictionary<int, Texture2D> Cache = new();

        [DebugAction(category = MpDebugActions.MultiplayerCategory, name = "Clear image cache",
            allowedGameStates = AllowedGameStates.Entry)]
        private static void ClearCache()
        {
            foreach (var tex in Cache.Values) UnityEngine.Object.Destroy(tex);
            Cache.Clear();
        }

        public static Texture2D GetTexture(int id, bool force = false)
        {
            if (Cache.TryGetValue(id, out Texture2D tex) && !force)
                return tex;

            if (!SteamUtils.GetImageSize(id, out uint width, out uint height))
            {
                Cache[id] = null;
                return null;
            }

            uint sizeInBytes = width * height * 4;
            byte[] data = new byte[sizeInBytes];

            if (!SteamUtils.GetImageRGBA(id, data, (int)sizeInBytes))
            {
                Cache[id] = null;
                return null;
            }

            tex = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(data);
            FlipVertically(tex);
            tex.Apply();

            if (Cache.TryGetValue(id, out var oldTex)) UnityEngine.Object.Destroy(oldTex);
            Cache[id] = tex;

            return tex;
        }

        private static void FlipVertically(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            var buf = new Color32[tex.width];

            for (int y = 0; y < tex.height / 2; y++)
            {
                var reversedY = tex.height - y - 1;
                Array.Copy(pixels, y * tex.width, buf, 0, tex.width);
                Array.Copy(pixels, reversedY * tex.width, pixels, y * tex.width, tex.width);
                Array.Copy(buf, 0, pixels, reversedY * tex.width, tex.width);
            }

            tex.SetPixels32(pixels);
        }
    }

}
