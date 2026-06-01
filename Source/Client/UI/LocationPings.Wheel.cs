using System.Collections.Generic;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
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

        // Visible-slice cap. Anything beyond this gets paged through Back / More nav slices at the
        // upper-left and upper-right of the wheel. See BuildPage for the layout rules.
        public const int WheelMaxSlots = 6;

        // What a single wheel slot is showing - category Defs share the array with the Prev/Next
        // nav buttons so hit-testing and rendering can iterate uniformly.
        internal enum SlotKind { Empty, Category, PrevPage, NextPage }
        internal readonly struct WheelSlot
        {
            public readonly SlotKind kind;
            public readonly MultiplayerPingDef def;
            public WheelSlot(SlotKind k) { kind = k; def = null; }
            public WheelSlot(MultiplayerPingDef d) { kind = SlotKind.Category; def = d; }
            public static readonly WheelSlot Empty = new(SlotKind.Empty);
            public static readonly WheelSlot Prev  = new(SlotKind.PrevPage);
            public static readonly WheelSlot Next  = new(SlotKind.NextPage);
        }

        // Pre-rotated sector textures, keyed by slot count. Generated lazily for 2..6 slots so the
        // static init cost is bounded; the procedural ramp gives a 1° seam (the 35.5° half-angle).
        private static readonly Dictionary<int, Texture2D[]> SectorTexCache = new();
        private static readonly Dictionary<int, Texture2D[]> SectorArcCache = new();

        private static Texture2D[] GetSectors(int slots)
        {
            if (SectorTexCache.TryGetValue(slots, out var arr)) return arr;
            return SectorTexCache[slots] = MakeSectorSet(slots, outerRadius: 127f, innerRadius: 44f);
        }

        private static Texture2D[] GetSectorArcs(int slots)
        {
            if (SectorArcCache.TryGetValue(slots, out var arr)) return arr;
            return SectorArcCache[slots] = MakeSectorSet(slots, outerRadius: 127f, innerRadius: 119f);
        }

        private static Texture2D[] MakeSectorSet(int slots, float outerRadius, float innerRadius)
        {
            // Half-angle is one slice minus the 1° seam, so two adjacent slices leave a hairline of
            // backdrop visible between them no matter how many slots the wheel has.
            var halfAngle = 360f / slots / 2f - 0.5f;
            var texs = new Texture2D[slots];
            for (int i = 0; i < slots; i++)
                texs[i] = MultiplayerStatic.MakeSectorTex(256, outerRadius, innerRadius,
                    halfAngleDeg: halfAngle, centerAngleDeg: i * (360f / slots));
            return texs;
        }

        // Slot positions in clock-face terms: 0=top, 1=upper-right, 2=lower-right, 3=bottom,
        // 4=lower-left, 5=upper-left. Nav lives at slots 5 (Back, '<' chevron, upper-left) and 1
        // (More, '>' chevron, upper-right) so spatial position matches chevron direction matches
        // semantic meaning.
        private const int BackSlot = 5;
        private const int MoreSlot = 1;

        // Builds the slot layout for the given page. Returned list is always exactly slotCount
        // entries; empty slots are rendered as dim slices with no label. slotCount can be 1..6.
        //
        // Layout rules (page index = wheelPage):
        //   total <= WheelMaxSlots  -> single page, slotCount = total, no nav, cats fill sequentially
        //   total > WheelMaxSlots:
        //     page 0:     [cat, More, cat, cat, cat, cat]   (5 cats + More at upper-right)
        //     middle:     [cat, More, cat, cat, cat, Back]  (4 cats + both nav at the upper sides)
        //     last page:  [cat, cat,  cat, cat, cat, Back]  (up to 5 cats + Back at upper-left)
        internal static List<WheelSlot> BuildPage(List<MultiplayerPingDef> cats, int page, out int slotCount, out int totalPages)
        {
            var list = new List<WheelSlot>(WheelMaxSlots);
            var total = cats.Count;

            if (total == 0)
            {
                slotCount = 1;
                totalPages = 1;
                list.Add(WheelSlot.Empty);
                return list;
            }

            if (total <= WheelMaxSlots)
            {
                for (int i = 0; i < total; i++)
                    list.Add(new WheelSlot(cats[i]));
                slotCount = total;
                totalPages = 1;
                return list;
            }

            // Multi-page layout. CountPages / FirstCatIndexForPage produce the same per-page cat
            // counts as the prior bookend-nav layout (5 / 4 / ... / up-to-5) - only the slot
            // positions changed, not the capacities, so the page math is reused as-is.
            totalPages = CountPages(total);
            slotCount = WheelMaxSlots;
            var clamped = Mathf.Clamp(page, 0, totalPages - 1);

            var isFirst = clamped == 0;
            var startIdx = FirstCatIndexForPage(clamped);
            var remaining = total - startIdx;
            // First page is never last in multi-page mode (total > WheelMaxSlots guarantees a
            // spillover). After the first page, "last" is whichever page can hold the remaining
            // cats without needing a More slot - i.e. remaining fits in the 5 cat-slots of a no-More
            // page.
            var isLast = !isFirst && remaining <= WheelMaxSlots - 1;

            for (int s = 0; s < WheelMaxSlots; s++)
                list.Add(WheelSlot.Empty);

            if (!isFirst) list[BackSlot] = WheelSlot.Prev;
            if (!isLast)  list[MoreSlot] = WheelSlot.Next;

            // Fill non-nav slots in clock order. Skipping the nav positions interleaves the cat
            // sequence (e.g. on page 0 cats land at slots 0, 2, 3, 4, 5 - the More slot at 1 is
            // jumped over). Users locate cats by icon/colour, not position, so the discontinuity
            // is acceptable in exchange for stable nav positions.
            var ci = startIdx;
            for (int s = 0; s < WheelMaxSlots && ci < total; s++)
            {
                if (s == MoreSlot && !isLast)  continue;
                if (s == BackSlot && !isFirst) continue;
                list[s] = new WheelSlot(cats[ci++]);
            }

            return list;
        }

        // Counts pages by walking through the per-page capacities documented in BuildPage. Faster
        // than a closed-form expression and keeps the two paths trivially in sync.
        private static int CountPages(int total)
        {
            if (total <= WheelMaxSlots) return 1;
            var pages = 0;
            var consumed = 0;
            while (consumed < total)
            {
                var isFirst = pages == 0;
                var firstSlot = isFirst ? 0 : 1;
                var lastPageCapacity = WheelMaxSlots - firstSlot;
                var remaining = total - consumed;
                var willBeLast = remaining <= lastPageCapacity;
                var endSlot = willBeLast ? WheelMaxSlots : WheelMaxSlots - 1;
                consumed += endSlot - firstSlot;
                pages++;
            }
            return pages;
        }

        private static int FirstCatIndexForPage(int page)
        {
            // Page p > 0 starts after page 0's 5 cats + (p-1) middle pages × 4 cats each.
            if (page <= 0) return 0;
            return 5 + (page - 1) * 4;
        }

        // Per-frame view: pulls categories from DefDatabase (excluding the Default) and builds the
        // active page. Cached only within the call - the def list is tiny so the rebuild is cheap.
        private List<WheelSlot> BuildCurrentPage(out int slotCount, out int totalPages)
        {
            var cats = MultiplayerPingDef.Sorted(includeDefault: false);
            return BuildPage(cats, wheelPage, out slotCount, out totalPages);
        }

        // Hover -> def. For nav slots returns null (the click handler still consumes them).
        // For Empty / deadzone returns null too. The DrawWheelCore renderer treats null as "no
        // category will fire on release".
        private MultiplayerPingDef ComputeHoveredCategory()
        {
            var slots = BuildCurrentPage(out var slotCount, out _);
            var idx = HoveredSlotIndex(wheelScreenOrigin, UI.MousePositionOnUIInverted, slotCount);
            if (idx < 0) return null;
            var slot = slots[idx];
            return slot.kind == SlotKind.Category ? slot.def : null;
        }

        // Pure hit-test: returns the slot index under the cursor for a wheel of `slotCount`
        // slices, or -1 if the cursor sits in the centre deadzone. Doesn't touch DefDatabase so
        // DrawWheelCore can call it without rebuilding the page each time.
        private static int HoveredSlotIndex(Vector2 center, Vector2 mouse, int slotCount)
        {
            var dx = mouse.x - center.x;
            var dy = mouse.y - center.y;
            var distSq = dx * dx + dy * dy;
            if (distSq < WheelInnerDeadzone * WheelInnerDeadzone) return -1;
            // GUI coords: x right, y down. 12 o'clock = -y.
            var angle = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360;
            var sectorSize = 360f / slotCount;
            return Mathf.FloorToInt((angle + sectorSize / 2f) / sectorSize) % slotCount;
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

        // Cursor mode lets Default-ring click fall through; drawer mode consumes it. Page-nav slots
        // never fire a ping in either mode - they swap wheelPage and re-render.
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

            var slots = BuildCurrentPage(out var slotCount, out var totalPages);

            // GUI coords: x right, y down. 12 o'clock = -y.
            var angle = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360;
            var sectorSize = 360f / slotCount;
            var idx = Mathf.FloorToInt((angle + sectorSize / 2f) / sectorSize) % slotCount;
            var slot = slots[idx];

            switch (slot.kind)
            {
                case SlotKind.PrevPage:
                    wheelPage = Mathf.Max(0, wheelPage - 1);
                    ev.Use();
                    return true;
                case SlotKind.NextPage:
                    wheelPage = Mathf.Min(totalPages - 1, wheelPage + 1);
                    ev.Use();
                    return true;
                case SlotKind.Empty:
                    // Click on an empty slot in the ring still consumes the event in drawer mode so
                    // it doesn't leak through to the window drag, but does nothing else.
                    if (inDrawer) ev.Use();
                    return inDrawer;
                case SlotKind.Category:
                    var clicked = slot.def;
                    if (!inDrawer)
                    {
                        var asMarker = Multiplayer.settings.pingPlaceMode == PingPlaceMode.Marker;
                        FirePing(wheelTargetMapId, wheelTargetTile, wheelTargetMapLoc, clicked, asMarker);
                        CancelWheel();
                        ev.Use();
                        return true;
                    }
                    ArmPlacement(clicked);
                    ev.Use();
                    return true;
            }
            return false;
        }

        private void DrawWheelCore(Vector2 center, Vector2 mousePos, bool inDrawer)
        {
            var ev = Event.current;

            // Build the page slots once per invocation; the hover-compute, deadzone test, and the
            // repaint render below all share this result. Previously each of those rebuilt the page
            // independently, which meant 2-3 sorted-list allocations per Repaint while the wheel
            // was open.
            var slots = BuildCurrentPage(out var slotCount, out var totalPages);
            var hoveredSlotIdx = HoveredSlotIndex(center, mousePos, slotCount);
            var hoveredDef = hoveredSlotIdx >= 0 && slots[hoveredSlotIdx].kind == SlotKind.Category
                ? slots[hoveredSlotIdx].def
                : null;

            // Cursor mode keeps `hoveredCategory` updated from HandleWheelEligibleInput (key-release
            // there reads this field). Drawer mode has no such pump, so we update it here.
            if (inDrawer)
                hoveredCategory = hoveredDef;

            if (TryHandleWheelMouseDown(center, mousePos, inDrawer)) return;

            if (ev.type != EventType.Repaint) return;

            var drawerOpen = inDrawer;

            var sectorSize = 360f / slotCount;
            var inDeadzone = hoveredDef == null && !PointerIsOverNavSlot(slots, slotCount, center, mousePos);

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

            var sectors = GetSectors(slotCount);
            var arcs    = GetSectorArcs(slotCount);

            // Armed slice keeps its tint so the cursor cue persists when off-wheel.
            for (var i = 0; i < slotCount; i++)
            {
                var slot = slots[i];
                var hovered = i == hoveredSlotIdx && slot.kind != SlotKind.Empty;
                var armed = drawerOpen && slot.kind == SlotKind.Category && armedCategory == slot.def;
                var navHot = (slot.kind == SlotKind.PrevPage || slot.kind == SlotKind.NextPage) && hovered;

                Color fill, arc;
                if (armed)
                {
                    var t = slot.def.tint;
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
                else if (slot.kind == SlotKind.Empty)
                {
                    // Empty slots stay flat so they read as "nothing here" rather than a clickable
                    // category - dimmer than the unhovered cat fill.
                    fill = new Color(0.10f, 0.10f, 0.11f, 1f);
                    arc = new Color(0.06f, 0.06f, 0.07f, 1f);
                }
                else if (slot.kind == SlotKind.PrevPage || slot.kind == SlotKind.NextPage)
                {
                    // Nav slots render in neutral steel grey - colour belongs to categories so nav
                    // reads as wheel chrome rather than a 14th category. The chevron texture carries
                    // the direction.
                    fill = navHot
                        ? new Color(0.28f, 0.28f, 0.32f, 1f)
                        : new Color(0.13f, 0.13f, 0.15f, 1f);
                    arc = new Color(0.05f, 0.05f, 0.06f, 1f);
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
                    GUI.DrawTexture(sectorRect, sectors[i]);
                using (MpStyle.Set(arc))
                    GUI.DrawTexture(sectorRect, arcs[i]);
            }

            for (var i = 0; i < slotCount; i++)
            {
                var slot = slots[i];
                var hovered = i == hoveredSlotIdx && slot.kind != SlotKind.Empty;
                var sectorAngleRad = (i * sectorSize) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(sectorAngleRad), -Mathf.Cos(sectorAngleRad));
                var anchor = center + dir * CardRadius;
                var iconCenterX = anchor.x;
                var iconCenterY = anchor.y + IconOffsetY;

                switch (slot.kind)
                {
                    case SlotKind.Empty:
                        continue;
                    case SlotKind.PrevPage:
                        DrawNavSlot(anchor, hovered, isNext: false);
                        continue;
                    case SlotKind.NextPage:
                        DrawNavSlot(anchor, hovered, isNext: true);
                        continue;
                }

                var cat = slot.def;
                var tint = cat.tint;
                var iconTex = cat.IconTexture;
                if (iconTex != null)
                {
                    var iconSize = IconBaseSize * cat.iconScale;
                    var iconRect = new Rect(iconCenterX - iconSize / 2f, iconCenterY - iconSize / 2f,
                        iconSize, iconSize);

                    using (MpStyle.Set(new Color(0f, 0f, 0f, 0.55f)))
                        GUI.DrawTexture(new Rect(iconRect.x + 1f, iconRect.y + 1.5f,
                            iconRect.width, iconRect.height), iconTex);
                    using (MpStyle.Set(Color.white))
                        GUI.DrawTexture(iconRect, iconTex);
                }
                else if (!string.IsNullOrEmpty(cat.glyph))
                {
                    using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.MiddleCenter))
                        MpUI.LabelOutlined(new Rect(iconCenterX - 20f, iconCenterY - 14f, 40f, 28f),
                            cat.glyph,
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
                    ? "MpPingWheel_Disarm".Translate()
                    : "MpPingWheel_ClickToDisarm".Translate();
            else
                cancelLabel = inDeadzone
                    ? "MpPingWheel_Cancel".Translate()
                    : "X";
            var cancelLabelColor = hot ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
                MpUI.LabelOutlined(cancelRect, cancelLabel,
                    cancelLabelColor,
                    new Color(0f, 0f, 0f, 0.95f));

            if (!inDrawer)
                DrawChevronTab(center, mousePos);

            // Page indicator on multi-page wheels - small dots above the chevron tab so the user
            // sees how many pages exist and where they are.
            if (totalPages > 1)
                DrawPageIndicator(center, totalPages, wheelPage, inDrawer);
        }

        private static bool PointerIsOverNavSlot(List<WheelSlot> slots, int slotCount, Vector2 center, Vector2 mouse)
        {
            var dx = mouse.x - center.x;
            var dy = mouse.y - center.y;
            var distSq = dx * dx + dy * dy;
            if (distSq < WheelInnerR * WheelInnerR) return false;
            if (distSq > WheelOuterR * WheelOuterR) return false;
            var angle = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360;
            var sectorSize = 360f / slotCount;
            var idx = Mathf.FloorToInt((angle + sectorSize / 2f) / sectorSize) % slotCount;
            var k = slots[idx].kind;
            return k == SlotKind.PrevPage || k == SlotKind.NextPage;
        }

        // Bold chevron texture at the slot centre - no label, no offset. The page-dot indicator
        // above the wheel (DrawPageIndicator) communicates position; the chevron only has to say
        // "this is page nav, in that direction".
        private static void DrawNavSlot(Vector2 anchor, bool hovered, bool isNext)
        {
            const float ChevSize = 26f;
            var chevRect = new Rect(anchor.x - ChevSize / 2f, anchor.y - ChevSize / 2f,
                ChevSize, ChevSize);
            var tex = isNext ? MultiplayerStatic.PingChevronRight : MultiplayerStatic.PingChevronLeft;

            using (MpStyle.Set(new Color(0f, 0f, 0f, 0.55f)))
                GUI.DrawTexture(new Rect(chevRect.x + 1f, chevRect.y + 1f,
                    chevRect.width, chevRect.height), tex);
            using (MpStyle.Set(hovered ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f)))
                GUI.DrawTexture(chevRect, tex);
        }

        // Tiny dots above the wheel that highlight the active page; rendered below the chevron tab
        // in cursor mode and just below the title in drawer mode (same logical position relative to
        // the wheel centre).
        private static void DrawPageIndicator(Vector2 center, int totalPages, int activePage, bool inDrawer)
        {
            const float Dot = 5f;
            const float DotGap = 5f;
            var width = totalPages * Dot + (totalPages - 1) * DotGap;
            var x = center.x - width / 2f;
            // Sits just above the backdrop ring (or below the chevron tab if there is one).
            var y = center.y - WheelBackdropR - (inDrawer ? 10f : ChevronTabGapY + ChevronTabHeight + 8f);
            for (int i = 0; i < totalPages; i++)
            {
                var r = new Rect(x + i * (Dot + DotGap), y, Dot, Dot);
                Widgets.DrawBoxSolid(r, i == activePage
                    ? new Color(1f, 1f, 1f, 0.95f)
                    : new Color(1f, 1f, 1f, 0.35f));
            }
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
            if (armedCategory == null) return;
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

            var cat = armedCategory;

            using (MpStyle.Set(new Color(0f, 0f, 0f, 0.75f)))
                GUI.DrawTexture(new Rect(ghostRect.x - 3f, ghostRect.y - 3f, ghostRect.width + 6f, ghostRect.height + 6f),
                    MultiplayerStatic.PingCircle);
            var tint = cat.tint;
            using (MpStyle.Set(new Color(tint.r, tint.g, tint.b, 0.85f)))
                GUI.DrawTexture(ghostRect, MultiplayerStatic.PingCircle);
            using (MpStyle.Set(new Color(1f, 1f, 1f, 0.9f)))
                GUI.DrawTexture(ghostRect.ExpandedBy(1f), MultiplayerStatic.PingRing);

            var icon = cat.IconTexture;
            if (icon != null)
            {
                var iconSize = GhostSize * 0.66f * cat.iconScale;
                var iconRect = new Rect(ghostRect.center.x - iconSize / 2f,
                    ghostRect.center.y - iconSize / 2f, iconSize, iconSize);
                using (MpStyle.Set(new Color(0f, 0f, 0f, 0.6f)))
                    GUI.DrawTexture(new Rect(iconRect.x + 1f, iconRect.y + 1.5f,
                        iconRect.width, iconRect.height), icon);
                using (MpStyle.Set(Color.white))
                    GUI.DrawTexture(iconRect, icon);
            }
            else if (!string.IsNullOrEmpty(cat.glyph))
            {
                using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
                    MpUI.LabelOutlined(ghostRect, cat.glyph, Color.white, new Color(0f, 0f, 0f, 0.95f));
            }

            var modeLabel = ArmedAsMarker
                ? "MpPingArmed_Marker".Translate()
                : "MpPingArmed_Ping".Translate();
            var modeRect = new Rect(ghostRect.x - 12f, ghostRect.yMax + 2f, GhostSize + 24f, 14f);
            Widgets.DrawBoxSolid(modeRect, new Color(0f, 0f, 0f, 0.7f));
            using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleCenter).Set(WordWrap.NoWrap))
                MpUI.LabelOutlined(modeRect, modeLabel, Color.white, new Color(0f, 0f, 0f, 0.95f));
        }

    }
}
