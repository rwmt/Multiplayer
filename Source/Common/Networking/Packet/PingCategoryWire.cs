namespace Multiplayer.Common.Networking.Packet
{
    // Wire-side constants for ping categories. The category identifier itself is a ushort short-hash
    // resolved client-side against DefDatabase<MultiplayerPingDef>; this file just holds the shared
    // label-length limits and the "unknown" sentinel.
    //
    // Common/ is RimWorld-blind, so we can't reference MultiplayerPingDef here - clients map the
    // ushort back to a Def themselves.
    public static class PingCategoryWire
    {
        public const int MaxLabelChars = 64;
        public const int MaxLabelBytes = MaxLabelChars * 4;

        // 0 means "unknown / fall back to Default" on the receiver. Real defs always hash to non-zero
        // via Verse.GenText.StableStringHash + the short-hash routine, so a packet with category == 0
        // is either a legacy probe or a sender that couldn't resolve its own def.
        public const ushort UnknownHash = 0;
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
}
