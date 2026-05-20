using Multiplayer.Common.Networking.Packet;
using Verse;

namespace Multiplayer.Client
{
    // Wire-side helpers around MultiplayerPingDef. The visual properties (tint / icon / glyph /
    // sound) live on the def itself now - this class is just the ushort short-hash codec plus a
    // couple of null-safe accessors.
    public static class PingCategoryExtensions
    {
        // Receivers map an unknown / zero hash to Default so a sender that joins with extra mods
        // doesn't desync the wheel UI on a stripped-down client. A missing Default def still
        // returns null and the renderer treats null as "untyped".
        public static MultiplayerPingDef ResolveFromWire(ushort hash)
        {
            if (hash == PingCategoryWire.UnknownHash) return MultiplayerPingDef.Default;
            var def = DefDatabase<MultiplayerPingDef>.GetByShortHash(hash);
            return def ?? MultiplayerPingDef.Default;
        }

        // Senders pass the def's short-hash; resolved-to-null defs (caller didn't have one in the
        // DefDatabase, e.g. mid-startup) emit UnknownHash so the receiver picks Default.
        public static ushort ToWire(MultiplayerPingDef def)
            => def?.shortHash ?? PingCategoryWire.UnknownHash;

        public static string DisplayName(this MultiplayerPingDef def)
            => def?.LabelCap ?? "";
    }
}
