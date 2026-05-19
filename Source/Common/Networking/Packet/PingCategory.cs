using System;

namespace Multiplayer.Common.Networking.Packet;

public enum PingCategory : byte
{
    Default = 0,
    Attack = 1,
    Defend = 2,
    Help = 3,
    Loot = 4,
    Rally = 5,
}

public static class PingCategoryWire
{
    public static readonly int Count = Enum.GetValues(typeof(PingCategory)).Length;
    public const int MaxLabelChars = 64;
    public const int MaxLabelBytes = MaxLabelChars * 4;

    public static bool IsValid(byte raw) => raw < Count;
}

// Marker cap; wire / scribe / UI must agree on the receiver FIFO eviction value.
public static class PingMarkerCap
{
    public const int Default = 50;
    public const int Min = 1;
    public const int Max = 200;

    public static int Clamp(int value)
    {
        if (value < Min) return Min;
        if (value > Max) return Max;
        return value;
    }
}
