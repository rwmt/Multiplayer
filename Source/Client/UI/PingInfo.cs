using System;
using Multiplayer.Client.Util;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

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
