using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class TextAreaWindow : Window
{
    private string text;
    private Vector2 scroll;

    public TextAreaWindow(string text)
    {
        this.text = text;

        absorbInputAroundWindow = true;
        doCloseX = true;
    }

    public override void DoWindowContents(Rect inRect)
    {
        Widgets.TextAreaScrollable(inRect, text, ref scroll);
    }
}
