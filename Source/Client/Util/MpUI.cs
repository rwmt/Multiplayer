using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client.Util
{
    [HotSwappable]
    public static class MpUI
    {
        public static Vector2 Resolution => new Vector2(UI.screenWidth, UI.screenHeight);

        public static string FixedEllipsis()
        {
            int num = Mathf.FloorToInt(Time.realtimeSinceStartup) % 3;
            return num switch
            {
                0 => ".  ",
                1 => ".. ",
                _ => "..."
            };
        }

        public static void DrawRotatedLine(Vector2 center, float length, float width, float angle, Color color)
        {
            var size = new Vector2(length, width);
            var start = center - size / 2f;
            Rect screenRect = new Rect(start.x, start.y, length, width);
            Matrix4x4 m = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.TRS(-center, Quaternion.identity, Vector3.one);

            GL.PushMatrix();
            GL.MultMatrix(m);
            GUI.DrawTexture(screenRect, Widgets.LineTexAA, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
            GL.PopMatrix();
        }

        public static void Label(Rect rect, string label, GameFont? font = null, TextAnchor? anchor = null, Color? color = null)
        {
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            if (font != null)
                Text.Font = font.Value;

            if (anchor != null)
                Text.Anchor = anchor.Value;

            if (color != null)
                GUI.color = color.Value;

            Widgets.Label(rect, label);

            GUI.color = prevColor;
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
        }

        public static void CheckboxLabeled(Rect rect, string label, ref bool checkOn, bool disabled = false, Texture2D texChecked = null, Texture2D texUnchecked = null, bool placeTextNearCheckbox = false)
        {
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            if (placeTextNearCheckbox)
            {
                float textWidth = Text.CalcSize(label).x;
                rect.x = rect.xMax - textWidth - 24f - 5f;
                rect.width = textWidth + 24f + 5f;
            }

            Widgets.Label(rect, label);

            if (!disabled && Widgets.ButtonInvisible(rect, false))
            {
                checkOn = !checkOn;
                if (checkOn)
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera(null);
                else
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera(null);
            }

            Widgets.CheckboxDraw(rect.x + rect.width - 24f, rect.y, checkOn, disabled, 24f, null, null);
            Text.Anchor = anchor;
        }

        public static string TextEntryLabeled(Rect rect, string label, string text, float labelWidth)
        {
            Rect labelRect = rect.Rounded();
            labelRect.width = labelWidth;

            using (MpStyle.Set(TextAnchor.MiddleRight))
                Widgets.Label(labelRect, label);

            rect.xMin += labelWidth;
            return Widgets.TextField(rect, text);
        }

        public static bool TextFieldNumericLabeled<T>(Rect rect, string label, ref T val, ref string buffer, float labelWidth, float min = 0, float max = float.MaxValue, bool clickable = false, string tooltip = null) where T : struct
        {
            Rect labelRect = rect;
            labelRect.width = labelWidth;

            using (MpStyle.Set(TextAnchor.MiddleRight))
            using (MpStyle.Set(WordWrap.NoWrap))
                Widgets.Label(labelRect, label);

            if (clickable)
                Widgets.DrawHighlightIfMouseover(labelRect.MinX(labelRect.xMax - Text.CalcSize(label).x - 10));

            if (tooltip != null)
                TooltipHandler.TipRegion(labelRect, tooltip);

            rect.xMin += labelWidth;
            Widgets.TextFieldNumeric(rect, ref val, ref buffer, min, max);

            return clickable && Widgets.ButtonInvisible(labelRect);
        }

        public static void ClearWindowStack()
        {
            Find.WindowStack.windows.Clear();
        }

        public static void TryUnfocusCurrentNamedControl(Window window)
        {
            // Unfocus the input field after a click or ESC press
            if ((Event.current.type == EventType.MouseDown || KeyBindingDefOf.Cancel.KeyDownEvent) && !GUI.GetNameOfFocusedControl().NullOrEmpty())
            {
                Event.current.Use();
                UI.UnfocusCurrentControl();
                Find.WindowStack.Notify_ManuallySetFocus(window);
            }
        }

        public static void LabelTruncatedWithTip(Rect rect, string str, Dictionary<string, string> cache)
        {
            var trunc = str.Truncate(rect.width, cache);
            Widgets.Label(rect, trunc);

            if (Mouse.IsOver(rect) && trunc != str)
                TooltipHandler.TipRegion(rect, new TipSignal(str, 3333));
        }

        public static void CheckboxLabeledWithTip(Rect rect, string label, string tip, ref bool check, bool disabled = false)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            if (tip != null)
                TooltipHandler.TipRegion(rect, tip);
            Widgets.CheckboxLabeled(rect, label, ref check, disabled);
        }

        public static bool ButtonTextWithTip(Rect rect, string label, string tip, bool disabled = false, int? tipId = null)
        {
            if (tip != null)
                TooltipHandler.TipRegion(rect, new TipSignal(tip, tipId ?? tip.GetHashCode()));

            using (MpStyle.Set(TextAnchor.MiddleCenter))
            using (MpStyle.Set(disabled ? Color.grey : Color.white))
            {
                var atlas = !disabled && Mouse.IsOver(rect) ?
                    (Input.GetMouseButton(0) ? Widgets.ButtonBGAtlasClick : Widgets.ButtonBGAtlasMouseover) :
                    Widgets.ButtonBGAtlas;

                Widgets.DrawAtlas(rect, atlas);
                Widgets.Label(rect, label);
            }

            var clicked = Widgets.ButtonInvisible(rect, false);

            if (!disabled)
            {
                MouseoverSounds.DoRegion(rect);
                return clicked;
            }

            if (clicked)
                SoundDefOf.ClickReject.PlayOneShotOnCamera();

            return false;
        }

        public static float LabelFlexibleWidth(Rect rect, string label)
        {
            var size = Text.CalcSize(label);
            rect.width = size.x;
            Widgets.Label(rect, label);
            return size.x;
        }
    }
}
