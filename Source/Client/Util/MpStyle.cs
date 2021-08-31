using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Util
{
    public enum WordWrap
    {
        NoWrap, DoWrap
    }

    public static class MpStyle
    {
        struct TextOptReset : IDisposable
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
        }

        public static IDisposable Set(WordWrap wrap)
        {
            var prev = Text.WordWrap;
            Text.WordWrap = wrap == WordWrap.DoWrap;
            return new TextOptReset() { wrap = prev };
        }

        public static IDisposable Set(TextAnchor anchor)
        {
            var prev = Text.Anchor;
            Text.Anchor = anchor;
            return new TextOptReset() { anchor = prev };
        }

        public static IDisposable Set(Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            return new TextOptReset() { color = prev };
        }

        public static IDisposable Set(GameFont font)
        {
            var prev = Text.Font;
            Text.Font = font;
            return new TextOptReset() { font = prev };
        }
    }
}
