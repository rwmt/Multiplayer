using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RimWorld.Planet;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    public class OnMainThread : MonoBehaviour
    {
        private static ActionQueue queue = new();
        private static List<(Action, float)> scheduled = new();

        public void Update()
        {
            try
            {
                Multiplayer.session?.netClient?.PollEvents();
                queue.RunQueue(Log.Error);

                for (int i = scheduled.Count - 1; i >= 0; i--)
                    if (Time.realtimeSinceStartup > scheduled[i].Item2)
                    {
                        scheduled[i].Item1();
                        scheduled.RemoveAt(i);
                    }

                if (SteamManager.Initialized)
                    SteamIntegration.UpdateRichPresence();

                if (Multiplayer.Client == null) return;

                Multiplayer.session.Update();
                SyncFieldUtil.UpdateSync();

                if (!Multiplayer.arbiterInstance && Application.isFocused && !TickPatch.Simulating && !Multiplayer.session.desynced)
                    Multiplayer.session.cursorAndPing.SendVisuals();

                if (Multiplayer.Client is SteamBaseConn steamConn && SteamManager.Initialized)
                    foreach (var packet in SteamIntegration.ReadPackets(steamConn.recvChannel))
                        // Note: receive can lead to disconnection
                        if (steamConn.remoteId == packet.remote && Multiplayer.Client != null)
                            ClientUtil.HandleReceive(packet.data, packet.reliable);
            }
            catch (Exception e)
            {
                Log.Error($"Exception in OnMainThread: {e}");
            }
        }

        public void OnApplicationQuit()
        {
            Multiplayer.StopMultiplayer();
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public static void Schedule(Action action, float time)
        {
            scheduled.Add((action, Time.realtimeSinceStartup + time));
        }
    }

}

