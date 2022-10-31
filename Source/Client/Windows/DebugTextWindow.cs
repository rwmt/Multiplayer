using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class DebugTextWindow : Window
{
    private Vector2 _initialSize;
    public override Vector2 InitialSize => _initialSize;

    private Vector2 scroll;
    private string text;
    private List<string> lines;

    private float fullHeight;

    public DebugTextWindow(string text, float width=800, float height=450)
    {
        this.text = text;
        this._initialSize = new Vector2(width, height);
        absorbInputAroundWindow = false;
        doCloseX = true;
        draggable = true;

        lines = text.Split('\n').ToList();
    }

    public override void DoWindowContents(Rect inRect)
    {
        const float offsetY = -5f;

        if (Event.current.type == EventType.Layout)
        {
            fullHeight = 0;
            foreach (var str in lines)
                fullHeight += Text.CalcHeight(str, inRect.width) + offsetY;
        }

        Text.Font = GameFont.Tiny;

        if (Widgets.ButtonText(new Rect(0, 0, 55f, 20f), "Copy all"))
            GUIUtility.systemCopyBuffer = text;

        Text.Font = GameFont.Small;

        var viewRect = new Rect(0f, 0f, inRect.width - 16f, Mathf.Max(fullHeight + 10f, inRect.height));
        inRect.y += 30f;
        Widgets.BeginScrollView(inRect, ref scroll, viewRect, true);

        foreach (var str in lines)
        {
            float h = Text.CalcHeight(str, viewRect.width);
            Widgets.TextArea(new Rect(viewRect.x, viewRect.y, viewRect.width, h), str, true);
            viewRect.y += h + offsetY;
        }

        Widgets.EndScrollView();
    }
}
