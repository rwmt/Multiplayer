using UnityEngine;
using Verse;

namespace Multiplayer.Client.Util
{
    public enum WordWrap
    {
        NoWrap, DoWrap
    }

    public ref struct MpStyle
    {
        public bool? wrap;
        public TextAnchor? anchor;
        public Color? color;
        public GameFont? font;

        public void Dispose()
        {
            if (wrap is { } w)
                Text.WordWrap = w;

            if (anchor is { } a)
                Text.Anchor = a;

            if (color is { } c)
                GUI.color = c;

            if (font is { } f)
                Text.Font = f;
        }

        public static MpStyle Set(WordWrap wrap)
        {
            var prev = Text.WordWrap;
            Text.WordWrap = wrap == WordWrap.DoWrap;
            return new() { wrap = prev };
        }

        public static MpStyle Set(TextAnchor anchor)
        {
            var prev = Text.Anchor;
            Text.Anchor = anchor;
            return new() { anchor = prev };
        }

        public static MpStyle Set(Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            return new() { color = prev };
        }

        public static MpStyle Set(GameFont font)
        {
            var prev = Text.Font;
            Text.Font = font;
            return new() { font = prev };
        }
    }

    public static class MpStyleExtensions
    {
        public static MpStyle Set(this MpStyle style, WordWrap wrap)
        {
            var prev = Text.WordWrap;
            Text.WordWrap = wrap == WordWrap.DoWrap;
            return style with { wrap = prev };
        }

        public static MpStyle Set(this MpStyle style, TextAnchor anchor)
        {
            var prev = Text.Anchor;
            Text.Anchor = anchor;
            return style with { anchor = prev };
        }

        public static MpStyle Set(this MpStyle style, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            return style with { color = prev };
        }

        public static MpStyle Set(this MpStyle style, GameFont font)
        {
            var prev = Text.Font;
            Text.Font = font;
            return style with { font = prev };
        }
    }
}
