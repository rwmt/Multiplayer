using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public partial class LocationPings
{
    private const float WheelOuterR         = 175f;
    private const float WheelInnerR         = 60f;
    private const float WheelInnerDeadzone  = WheelInnerR;
    private const float CardRadius          = 117f;
    private const float IconOffsetY         = -10f;
    private const float NameOffsetY         = 21f;
    private const float IconBaseSize        = 32f;
    private const float NameCardWidth       = 80f;
    private const float NameCardHeight      = 22f;
    private const float NameCardStripeHeight = 3f;

    public  const float WheelBackdropR   = WheelOuterR + 26f;
    private const float ChevronTabWidth  = 56f;
    private const float ChevronTabHeight = 20f;
    private const float ChevronTabGapY   = 4f;

    // Clockwise from the top.
    private static readonly PingCategory[] WheelOptions =
    {
        PingCategory.Attack,
        PingCategory.Help,
        PingCategory.Loot,
        PingCategory.Rally,
        PingCategory.Defend,
    };

    // Pre-rotated; 35.5° half-angle leaves a 1° seam so the backdrop shows through.
    internal static readonly Texture2D[] PingSectors    = MakePingSectors(outerRadius: 127f, innerRadius: 44f);
    internal static readonly Texture2D[] PingSectorArcs = MakePingSectors(outerRadius: 127f, innerRadius: 119f);

    private static Texture2D[] MakePingSectors(float outerRadius, float innerRadius)
    {
        int slots = WheelOptions.Length;
        var texs = new Texture2D[slots];
        for (int i = 0; i < slots; i++)
            texs[i] = MultiplayerStatic.MakeSectorTex(256, outerRadius, innerRadius,
                halfAngleDeg: 35.5f, centerAngleDeg: i * (360f / slots));
        return texs;
    }

    private PingCategory ComputeHoveredCategory() =>
        ComputeHoveredCategoryAt(wheelScreenOrigin, UI.MousePositionOnUIInverted);

    private static PingCategory ComputeHoveredCategoryAt(Vector2 center, Vector2 mouse)
    {
        var dx = mouse.x - center.x;
        var dy = mouse.y - center.y;
        var distSq = dx * dx + dy * dy;
        if (distSq < WheelInnerDeadzone * WheelInnerDeadzone)
            return PingCategory.Default;

        // GUI coords: x right, y down. 12 o'clock = -y.
        var angle = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360;

        var sectorSize = 360f / WheelOptions.Length;
        var sectorIdx = Mathf.FloorToInt((angle + sectorSize / 2f) / sectorSize) % WheelOptions.Length;
        return WheelOptions[sectorIdx];
    }

    private static Rect ComputeChevronTabRect(Vector2 center)
    {
        var x = center.x - ChevronTabWidth / 2f;
        var y = center.y - WheelBackdropR - ChevronTabGapY - ChevronTabHeight;
        return new Rect(x, y, ChevronTabWidth, ChevronTabHeight);
    }

    public void DrawWheelOverlay()
    {
        if (MenuWindowOpen) return;
        if (!wheelActive) return;

        DrawWheelCore(wheelScreenOrigin, mousePos: UI.MousePositionOnUIInverted, inDrawer: false);
    }

    public void DrawWheelInDrawer(Vector2 center, Vector2 mousePos)
    {
        DrawWheelCore(center, mousePos, inDrawer: true);
    }

    // Cursor mode lets Default-ring click fall through; drawer mode consumes it.
    private bool TryHandleWheelMouseDown(Vector2 center, Vector2 mousePos, bool inDrawer)
    {
        var ev = Event.current;
        if (ev.type != EventType.MouseDown || ev.button != 0) return false;

        // Chevron is cursor-mode only; drawer mode uses the deadzone to disarm.
        if (!inDrawer && ComputeChevronTabRect(center).Contains(mousePos))
        {
            ToggleDrawer();
            ev.Use();
            return true;
        }

        var dx = mousePos.x - center.x;
        var dy = mousePos.y - center.y;
        var distSq = dx * dx + dy * dy;
        var outerR2 = WheelOuterR * WheelOuterR;
        var innerR2 = WheelInnerR * WheelInnerR;
        if (distSq > outerR2) return false;

        if (distSq < innerR2)
        {
            if (!inDrawer)
                CancelWheel();
            else if (armedCategory != null)
                DisarmPlacement();
            ev.Use();
            return true;
        }

        var clicked = ComputeHoveredCategoryAt(center, mousePos);

        if (!inDrawer)
        {
            // Default-category click in the ring falls through so the wheel stays up.
            if (clicked == PingCategory.Default) return false;

            var asMarker = Multiplayer.settings.pingPlaceMode == PingPlaceMode.Marker;
            FirePing(wheelTargetMapId, wheelTargetTile, wheelTargetMapLoc, clicked, asMarker);
            CancelWheel();
            ev.Use();
            return true;
        }

        // Drawer mode: any in-ring click consumes the event; real slices also arm.
        if (clicked != PingCategory.Default)
            ArmPlacement(clicked);
        ev.Use();
        return true;
    }

    private void DrawWheelCore(Vector2 center, Vector2 mousePos, bool inDrawer)
    {
        var ev = Event.current;

        if (inDrawer)
        {
            hoveredCategory = ComputeHoveredCategoryAt(center, mousePos);
        }

        if (TryHandleWheelMouseDown(center, mousePos, inDrawer)) return;

        if (ev.type != EventType.Repaint) return;

        var drawerOpen = inDrawer;

        var sectorSize = 360f / WheelOptions.Length;
        var inDeadzone = hoveredCategory == PingCategory.Default;

        // 256-px sector tex with 127-px native outer radius scales up to WheelOuterR on screen.
        const float TexNativeOuterR = 127f;
        var sectorRectSide = 256f * (WheelOuterR / TexNativeOuterR);
        var sectorRect = new Rect(center.x - sectorRectSide / 2f, center.y - sectorRectSide / 2f,
            sectorRectSide, sectorRectSide);

        var backdropDiam = WheelBackdropR * 2f;
        var backdropRect = new Rect(center.x - backdropDiam / 2f, center.y - backdropDiam / 2f,
            backdropDiam, backdropDiam);
        using (MpStyle.Set(new Color(0f, 0f, 0f, 0.55f)))
            GUI.DrawTexture(backdropRect, MultiplayerStatic.PingCircle);
        using (MpStyle.Set(new Color(1f, 1f, 1f, 0.18f)))
            GUI.DrawTexture(backdropRect, MultiplayerStatic.PingRing);

        // Armed slice keeps its tint so the cursor cue persists when off-wheel.
        for (var i = 0; i < WheelOptions.Length; i++)
        {
            var hovered = WheelOptions[i] == hoveredCategory;
            var armed = drawerOpen && armedCategory == WheelOptions[i];

            Color fill, arc;
            if (armed)
            {
                var t = WheelOptions[i].Tint();
                var amt = hovered ? 0.40f : 0.28f;
                fill = new Color(
                    Mathf.Lerp(0.24f, t.r, amt),
                    Mathf.Lerp(0.24f, t.g, amt),
                    Mathf.Lerp(0.24f, t.b, amt),
                    1f);
                arc = new Color(
                    Mathf.Lerp(0.16f, t.r, 0.35f),
                    Mathf.Lerp(0.16f, t.g, 0.35f),
                    Mathf.Lerp(0.16f, t.b, 0.35f),
                    1f);
            }
            else
            {
                fill = hovered
                    ? new Color(0.24f, 0.24f, 0.27f, 1f)
                    : new Color(0.14f, 0.14f, 0.16f, 1f);
                arc = hovered
                    ? new Color(0.16f, 0.16f, 0.19f, 1f)
                    : new Color(0.08f, 0.08f, 0.10f, 1f);
            }

            using (MpStyle.Set(fill))
                GUI.DrawTexture(sectorRect, PingSectors[i]);
            using (MpStyle.Set(arc))
                GUI.DrawTexture(sectorRect, PingSectorArcs[i]);
        }

        for (var i = 0; i < WheelOptions.Length; i++)
        {
            var cat = WheelOptions[i];
            var hovered = cat == hoveredCategory;
            var tint = cat.Tint();
            var sectorAngleRad = (i * sectorSize) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(sectorAngleRad), -Mathf.Cos(sectorAngleRad));
            var anchor = center + dir * CardRadius;

            var iconCenterX = anchor.x;
            var iconCenterY = anchor.y + IconOffsetY;
            var iconTex = cat.Icon();
            if (iconTex != null)
            {
                var iconSize = IconBaseSize * cat.IconScale();
                var iconRect = new Rect(iconCenterX - iconSize / 2f, iconCenterY - iconSize / 2f,
                    iconSize, iconSize);

                using (MpStyle.Set(new Color(0f, 0f, 0f, 0.55f)))
                    GUI.DrawTexture(new Rect(iconRect.x + 1f, iconRect.y + 1.5f,
                        iconRect.width, iconRect.height), iconTex);
                using (MpStyle.Set(Color.white))
                    GUI.DrawTexture(iconRect, iconTex);
            }
            else
            {
                using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.MiddleCenter))
                    MpUI.LabelOutlined(new Rect(iconCenterX - 20f, iconCenterY - 14f, 40f, 28f),
                        cat.Glyph(),
                        Color.white,
                        new Color(0f, 0f, 0f, 0.95f));
            }

            // Dark ribbon name card with a category-color top stripe - the only color element per slice.
            var nameRect = new Rect(anchor.x - NameCardWidth / 2f,
                anchor.y + NameOffsetY - NameCardHeight / 2f,
                NameCardWidth, NameCardHeight);

            Widgets.DrawBoxSolid(new Rect(nameRect.x + 1f, nameRect.y + 2f,
                nameRect.width, nameRect.height), new Color(0f, 0f, 0f, 0.45f));
            Widgets.DrawBoxSolid(nameRect, hovered
                ? new Color(0.22f, 0.22f, 0.25f, 0.97f)
                : new Color(0.10f, 0.10f, 0.12f, 0.92f));
            Widgets.DrawBoxSolid(new Rect(nameRect.x, nameRect.y, nameRect.width, NameCardStripeHeight),
                new Color(tint.r, tint.g, tint.b, hovered ? 1f : 0.78f));
            using (MpStyle.Set(hovered ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.22f)))
                Widgets.DrawBox(nameRect);
            using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleCenter).Set(WordWrap.NoWrap))
                MpUI.LabelOutlined(nameRect, cat.DisplayName(),
                    hovered ? Color.white : new Color(1f, 1f, 1f, 0.92f),
                    new Color(0f, 0f, 0f, 0.95f));
        }

        // Center cancel disc - doubles as Disarm in drawer mode.
        var cancelDiam = (WheelInnerR - 4f) * 2f;
        var cancelRect = new Rect(center.x - cancelDiam / 2f, center.y - cancelDiam / 2f,
            cancelDiam, cancelDiam);
        var hot = inDeadzone || (drawerOpen && armedCategory != null);
        var cancelFill = hot
            ? new Color(0.85f, 0.30f, 0.30f, 1f)
            : new Color(0.18f, 0.18f, 0.20f, 0.92f);

        using (MpStyle.Set(new Color(0f, 0f, 0f, 0.55f)))
            GUI.DrawTexture(new Rect(cancelRect.x + 1f, cancelRect.y + 2f,
                cancelRect.width, cancelRect.height), MultiplayerStatic.PingCircle);
        using (MpStyle.Set(cancelFill))
            GUI.DrawTexture(cancelRect, MultiplayerStatic.PingCircle);
        using (MpStyle.Set(new Color(1f, 1f, 1f, hot ? 0.85f : 0.45f)))
            GUI.DrawTexture(cancelRect.ExpandedBy(2f), MultiplayerStatic.PingRing);

        string cancelLabel;
        if (drawerOpen && armedCategory != null)
            cancelLabel = inDeadzone
                ? MpTranslate.Fallback("MpPingWheel_Disarm",        "Disarm")
                : MpTranslate.Fallback("MpPingWheel_ClickToDisarm", "X");
        else
            cancelLabel = inDeadzone
                ? MpTranslate.Fallback("MpPingWheel_Cancel",        "Cancel")
                : "X";
        var cancelLabelColor = hot ? Color.white : new Color(1f, 1f, 1f, 0.55f);
        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
            MpUI.LabelOutlined(cancelRect, cancelLabel,
                cancelLabelColor,
                new Color(0f, 0f, 0f, 0.95f));

        if (!inDrawer)
            DrawChevronTab(center, mousePos);
    }

    private void DrawChevronTab(Vector2 center, Vector2 mousePos)
    {
        var tabRect = ComputeChevronTabRect(center);
        var hot = tabRect.Contains(mousePos);

        var atlas = hot && Input.GetMouseButton(0) ? Widgets.ButtonBGAtlasClick
            : (hot ? Widgets.ButtonBGAtlasMouseover : Widgets.ButtonBGAtlas);
        Widgets.DrawAtlas(tabRect, atlas);

        var chevSize = ChevronTabHeight - 6f;
        var chevRect = new Rect(tabRect.center.x - chevSize / 2f, tabRect.center.y - chevSize / 2f,
            chevSize, chevSize);

        using (MpStyle.Set(new Color(0f, 0f, 0f, 0.7f)))
            GUI.DrawTexture(new Rect(chevRect.x + 1f, chevRect.y + 1f, chevRect.width, chevRect.height),
                MultiplayerStatic.PingChevronUp);
        using (MpStyle.Set(hot ? Color.white : new Color(0.95f, 0.95f, 0.95f, 1f)))
            GUI.DrawTexture(chevRect, MultiplayerStatic.PingChevronUp);
    }

    public void DrawArmedCursor()
    {
        if (armedCategory is not { } cat) return;
        if (Event.current.type != EventType.Repaint) return;

        if (Find.DesignatorManager?.SelectedDesignator != null) return;
        if ((Find.Targeter?.IsTargeting ?? false)
            || (Find.WorldTargeter?.IsTargeting ?? false)) return;

        var mouse = UI.MousePositionOnUIInverted;
        // Skip while over the wheel (armed slice shows the cue) or over any window.
        var dx = mouse.x - wheelScreenOrigin.x;
        var dy = mouse.y - wheelScreenOrigin.y;
        var wheelDispSq = dx * dx + dy * dy;
        var backdropR2 = WheelBackdropR * WheelBackdropR;
        if (wheelDispSq <= backdropR2) return;

        if (Find.WindowStack?.GetWindowAt(mouse) != null) return;

        const float GhostSize = 32f;
        const float GhostOffsetX = 18f;
        const float GhostOffsetY = 18f;

        var ghostRect = new Rect(mouse.x + GhostOffsetX, mouse.y + GhostOffsetY,
            GhostSize, GhostSize);

        using (MpStyle.Set(new Color(0f, 0f, 0f, 0.75f)))
            GUI.DrawTexture(new Rect(ghostRect.x - 3f, ghostRect.y - 3f, ghostRect.width + 6f, ghostRect.height + 6f),
                MultiplayerStatic.PingCircle);
        var tint = cat.Tint();
        using (MpStyle.Set(new Color(tint.r, tint.g, tint.b, 0.85f)))
            GUI.DrawTexture(ghostRect, MultiplayerStatic.PingCircle);
        using (MpStyle.Set(new Color(1f, 1f, 1f, 0.9f)))
            GUI.DrawTexture(ghostRect.ExpandedBy(1f), MultiplayerStatic.PingRing);

        var icon = cat.Icon();
        if (icon != null)
        {
            var iconSize = GhostSize * 0.66f * cat.IconScale();
            var iconRect = new Rect(ghostRect.center.x - iconSize / 2f,
                ghostRect.center.y - iconSize / 2f, iconSize, iconSize);
            using (MpStyle.Set(new Color(0f, 0f, 0f, 0.6f)))
                GUI.DrawTexture(new Rect(iconRect.x + 1f, iconRect.y + 1.5f,
                    iconRect.width, iconRect.height), icon);
            using (MpStyle.Set(Color.white))
                GUI.DrawTexture(iconRect, icon);
        }
        else
        {
            using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
                MpUI.LabelOutlined(ghostRect, cat.Glyph(), Color.white, new Color(0f, 0f, 0f, 0.95f));
        }

        var modeLabel = ArmedAsMarker
            ? MpTranslate.Fallback("MpPingArmed_Marker", "Marker")
            : MpTranslate.Fallback("MpPingArmed_Ping",   "Ping");
        var modeRect = new Rect(ghostRect.x - 12f, ghostRect.yMax + 2f, GhostSize + 24f, 14f);
        Widgets.DrawBoxSolid(modeRect, new Color(0f, 0f, 0f, 0.7f));
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleCenter).Set(WordWrap.NoWrap))
            MpUI.LabelOutlined(modeRect, modeLabel, Color.white, new Color(0f, 0f, 0f, 0.95f));
    }

}
