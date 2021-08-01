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
        public static void TryConnect(string address, int port)
        {
            Multiplayer.session = new MultiplayerSession();

            // todo
            //Multiplayer.session.mods.remoteAddress = address;
            //Multiplayer.session.mods.remotePort = port;

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

        public static void HandleReceive(ByteReader data, bool reliable)
        {
            try
            {
                Multiplayer.Client.HandleReceive(data, reliable);
            }
            catch (Exception e)
            {
                Log.Error($"Exception handling packet by {Multiplayer.Client}: {e}");

                Multiplayer.session.disconnectReasonTranslated = "MpPacketError".Translate();
                Multiplayer.session.disconnectInfoTranslated = "MpPacketErrorInfo".Translate();

                ConnectionStatusListeners.TryNotifyAll_Disconnected();
                OnMainThread.StopMultiplayer();
            }
        }
    }

}
