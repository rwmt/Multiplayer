using UnityEngine;
using Verse;

namespace Multiplayer.Client.Util;

public static class L
{
    public static void Label(string text)
    {
        GUILayout.Label(text, Text.CurFontStyle);
    }

    public static bool Button(string text, float width, float height = 35f)
    {
        return Widgets.ButtonText(
            GUILayoutUtility.GetRect(
                new GUIContent(text),
                Text.CurFontStyle,
                GUILayout.Height(height),
                GUILayout.Width(width)),
            text
        );
    }

    public static void BeginHorizCenter()
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
    }

    public static void EndHorizCenter()
    {
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }
}
