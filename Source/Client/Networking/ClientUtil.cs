using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Common;
using Multiplayer.Client.EarlyPatches;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.ComponentModel;
using Verse;
using UnityEngine;
using System.IO;
using Multiplayer.Client.Networking;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static void TryConnectWithWindow(string address, int port, bool returnToServerBrowser = true)
        {
            Find.WindowStack.Add(new ConnectingWindow(address, port) { returnToServerBrowser = returnToServerBrowser });

            Multiplayer.session = new MultiplayerSession
            {
                address = address,
                port = port
            };

            NetManager netClient = new NetManager(new MpClientNetListener())
            {
                EnableStatistics = true,
                IPv6Enabled = IPv6Mode.Disabled
            };

            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address, port, "");
        }

        public static void TrySteamConnectWithWindow(CSteamID user, bool returnToServerBrowser = true)
        {
            Log.Message("Connecting through Steam");

            Multiplayer.session = new MultiplayerSession
            {
                client = new SteamClientConn(user) { username = Multiplayer.username },
                steamHost = user
            };

            Find.WindowStack.Add(new SteamConnectingWindow(user) { returnToServerBrowser = returnToServerBrowser });

            Multiplayer.session.ReapplyPrefs();
            Multiplayer.Client.State = ConnectionStateEnum.ClientSteam;
        }

        public static void HandleReceive(ByteReader data, bool reliable)
        {
            try
            {
                Multiplayer.Client.HandleReceive(data, reliable);
            }
            catch (Exception e)
            {
                Log.Error($"Exception handling packet by {Multiplayer.Client}: {e}");

                Multiplayer.session.disconnectInfo.titleTranslated = "MpPacketErrorLocal".Translate();

                ConnectionStatusListeners.TryNotifyAll_Disconnected();
                Multiplayer.StopMultiplayer();
            }
        }
    }

}
