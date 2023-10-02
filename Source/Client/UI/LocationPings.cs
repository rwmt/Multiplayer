using System.Collections.Generic;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

public class LocationPings
{
    public List<PingInfo> pings = new();
    public bool alertHidden;
    private int pingJumpCycle;

    public void UpdatePing()
    {
        var pingsEnabled = !TickPatch.Simulating && Multiplayer.settings.enablePings;

        if (pingsEnabled)
            if (MultiplayerStatic.PingKeyDef.JustPressed || KeyDown(Multiplayer.settings.sendPingButton))
            {
                if (WorldRendererUtility.WorldRenderedNow)
                    PingLocation(-1, GenWorld.MouseTile(), Vector3.zero);
                else if (Find.CurrentMap != null)
                    PingLocation(Find.CurrentMap.uniqueID, 0, UI.MouseMapPosition());
            }

        for (int i = pings.Count - 1; i >= 0; i--)
        {
            var ping = pings[i];

            if (ping.Update() || ping.PlayerInfo == null || ping.Target == null)
                pings.RemoveAt(i);
        }

        if (pingsEnabled && KeyDown(Multiplayer.settings.jumpToPingButton))
        {
            pingJumpCycle++;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                alertHidden = true;
            else if (pings.Any())
                // ReSharper disable once PossibleInvalidOperationException
                CameraJumper.TryJumpAndSelect(pings[GenMath.PositiveMod(pingJumpCycle, pings.Count)].Target.Value);
        }
    }

    private static bool KeyDown(KeyCode? keyNullable)
    {
        if (keyNullable is not { } key) return false;

        if (keyNullable == KeyCode.Mouse2)
            return MpInput.Mouse2UpWithoutDrag;

        return Input.GetKeyDown(key);
    }

    private void PingLocation(int map, int tile, Vector3 loc)
    {
        var writer = new ByteWriter();
        writer.WriteInt32(map);
        writer.WriteInt32(tile);
        writer.WriteFloat(loc.x);
        writer.WriteFloat(loc.y);
        writer.WriteFloat(loc.z);
        Multiplayer.Client.Send(Packets.Client_PingLocation, writer.ToArray());

        SoundDefOf.TinyBell.PlayOneShotOnCamera();
    }

    public void ReceivePing(int player, int map, int tile, Vector3 loc)
    {
        if (!Multiplayer.settings.enablePings) return;

        pings.RemoveAll(p => p.player == player);
        pings.Add(new PingInfo { player = player, mapId = map, planetTile = tile, mapLoc = loc });
        alertHidden = false;

        if (player != Multiplayer.session.playerId)
            SoundDefOf.TinyBell.PlayOneShotOnCamera();
    }
}
