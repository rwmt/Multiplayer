using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{

    public class CursorAndPing
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

        public void SendVisuals()
        {
            if (Time.realtimeSinceStartup - lastCursorSend > 0.05f)
            {
                lastCursorSend = Time.realtimeSinceStartup;
                SendCursor();
            }

            if (Time.realtimeSinceStartup - lastSelectedSend > 0.2f)
            {
                lastSelectedSend = Time.realtimeSinceStartup;
                SendSelected();
            }
        }

        private byte cursorSeq;
        private float lastCursorSend;

        private void SendCursor()
        {
            var writer = new ByteWriter();
            writer.WriteByte(cursorSeq++);

            if (Find.CurrentMap != null && !WorldRendererUtility.WorldRenderedNow)
            {
                writer.WriteByte((byte)Find.CurrentMap.Index);

                var icon = Find.MapUI?.designatorManager?.SelectedDesignator?.icon;
                int iconId = icon == null ? 0 : !MultiplayerData.icons.Contains(icon) ? 0 : MultiplayerData.icons.IndexOf(icon);
                writer.WriteByte((byte)iconId);

                writer.WriteVectorXZ(UI.MouseMapPosition());

                if (Find.Selector.dragBox.IsValidAndActive)
                    writer.WriteVectorXZ(Find.Selector.dragBox.start);
                else
                    writer.WriteShort(-1);
            }
            else
            {
                writer.WriteByte(byte.MaxValue);
            }

            Multiplayer.Client.Send(Packets.Client_Cursor, writer.ToArray(), reliable: false);
        }

        private HashSet<int> lastSelected = new();
        private float lastSelectedSend;
        private int lastMap;

        private void SendSelected()
        {
            if (Current.ProgramState != ProgramState.Playing) return;

            var writer = new ByteWriter();

            int mapId = Find.CurrentMap?.Index ?? -1;
            if (WorldRendererUtility.WorldRenderedNow) mapId = -1;

            bool reset = false;

            if (mapId != lastMap)
            {
                reset = true;
                lastMap = mapId;
                lastSelected.Clear();
            }

            var selected = new HashSet<int>(Find.Selector.selected.OfType<Thing>().Select(t => t.thingIDNumber));

            var add = new List<int>(selected.Except(lastSelected));
            var remove = new List<int>(lastSelected.Except(selected));

            if (!reset && add.Count == 0 && remove.Count == 0) return;

            writer.WriteBool(reset);
            writer.WritePrefixedInts(add);
            writer.WritePrefixedInts(remove);

            lastSelected = selected;

            Multiplayer.Client.Send(Packets.Client_Selected, writer.ToArray());
        }
    }


    public class PingInfo
    {
        public int player;
        public int mapId; // Map id or -1 for planet
        public int planetTile;
        public Vector3 mapLoc;

        public PlayerInfo PlayerInfo => Multiplayer.session.GetPlayerInfo(player);

        public float y = 1f;
        private float v = -3f;
        private float lastTime = Time.time;
        private float bounceAt = Time.time + 2;
        public float timeAlive;

        private float AlphaMult => 1f - Mathf.Clamp01(timeAlive - (PingDuration - 1f));

        public GlobalTargetInfo? Target
        {
            get
            {
                if (mapId == -1)
                    return new GlobalTargetInfo(planetTile);

                if (Find.Maps.GetById(mapId) is { } map)
                    return new GlobalTargetInfo(mapLoc.ToIntVec3(), map);

                return null;
            }
        }

        const float PingDuration = 10f;

        public bool Update()
        {
            float delta = Mathf.Min(Time.time - lastTime, 0.05f);
            lastTime = Time.time;

            v -= 8f * delta;
            y += v * delta;

            if (y < 0)
            {
                y = 0;
                v = Math.Max(-v / 2f - 0.5f, 0);
            }

            if (Mathf.Abs(v) < 0.0001f && y < 0.05f)
            {
                v = 0f;
                y = 0f;
            }

            if (bounceAt != 0 && Time.time > bounceAt)
            {
                v = 3f;
                bounceAt = Time.time + 2;
            }

            timeAlive += delta;

            return timeAlive > PingDuration;
        }

        public void DrawAt(Vector2 screenCenter, Color baseColor, float size)
        {
            var colorAlpha = baseColor;
            colorAlpha.a = 0.5f * AlphaMult;

            using (MpStyle.Set(colorAlpha))
                GUI.DrawTexture(
                    new Rect(screenCenter - new Vector2(size / 2 - 1, size / 2), new(size, size)),
                    MultiplayerStatic.PingBase
                );

            var color = baseColor;
            color.a = AlphaMult;

            using (MpStyle.Set(color))
                GUI.DrawTexture(
                    new Rect(screenCenter - new Vector2(size / 2, size + y * size), new(size, size)),
                    MultiplayerStatic.PingPin
                );
        }
    }
}
