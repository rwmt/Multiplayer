using System.Collections.Generic;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

// Local-only alpha override per markerId, persisted to MpSettings. Styled after vanilla
// Dialog_Slider: centered Medium title, min/max labels under the slider, Cancel/Apply pair.
public class MarkerAlphaWindow : Window
{
    private const float MinAlpha = 0.05f;
    private const float SwatchSize = 56f;

    private readonly List<int> markerIds;
    private readonly float initialAlpha;
    private float alpha;

    public override Vector2 InitialSize => new(360f, 220f);
    public override float Margin => 10f;

    public MarkerAlphaWindow(List<int> markerIds)
    {
        this.markerIds = markerIds;

        // Mixed values: seed from first marker; user can re-drag.
        alpha = 1f;
        var s = Multiplayer.settings;
        if (s?.localMarkerAlpha != null && markerIds != null && markerIds.Count > 0
            && s.localMarkerAlpha.TryGetValue(markerIds[0], out var a))
            alpha = Mathf.Clamp(a, MinAlpha, 1f);
        initialAlpha = alpha;

        forcePause = true;
        closeOnAccept = true;
        closeOnCancel = true;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = true;
        doCloseX = true;
        focusWhenOpened = true;
        soundAppear = SoundDefOf.InfoCard_Open;
        soundClose = SoundDefOf.InfoCard_Close;
    }

    public override void DoWindowContents(Rect inRect)
    {
        const float TitleH = 28f;
        const float SwatchGap = 10f;
        const float SliderH = 28f;
        const float MinMaxH = 14f;
        const float ButtonH = 30f;
        const float ButtonGap = 10f;

        var titleRect = new Rect(inRect.x, inRect.y, inRect.width, TitleH);
        using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.UpperCenter))
            Widgets.Label(titleRect, MpTranslate.Fallback("MpMarkerAlpha_Title", "Marker transparency"));

        var swatchRect = new Rect(
            inRect.center.x - SwatchSize / 2f,
            titleRect.yMax + 4f,
            SwatchSize, SwatchSize);
        DrawAlphaSwatch(swatchRect, alpha);

        var sliderY = swatchRect.yMax + SwatchGap;
        var sliderRect = new Rect(inRect.x + 6f, sliderY, inRect.width - 12f, SliderH);
        var pctLabel = MpTranslate.Fallback("MpMarkerAlpha_Percent",
            $"{Mathf.RoundToInt(alpha * 100f)}%", Mathf.RoundToInt(alpha * 100f));
        var newAlpha = Widgets.HorizontalSlider(sliderRect, alpha, MinAlpha, 1f,
            middleAlignment: true, label: pctLabel, roundTo: 0.01f);
        if (!Mathf.Approximately(newAlpha, alpha))
        {
            alpha = newAlpha;
            ApplyToSelection();
        }

        var minMaxRect = new Rect(inRect.x + 6f, sliderRect.yMax + 2f, inRect.width - 12f, MinMaxH);
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.UpperLeft).Set(ColoredText.SubtleGrayColor))
            Widgets.Label(minMaxRect,
                MpTranslate.Fallback("MpMarkerAlpha_MinHint", $"{Mathf.RoundToInt(MinAlpha * 100f)}% (min)",
                    Mathf.RoundToInt(MinAlpha * 100f)));
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.UpperRight).Set(ColoredText.SubtleGrayColor))
            Widgets.Label(minMaxRect, MpTranslate.Fallback("MpMarkerAlpha_MaxHint", "100% (full)"));
        TooltipHandler.TipRegion(sliderRect, MpTranslate.Fallback("MpMarkerAlpha_MinTip",
            "Markers stay clickable at the minimum so you can always bring them back."));

        var btnY = inRect.yMax - ButtonH;
        var btnW = (inRect.width - ButtonGap) / 2f;
        var cancelRect = new Rect(inRect.x, btnY, btnW, ButtonH);
        var applyRect = new Rect(inRect.x + btnW + ButtonGap, btnY, btnW, ButtonH);

        if (Widgets.ButtonText(cancelRect, MpTranslate.Fallback("MpMarkerAlpha_Cancel", "Cancel")))
        {
            alpha = initialAlpha;
            ApplyToSelection();
            Close();
        }
        if (Widgets.ButtonText(applyRect, MpTranslate.Fallback("MpMarkerAlpha_Apply", "Apply")))
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }
    }

    private static void DrawAlphaSwatch(Rect rect, float alpha)
    {
        // Checkerboard backdrop so the alpha is visible against any UI shade.
        var checkColorA = new Color(0.30f, 0.30f, 0.30f);
        var checkColorB = new Color(0.20f, 0.20f, 0.20f);
        const int Checks = 4;
        var cw = rect.width / Checks;
        var ch = rect.height / Checks;
        for (var iy = 0; iy < Checks; iy++)
        for (var ix = 0; ix < Checks; ix++)
        {
            var c = ((ix + iy) & 1) == 0 ? checkColorA : checkColorB;
            Widgets.DrawBoxSolid(new Rect(rect.x + ix * cw, rect.y + iy * ch, cw, ch), c);
        }

        using (MpStyle.Set(new Color(0.65f, 0.85f, 1f, alpha)))
            GUI.DrawTexture(rect.ContractedBy(6f), MultiplayerStatic.PingCircle);

        Widgets.DrawBox(rect);
    }

    private void ApplyToSelection()
    {
        var s = Multiplayer.settings;
        if (s == null || markerIds == null) return;
        s.localMarkerAlpha ??= new Dictionary<int, float>();
        foreach (var id in markerIds)
        {
            if (alpha >= 0.999f) s.localMarkerAlpha.Remove(id);
            else s.localMarkerAlpha[id] = alpha;
        }
        if (Multiplayer.game?.gameComp != null) Multiplayer.game.gameComp.markersVersion++;
    }

    public override void OnAcceptKeyPressed()
    {
        SoundDefOf.Click.PlayOneShotOnCamera();
        Close();
    }

    public override void OnCancelKeyPressed()
    {
        alpha = initialAlpha;
        ApplyToSelection();
        Close();
    }

    public override void PostClose()
    {
        base.PostClose();
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
    }
}
