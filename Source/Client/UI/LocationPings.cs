using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
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
                if (WorldRendererUtility.WorldSelected)
                {
                    // Grab the tile under mouse
                    var tile = GenWorld.MouseTile();
                    // If the tile is not valid, snap to expandable world objects (handles orbital locations)
                    if (!tile.Valid)
                        tile = GenWorld.MouseTile(true);

                    // Make sure the tile is valid and that we didn't ping with the mouse outside of map bounds or in space
                    if (tile.Valid)
                        PingLocation(-1, tile, Vector3.zero);
                }
                else if (Find.CurrentMap != null)
                    PingLocation(Find.CurrentMap.uniqueID, PlanetTile.Invalid, UI.MouseMapPosition());
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

    private void PingLocation(int map, PlanetTile tile, Vector3 loc)
    {
        Multiplayer.Client.Send(new ClientPingLocPacket(map, tile.tileId, tile.layerId, loc.x, loc.y, loc.z));
        SoundDefOf.TinyBell.PlayOneShotOnCamera();
    }

    public void ReceivePing(ServerPingLocPacket packet)
    {
        if (!Multiplayer.settings.enablePings) return;

        var data = packet.data;
        var planetTile = new PlanetTile(data.planetTileId, data.planetTileLayer);
        // Return early if both the map and planet tile are invalid
        if (data.mapId == -1 && !planetTile.Valid)
            return;

        pings.RemoveAll(p => p.player == packet.playerId);
        pings.Add(new PingInfo {
            player = packet.playerId,
            mapId = data.mapId,
            planetTile = planetTile,
            mapLoc = new Vector3(data.x, data.y, data.z)
        });
        alertHidden = false;

        if (packet.playerId != Multiplayer.session.playerId)
            SoundDefOf.TinyBell.PlayOneShotOnCamera();
    }
}
