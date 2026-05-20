using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    // Player-placeable ping/marker category. Defined in XML so any mod can drop in additional
    // categories without touching the C#; the wheel and selection UI pick these up automatically.
    //
    // One def in the database is flagged <isDefault>true</isDefault>: that's the "untyped" centre
    // option (no slice, lives in the wheel's cancel disc). Vanilla ships MpPing_Default for that
    // slot - mods should add new categories rather than replace it.
    public class MultiplayerPingDef : Def
    {
        // Marks the singleton "no category picked" entry that lives in the centre of the wheel.
        // Exactly one def in the database should have this set. If two defs flag themselves as
        // default, ResolveDefault picks the first one it encounters; if none do, it falls back to
        // the lowest-ordered def so the wheel still draws something.
        public bool isDefault;

        // Sort key - lower comes first on the wheel and in lists. Use multiples of 100 in vanilla to
        // leave room for mods to slot themselves between defaults without renumbering everything.
        // Ties broken by defName (ordinal) for cross-client determinism.
        public int order = 1000;

        // Slice / pin tint. Default-category gets ignored at render time (uses placer colour instead).
        public Color tint = Color.white;

        // Wheel-slice icon. Resolved against ContentFinder lazily on first access so the static
        // ctor doesn't run before content is mounted.
        public string iconPath;

        // Normalises visual size across vanilla atlases with varying canvas padding.
        public float iconScale = 1f;

        // One-character fallback drawn in place of the icon when iconPath is missing/unresolvable.
        public string glyph = "";

        // Vanilla SoundDef defName, e.g. "Quest_Failed". Falls back to TinyBell when null/missing.
        public string soundDefName;

        // Lazily resolved; ContentFinder requires the asset bundles to be live and Texture2D fields
        // can't be set from the def loader directly. Null-safe - callers check IconTexture != null.
        private Texture2D resolvedIcon;
        private bool iconResolved;

        public Texture2D IconTexture
        {
            get
            {
                if (iconResolved) return resolvedIcon;
                iconResolved = true;
                if (string.IsNullOrEmpty(iconPath)) return resolvedIcon = null;
                // reportFailure: false so the wheel falls back to glyph silently when the path's bad.
                resolvedIcon = ContentFinder<Texture2D>.Get(iconPath, false);
                return resolvedIcon;
            }
        }

        private SoundDef resolvedSound;
        private bool soundResolved;

        public SoundDef Sound
        {
            get
            {
                if (soundResolved) return resolvedSound;
                soundResolved = true;
                if (!string.IsNullOrEmpty(soundDefName))
                    resolvedSound = SoundDef.Named(soundDefName);
                // SoundDef.Named throws on missing - guarded above. TinyBell is the universal fallback
                // (defined in vanilla, always present, has on-camera subSounds).
                return resolvedSound ?? SoundDefOf.TinyBell;
            }
        }

        public override void PostLoad()
        {
            base.PostLoad();
            // Defs without a label fall back to the defName for display, which reads as
            // "MpPing_Attack" in the UI. Catch that here so vanilla labels aren't required to look up
            // a fallback at every site.
            if (string.IsNullOrEmpty(label))
                label = defName;
        }

        // Cached on first access. Changing the active mod list requires a full process restart in
        // RimWorld, which resets statics, so no explicit invalidation is needed.
        private static MultiplayerPingDef cachedDefault;

        public static MultiplayerPingDef Default
        {
            get
            {
                if (cachedDefault != null) return cachedDefault;
                return cachedDefault = ResolveDefault();
            }
        }

        private static MultiplayerPingDef ResolveDefault()
        {
            MultiplayerPingDef fallback = null;
            foreach (var def in DefDatabase<MultiplayerPingDef>.AllDefsListForReading)
            {
                if (def.isDefault) return def;
                if (fallback == null || def.order < fallback.order) fallback = def;
            }
            // If no def is flagged isDefault, take the lowest-ordered as a softer fallback so the
            // wheel still draws something. DefDatabase is empty only in dev/test contexts.
            return fallback;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (iconScale <= 0f) yield return $"{defName}: iconScale must be > 0";
            // Vanilla iconScale runs 0.92..1.20. Anything above 4 is almost certainly a missing
            // decimal point (e.g. "1.20" mistyped as "120"); warn rather than crash at draw time.
            if (iconScale > 4f)
                yield return $"{defName}: iconScale {iconScale} looks like a typo (vanilla range is 0.9 to 1.2)";
            if (!string.IsNullOrEmpty(glyph) && glyph.Length > 4)
                yield return $"{defName}: glyph is intended to be a single character (got '{glyph}')";
        }

        // Sorted by (order, defName) for cross-client determinism. Default lives in the centre disc
        // and is filtered out of every selection UI - callers should pass `includeDefault: false`.
        public static List<MultiplayerPingDef> Sorted(bool includeDefault)
        {
            var list = new List<MultiplayerPingDef>();
            foreach (var def in DefDatabase<MultiplayerPingDef>.AllDefsListForReading)
            {
                if (!includeDefault && def.isDefault) continue;
                list.Add(def);
            }
            list.Sort(Compare);
            return list;
        }

        public static int Compare(MultiplayerPingDef a, MultiplayerPingDef b)
        {
            if (a.order != b.order) return a.order.CompareTo(b.order);
            return string.CompareOrdinal(a.defName, b.defName);
        }
    }
}
