using System;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class IngameModal
{
    public const int ModalWindowId = 26461263;

    internal static void DrawModalWindow(string text, Func<bool> shouldShow, Action onCancel, string cancelButtonLabel)
    {
        string textWithEllipsis = $"{text}{MpUI.FixedEllipsis()}";
        float textWidth = Text.CalcSize(textWithEllipsis).x;
        float windowWidth = Math.Max(240f, textWidth + 40f);
        float windowHeight = onCancel != null ? 100f : 75f;
        var rect = new Rect(0, 0, windowWidth, windowHeight).CenterOn(new Rect(0, 0, UI.screenWidth, UI.screenHeight));

        Find.WindowStack.ImmediateWindow(ModalWindowId, rect, WindowLayer.Super, () =>
        {
            if (!shouldShow()) return;

            var textRect = rect.AtZero();
            if (onCancel != null)
            {
                textRect.yMin += 5f;
                textRect.height -= 50f;
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(textRect, text);
            Text.Anchor = TextAnchor.UpperLeft;

            var cancelBtn = new Rect(0, textRect.yMax, 100f, 35f).CenteredOnXIn(textRect);

            if (onCancel != null && Widgets.ButtonText(cancelBtn, cancelButtonLabel))
                onCancel();
        }, absorbInputAroundWindow: true);
    }
}
