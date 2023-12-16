using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class Layouter
{
    #region Data
    private class El
    {
        public El? nextTopLevel;

        public El? parent;
        public List<El> children = new();
        public int currentChild;
        public Action<Rect>? drawer;

        public bool horizontal;
        public DimensionMode widthMode, heightMode;
        public int stretchChildrenX, stretchChildrenY;
        public float aspectRatio;
        public float childrenHeight;
        public float paddingRight;
        public bool scroll;

        public string? stringContent;
        public GUIStyle? style;

        public float spacing;
        public Rect rect;

        private static int indent;

        public override string ToString()
        {
            string output = new string(' ', indent) + $"Horizontal:{horizontal} Width:{widthMode},{stretchChildrenX} Height:{heightMode},{stretchChildrenY} Rect:{rect} PaddingRight:{paddingRight}";

            if (children.Any())
                output += "\n";

            indent++;
            for (int i = 0; i < children.Count; i++)
                output += (i != 0 ? "\n" : "") + new string(' ', indent) + children[i];
            indent--;

            return output;
        }
    }

    enum DimensionMode
    {
        Fixed,
        Stretch,
        Decide, // Stretch if there are any Stretch children, otherwise Fixed with max child dimension
        AspectRatio, // Only possible for height (height depends on width)
        ContentHeight, // Only possible for height (height depends on width)
        SumFixedChildren, // Only possible for height
        MaxFixedSibling, // Only possible for height
    }

    private static El? currentGroup;

    private static Dictionary<int, El?> firstTopLevel = new (); // First element of linked list of areas per window id
    private static El? currentTopLevel;

    private static readonly Rect DummyRect = new(0f, 0f, 1f, 1f);

    private const int NoWindowId = int.MaxValue;
    #endregion

    #region Main
    public static void BeginFrame()
    {
        if (Event.current.type == EventType.Layout)
            firstTopLevel.Clear();

        currentTopLevel = null;
        currentGroup = null;
    }

    private static void DoDrawers(El parent)
    {
        parent.drawer?.Invoke(parent.rect);

        foreach (var el in parent.children)
            DoDrawers(el);
    }

    public static string DebugString()
    {
        return firstTopLevel.GetValueOrDefault(Find.WindowStack.focusedWindow.ID)?.ToString() ??
               "No layout for focused window";
    }

    private static El GetNextChild()
    {
        var child = currentGroup!.children[currentGroup.currentChild++];
        return child;
    }
    #endregion

    #region Groups
    private static void PushGroup(El el)
    {
        if (currentGroup != null)
        {
            el.parent = currentGroup;
            currentGroup.children.Add(el);
        }

        currentGroup = el;
    }

    private static void PopGroup()
    {
        currentGroup = currentGroup!.parent;
    }

    public static bool BeginArea(Rect rect, float spacing = 10f)
    {
        return BeginArea(Find.WindowStack.currentlyDrawnWindow?.ID ?? NoWindowId, rect, spacing);
    }

    public static bool BeginArea(int windowId, Rect rect, float spacing = 10f)
    {
        if (Event.current.type == EventType.Layout)
        {
            PushGroup(new El { rect = rect.AtZero(), spacing = spacing });

            // Add to linked list
            if (!firstTopLevel.ContainsKey(windowId))
                firstTopLevel[windowId] = currentGroup;
            else
                currentTopLevel!.nextTopLevel = currentGroup;

            // Set current list pointer
            currentTopLevel = currentGroup;
        }
        else
        {
            if (!firstTopLevel.ContainsKey(windowId))
                return false;

            currentTopLevel = currentTopLevel == null ?
                firstTopLevel[windowId] :
                currentTopLevel.nextTopLevel;
            currentGroup = currentTopLevel;
        }

        GUI.BeginGroup(rect);
        return true;
    }

    public static void EndArea()
    {
        if (Event.current.type == EventType.Layout)
            Layout();

        PopGroup();

        // Calling drawers after layout but still within GUI group
        DoDrawers(currentTopLevel!);

        if (currentTopLevel?.nextTopLevel == null)
            currentTopLevel = null;

        GUI.EndGroup();
    }

    public static void BeginHorizontal(float spacing = 10f)
    {
        if (Event.current.type == EventType.Layout)
            PushGroup(new El { widthMode = DimensionMode.Stretch, heightMode = DimensionMode.Decide, horizontal = true, spacing = spacing});
        else
            currentGroup = GetNextChild();
    }

    public static void EndHorizontal()
    {
        PopGroup();
    }

    public static void BeginHorizontalCenter()
    {
        BeginHorizontal();
        FlexibleWidth();
    }

    public static void EndHorizontalCenter()
    {
        FlexibleWidth();
        EndHorizontal();
    }

    public static void BeginVertical(float spacing = 10f, bool stretch = true)
    {
        if (Event.current.type == EventType.Layout)
            PushGroup(new El { widthMode = DimensionMode.Decide, heightMode = stretch ? DimensionMode.Stretch : DimensionMode.SumFixedChildren, horizontal = false, spacing = spacing});
        else
            currentGroup = GetNextChild();
    }

    public static void BeginVerticalInLastRect(float spacing = 10f)
    {
        if (Event.current.type == EventType.Layout)
        {
            LastEl().parent = currentGroup;
            currentGroup = LastEl();
            currentGroup.spacing = spacing;
        } else
        {
            currentGroup = LastEl();
        }
    }

    public static void EndVertical()
    {
        PopGroup();
    }

    public static void BeginScroll(ref Vector2 scrollPos, float spacing = 10f)
    {
        BeginVertical(spacing);

        var outRect = currentGroup!.rect;
        currentGroup.scroll = true;

        var viewRect = new Rect(outRect.x, outRect.y, outRect.width - currentGroup.paddingRight, currentGroup.childrenHeight);

        if (currentGroup.paddingRight != 0f)
            Widgets.BeginScrollView(
                outRect,
                ref scrollPos,
                viewRect);
    }

    public static void EndScroll()
    {
        if (currentGroup.paddingRight != 0f)
            Widgets.EndScrollView();
        EndVertical();
    }
    #endregion

    #region Rects
    public static Rect Rect(float width, float height)
    {
        if (Event.current.type == EventType.Layout)
        {
            currentGroup!.children.Add(new El()
            {
                rect = new Rect(0, 0, width, height),
                heightMode = DimensionMode.Fixed,
                widthMode = DimensionMode.Fixed
            });
            return DummyRect;
        }

        return GetNextChild().rect;
    }

    public static Rect AspectRect(float widthByHeight)
    {
        if (Event.current.type == EventType.Layout)
        {
            currentGroup!.children.Add(new El()
                { widthMode = DimensionMode.Stretch, heightMode = DimensionMode.AspectRatio, aspectRatio = widthByHeight });
            return DummyRect;
        }

        return GetNextChild().rect;
    }

    public static Rect ContentRect(string stringContent)
    {
        if (Event.current.type == EventType.Layout)
        {
            currentGroup!.children.Add(new El()
                { widthMode = DimensionMode.Stretch, heightMode = DimensionMode.ContentHeight, stringContent = stringContent, style = new GUIStyle(Text.CurFontStyle) });
            return DummyRect;
        }

        return GetNextChild().rect;
    }

    public static Rect FlexibleSpace()
    {
        if (Event.current.type == EventType.Layout)
        {
            currentGroup!.children.Add(new El()
                { widthMode = DimensionMode.Stretch, heightMode = DimensionMode.Stretch });
            return DummyRect;
        }

        return GetNextChild().rect;
    }

    // Postpones drawing the rect until after it's laid out
    // This allows nesting areas
    public static void PostponeFlexible(Action<Rect> drawer)
    {
        FlexibleSpace();

        if (Event.current.type == EventType.Layout)
            currentGroup!.children.Last().drawer = drawer;
    }

    public static Rect FlexibleWidth()
    {
        if (Event.current.type == EventType.Layout)
        {
            currentGroup!.children.Add(new El()
                { widthMode = DimensionMode.Stretch, heightMode = DimensionMode.MaxFixedSibling });
            return DummyRect;
        }

        return GetNextChild().rect;
    }

    public static Rect FixedWidth(float width)
    {
        if (Event.current.type == EventType.Layout)
        {
            currentGroup!.children.Add(new El()
                { rect = new Rect(0, 0, width, 0), widthMode = DimensionMode.Fixed, heightMode = DimensionMode.MaxFixedSibling });
            return DummyRect;
        }

        return GetNextChild().rect;
    }

    public static Rect FixedHeight(float height)
    {
        if (Event.current.type == EventType.Layout)
        {
            currentGroup!.children.Add(new El()
                { rect = new Rect(0, 0, 0, height), widthMode = DimensionMode.Stretch, heightMode = DimensionMode.Fixed });
            return DummyRect;
        }

        return GetNextChild().rect;
    }

    private static El LastEl()
    {
        return
            Event.current.type == EventType.Layout ?
                currentGroup!.children.Last() :
                currentGroup!.children[currentGroup.currentChild - 1];
    }

    public static Rect LastRect()
    {
        return LastEl().rect;
    }

    public static Rect GroupRect()
    {
        return currentGroup!.rect;
    }
    #endregion

    #region UI elements
    public static void Label(string text, bool inheritHeight = false)
    {
        GUI.Label(inheritHeight ? FlexibleWidth() : ContentRect(text), text, Text.CurFontStyle);
    }

    public static bool Button(string text, float width, float height = 35f)
    {
        return Widgets.ButtonText(Rect(width, height), text);
    }
    #endregion

    #region Algorithm
    private static void Layout()
    {
        BottomUpWidth(currentTopLevel!);
        TopDownWidth(currentTopLevel!);

        BottomUpHeight(currentTopLevel!);
        TopDownHeight(currentTopLevel!);

        TopDownWidth(currentTopLevel!); // another pass for scroll views

        TopDownPosition(currentTopLevel!);
    }

    private static void BottomUpWidth(El parent)
    {
        foreach (var el in parent.children)
            BottomUpWidth(el);

        foreach (var el in parent.children)
        {
            if (el.widthMode == DimensionMode.Stretch)
            {
                if (parent.widthMode == DimensionMode.Decide)
                    parent.widthMode = DimensionMode.Stretch;
                parent.stretchChildrenX++;
            }
        }

        if (parent.widthMode == DimensionMode.Decide)
        {
            foreach (var el in parent.children)
                parent.rect.width = Mathf.Max(parent.rect.width, el.rect.width);

            parent.widthMode = DimensionMode.Fixed;
        }
    }

    private static void BottomUpHeight(El parent)
    {
        foreach (var el in parent.children)
            BottomUpHeight(el);

        foreach (var el in parent.children)
        {
            if (el.heightMode == DimensionMode.Stretch)
            {
                if (parent.heightMode == DimensionMode.Decide)
                    parent.heightMode = DimensionMode.Stretch;
                parent.stretchChildrenY++;
            }
        }

        if (parent.heightMode == DimensionMode.SumFixedChildren)
        {
            foreach (var el in parent.children)
                parent.rect.height += el.rect.height;

            if (parent.children.Any())
                parent.rect.height += parent.spacing * parent.children.Count - 1;

            parent.heightMode = DimensionMode.Fixed;
        }

        if (parent.heightMode == DimensionMode.Decide)
        {
            foreach (var el in parent.children)
                parent.rect.height = Mathf.Max(parent.rect.height, el.rect.height);

            parent.heightMode = DimensionMode.Fixed;
        }
    }

    private static void TopDownWidth(El parent)
    {
        if (parent.scroll && parent.childrenHeight > parent.rect.height)
            parent.paddingRight = 16f;

        float childrenWidth = parent.horizontal ? (parent.children.Count - 1) * parent.spacing : 0;

        // Collect known child dimensions
        foreach (var el in parent.children)
        {
            if (el.widthMode == DimensionMode.Fixed)
                childrenWidth += el.rect.width;
        }

        // Size flexible children
        foreach (var el in parent.children)
        {
            if (el.widthMode == DimensionMode.Stretch)
                el.rect.width = parent.horizontal && parent.stretchChildrenX > 0 ?
                    (parent.rect.width - childrenWidth - parent.paddingRight) / parent.stretchChildrenX :
                    parent.rect.width - parent.paddingRight;
        }

        if (parent.heightMode == DimensionMode.AspectRatio)
        {
            parent.rect.height = parent.rect.width / parent.aspectRatio;
            parent.heightMode = DimensionMode.Fixed;
        }

        if (parent.heightMode == DimensionMode.ContentHeight)
        {
            Text.tmpTextGUIContent.text = parent.stringContent.StripTags();
            parent.rect.height = parent.style!.CalcHeight(Text.tmpTextGUIContent, parent.rect.width);
            parent.heightMode = DimensionMode.Fixed;
        }

        foreach (var el in parent.children)
            TopDownWidth(el);
    }

    private static void TopDownHeight(El parent)
    {
        parent.childrenHeight = !parent.horizontal ? (parent.children.Count - 1) * parent.spacing : 0;
        var maxChildHeight = 0f;

        // Collect known child dimensions
        foreach (var el in parent.children)
        {
            if (el.heightMode == DimensionMode.Fixed)
            {
                parent.childrenHeight += el.rect.height;
                maxChildHeight = Mathf.Max(maxChildHeight, el.rect.height);
            }
        }

        // Size flexible children
        foreach (var el in parent.children)
        {
            if (el.heightMode == DimensionMode.Stretch)
                el.rect.height += !parent.horizontal && parent.stretchChildrenY > 0 ?
                    (parent.rect.height - parent.childrenHeight) / parent.stretchChildrenY :
                    parent.rect.height;

            if (el.heightMode == DimensionMode.MaxFixedSibling)
                el.rect.height = maxChildHeight;
        }

        foreach (var el in parent.children)
            TopDownHeight(el);
    }

    private static void TopDownPosition(El parent)
    {
        float lastPos = parent.horizontal ? parent.rect.x : parent.rect.y;

        // Layout children in sequence
        foreach (var el in parent.children)
        {
            if (parent.horizontal)
            {
                el.rect.x = lastPos;
                el.rect.y = parent.rect.y;
                lastPos += el.rect.width;
            }
            else
            {
                el.rect.y = lastPos;
                el.rect.x = parent.rect.x;
                lastPos += el.rect.height;
            }

            if (el != parent.children.Last())
                lastPos += parent.spacing;
        }

        foreach (var el in parent.children)
            TopDownPosition(el);
    }
    #endregion
}
