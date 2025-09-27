using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class RectExtensions
    {
        public static Rect Under(this Rect rect, float height)
        {
            return new Rect(rect.xMin, rect.yMax, rect.width, height);
        }

        public static Rect Down(this Rect rect, float y)
        {
            rect.y += y;
            return rect;
        }

        public static Rect Up(this Rect rect, float y)
        {
            rect.y -= y;
            return rect;
        }

        public static Rect Right(this Rect rect, float x)
        {
            rect.x += x;
            return rect;
        }

        public static Rect Left(this Rect rect, float x)
        {
            rect.x -= x;
            return rect;
        }

        public static Rect Width(this Rect rect, float width)
        {
            rect.width = width;
            return rect;
        }

        public static Rect Height(this Rect rect, float height)
        {
            rect.height = height;
            return rect;
        }

        public static Rect CenterOn(this Rect rect, Rect on)
        {
            rect.x = on.x + (on.width - rect.width) / 2f;
            rect.y = on.y + (on.height - rect.height) / 2f;
            return rect;
        }

        public static Rect CenterOn(this Rect rect, Vector2 on)
        {
            rect.x = on.x - rect.width / 2f;
            rect.y = on.y - rect.height / 2f;
            return rect;
        }

        public static Vector2 BottomLeftCorner(this Rect rect)
        {
            return new Vector2(rect.xMin, rect.yMax);
        }

        public static Vector2 TopRightCorner(this Rect rect)
        {
            return new Vector2(rect.xMax, rect.yMin);
        }

        public static Rect ExpandedBy(this Vector2 center, float expand)
        {
            return new Rect(center.x - expand, center.y - expand, 2 * expand, 2 * expand);
        }

        public static Rect MinX(this Rect rect, float x)
        {
            rect.xMin = x;
            return rect;
        }

        public static Rect MinY(this Rect rect, float y)
        {
            rect.yMin = y;
            return rect;
        }

        public static Rect MaxX(this Rect rect, float x)
        {
            rect.xMax = x;
            return rect;
        }

        public static Rect MaxY(this Rect rect, float y)
        {
            rect.yMax = y;
            return rect;
        }

        public static Rect AtX(this Rect rect, float x)
        {
            rect.x = x;
            return rect;
        }

        public static Rect AtY(this Rect rect, float y)
        {
            rect.y = y;
            return rect;
        }

        public static Rect FitToText(this Rect rect, string text)
        {
            var textSize = Text.CalcSize(text);
            rect.width = textSize.x;
            rect.height = textSize.y;
            return rect;
        }

        public static Rect FitToTextWithinWidth(this Rect rect, string text)
        {
            var height = Text.CalcHeight(text, rect.width);
            rect.height = height;
            return rect;
        }

        public static Rect MarginLeft(this Rect rect, float margin)
        {
            rect.x += margin;
            rect.width -= margin;
            return rect;
        }

        public static Rect MarginRight(this Rect rect, float margin)
        {
            rect.width -= margin;
            return rect;
        }

        public static Rect MarginTop(this Rect rect, float margin)
        {
            rect.y += margin;
            rect.height -= margin;
            return rect;
        }

        public static Rect MarginBottom(this Rect rect, float margin)
        {
            rect.height -= margin;
            return rect;
        }

        public static Rect CenteredButtonX(this Rect rect, int buttonIndex,
            int buttonsCount,
            float buttonWidth,
            float pad = 10f)
        {
            rect.x += GenUI.GetCenteredButtonPos(buttonIndex, buttonsCount, rect.width, buttonWidth, pad);
            rect.width = buttonWidth;
            return rect;
        }

        public static Rect SpacedEvenlyX(this Rect rect, int buttonIndex, int buttonsCount, float buttonWidth)
        {
            float totalWidth = rect.width;
            float pad = (float)(((double) totalWidth - (double)buttonsCount * (double) buttonWidth) / (double) (buttonsCount + 1));
            rect.x += Mathf.Floor(pad + buttonIndex * (buttonWidth + pad));
            rect.width = buttonWidth;
            return rect;
        }

        public static Rect SpacedAroundX(this Rect rect, int buttonIndex, int buttonsCount, float buttonWidth)
        {
            float totalWidth = rect.width;
            float pad = (float)(((double) totalWidth - (double)buttonsCount * (double) buttonWidth) / (double) (buttonsCount));
            rect.x += Mathf.Floor(pad/2 + buttonIndex * (buttonWidth + pad));
            rect.width = buttonWidth;
            return rect;
        }

        public static Rect SpacedBetweenX(this Rect rect, int buttonIndex, int buttonsCount, float buttonWidth)
        {
            float totalWidth = rect.width;
            float pad = (float)(((double) totalWidth - (double)buttonsCount * (double) buttonWidth) / (double) (buttonsCount - 1));
            rect.x += Mathf.Floor(buttonIndex * (buttonWidth + pad));
            rect.width = buttonWidth;
            return rect;
        }
    }
}
