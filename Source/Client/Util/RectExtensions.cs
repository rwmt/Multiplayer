using UnityEngine;

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
    }
}
