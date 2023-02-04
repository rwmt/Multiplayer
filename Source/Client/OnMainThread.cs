using Multiplayer.Client.Networking;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    public class OnMainThread : MonoBehaviour
    {
        private static ActionQueue queue = new();
        private static List<(Action action, float atTime)> scheduled = new();

        public void Update()
        {
            // Changing scene
            if (LongEventHandler.currentEvent is { levelToLoad: not null })
                return;

            MpInput.Update();

            try
            {
                Multiplayer.session?.netClient?.PollEvents();
            }
            catch (Exception e)
            {
                Log.Error($"Exception handling network events: {e}");
            }

            queue.RunQueue(Log.Error);
            RunScheduled();

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

        public void OnApplicationQuit()
        {
            Multiplayer.StopMultiplayer();
        }

        private static void RunScheduled()
        {
            for (int i = scheduled.Count - 1; i >= 0; i--)
            {
                try
                {
                    var item = scheduled[i];
                    if (Time.realtimeSinceStartup > item.atTime)
                    {
                        scheduled.RemoveAt(i);
                        item.action();
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Exception running scheduled action: {e}");
                }

                if (scheduled.Count == 0) // The action can call ClearScheduled
                    return;
            }
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public static void Schedule(Action action, float time)
        {
            scheduled.Add((action, Time.realtimeSinceStartup + time));
        }

        public static void ClearScheduled()
        {
            scheduled.Clear();
        }
    }

}

