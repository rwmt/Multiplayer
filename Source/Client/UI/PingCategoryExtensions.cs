using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

public static class PingCategoryExtensions
{
    public static readonly PingCategory[] All =
    {
        PingCategory.Default,
        PingCategory.Attack,
        PingCategory.Defend,
        PingCategory.Help,
        PingCategory.Loot,
        PingCategory.Rally,
    };

    public static Color Tint(this PingCategory c) => c switch
    {
        PingCategory.Attack => new Color(1f,    0.25f, 0.25f),
        PingCategory.Defend => new Color(0.4f,  0.6f,  1f   ),
        PingCategory.Help   => new Color(1f,    0.95f, 0.3f ),
        PingCategory.Loot   => new Color(0.4f,  1f,    0.4f ),
        PingCategory.Rally  => new Color(0.85f, 0.55f, 1f   ),
        _                   => Color.white,
    };

    public static string Glyph(this PingCategory c) => c switch
    {
        PingCategory.Attack => "A",
        PingCategory.Defend => "D",
        PingCategory.Help   => "+",
        PingCategory.Loot   => "L",
        PingCategory.Rally  => "R",
        _                   => "",
    };

    // Returns null for Default so the renderer falls back to Glyph().
    public static Texture2D Icon(this PingCategory c) => c switch
    {
        PingCategory.Attack => MultiplayerStatic.PingIconAttack,
        PingCategory.Defend => MultiplayerStatic.PingIconDefend,
        PingCategory.Help   => MultiplayerStatic.PingIconHelp,
        PingCategory.Loot   => MultiplayerStatic.PingIconLoot,
        PingCategory.Rally  => MultiplayerStatic.PingIconRally,
        _                   => null,
    };

    // Normalizes visual size across vanilla icons with varying canvas padding.
    public static float IconScale(this PingCategory c) => c switch
    {
        PingCategory.Attack => 1.20f,
        PingCategory.Defend => 0.92f,
        PingCategory.Help   => 1.00f,
        PingCategory.Loot   => 1.06f,
        PingCategory.Rally  => 0.95f,
        _                   => 1.00f,
    };

    private static string EnglishFallback(PingCategory c) => c switch
    {
        PingCategory.Default => "Ping",
        PingCategory.Attack  => "Attack",
        PingCategory.Defend  => "Defend",
        PingCategory.Help    => "Help",
        PingCategory.Loot    => "Loot",
        PingCategory.Rally   => "Rally",
        _                    => c.ToString(),
    };

    // Dev-mode pseudo-localization mangles missing keys - must go through MpTranslate.Fallback.
    public static string DisplayName(this PingCategory c)
        => MpTranslate.Fallback("MpPingCategory_" + c, EnglishFallback(c));

    // Vanilla SoundDefOf only (no XML). The ?? TinyBell fallback covers the case where a
    // SoundDefOf field is null because RimWorld renamed or removed the underlying Def.
    // Every chosen SoundDef must ship with on-camera subSounds - PlayOneShotOnCamera errors otherwise.
    public static SoundDef Sound(this PingCategory c)
    {
        var picked = c switch
        {
            PingCategory.Attack => SoundDefOf.Quest_Failed,
            PingCategory.Defend => SoundDefOf.DraftOn,
            PingCategory.Help   => SoundDefOf.TutorMessageAppear,
            PingCategory.Loot   => SoundDefOf.ExecuteTrade,
            PingCategory.Rally  => SoundDefOf.Quest_Accepted,
            _                   => SoundDefOf.TinyBell,
        };
        return picked ?? SoundDefOf.TinyBell;
    }
}
