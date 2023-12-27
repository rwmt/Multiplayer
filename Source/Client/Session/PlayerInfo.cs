using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Common;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class PlayerInfo : IPlayerInfo
{
    public static readonly Vector3 Invalid = new(-1, 0, -1);

    public int id;
    public string username;
    public int latency;
    public int ticksBehind;
    public bool simulating;
    public float frameTime;
    public PlayerType type;
    public PlayerStatus status;
    public Color color;
    public int factionId;

    public ulong steamId;
    public string steamPersonaName;

    public byte cursorSeq;
    public byte map = byte.MaxValue;
    public Vector3 cursor;
    public Vector3 lastCursor;
    public double updatedAt;
    public double lastDelta;
    public byte cursorIcon;
    public Vector3 dragStart = Invalid;

    public Dictionary<int, float> selectedThings = new();

    public int Id => id;
    public string Username => username;
    public bool IsArbiter => type == PlayerType.Arbiter;
    public int CurrentMapIndex => map;
    public Map CurrentMap => Find.Maps.Find(m => m.Index == map);
    public IReadOnlyList<int> SelectedThingsByIds => selectedThings
        .Select(x => x.Key)
        .ToList()
        .AsReadOnly();
    public IReadOnlyList<Thing> SelectedThings => selectedThings
        .Select(x => ThingsById.thingsById.TryGetValue(x.Key, out var thing) ? thing : null)
        .Where(x => x != null)
        .ToList()
        .AsReadOnly();

    private PlayerInfo(int id, string username, int latency, PlayerType type)
    {
        this.id = id;
        this.username = username;
        this.latency = latency;
        this.type = type;
    }

    public static PlayerInfo Read(ByteReader data)
    {
        int id = data.ReadInt32();
        string username = data.ReadString();
        int latency = data.ReadInt32();
        var type = (PlayerType)data.ReadByte();
        var status = (PlayerStatus)data.ReadByte();

        var steamId = data.ReadULong();
        var steamName = data.ReadString();

        var ticksBehind = data.ReadInt32();
        var simulating = data.ReadBool();

        var color = new Color(data.ReadByte() / 255f, data.ReadByte() / 255f, data.ReadByte() / 255f);

        int factionId = data.ReadInt32();

        return new PlayerInfo(id, username, latency, type)
        {
            status = status,
            steamId = steamId,
            steamPersonaName = steamName,
            color = color,
            ticksBehind = ticksBehind,
            simulating = simulating,
            factionId = factionId
        };
    }
}
