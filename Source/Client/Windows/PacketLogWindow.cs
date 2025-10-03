using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{

    public class PacketLogWindow : Window
    {
        public override Vector2 InitialSize => new(UI.screenWidth / 2f, UI.screenHeight / 2f);

        private class UINode(string text)
        {
            public string text = text;
            public List<UINode> children = [];
            public bool expanded;

            public static UINode FromLogNode(LogNode node)
            {
                return new UINode(node.text)
                {
                     children = node.children.Select(FromLogNode).ToList()
                };
            }

            public void AttachStackTrace()
            {
                var stackTrace = StackTraceUtility.ExtractStackTrace();
                // Removes the first 3 frames - ExtractStackTrace, AttachStackTrace and AddCurrentNode
                var index = stackTrace.IndexOfOccurrence('\n', 3) + 1;
                stackTrace = stackTrace[index..];
                children.Add(new UINode("Stack trace")
                {
                    children = [new UINode(stackTrace)]
                });
            }
        }

        private List<UINode> nodes = new();
        private int logHeight;
        private Vector2 scrollPos;
        private bool storeStackTrace;

        public int NodeCount => nodes.Count;

        public PacketLogWindow(bool storeStackTrace)
        {
            doCloseX = true;

            this.storeStackTrace = storeStackTrace;
        }

        public override void DoWindowContents(Rect rect)
        {
            GUI.BeginGroup(rect);

            Text.Font = GameFont.Tiny;
            Rect outRect = new Rect(0f, 0f, rect.width, rect.height - 30f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, logHeight + 10f);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            Rect nodeRect = new Rect(0f, 0f, viewRect.width - 16f, 20f);
            foreach (var node in nodes)
                Draw(node, 0, ref nodeRect);

            if (Event.current.type == EventType.Layout)
                logHeight = (int)nodeRect.y;

            Widgets.EndScrollView();

            GUI.EndGroup();
        }

        private void Draw(UINode node, int depth, ref Rect rect)
        {
            string text = node.text;
            if (depth == 0 && text == SyncLogger.RootNodeName)
                text = node.children[0].text;

            rect.x = depth * 15;
            if (node.children.Count > 0)
            {
                Widgets.Label(rect, node.expanded ? "[-]" : "[+]");
                rect.x += 15;
            }

            // Taken from Verse.Text.CalcHeight, edited not to remove "XML tags"
            Text.tmpTextGUIContent.text = text;
            rect.height = Text.CurFontStyle.CalcHeight(Text.tmpTextGUIContent, rect.width);

            Widgets.Label(rect, text);
            if (Widgets.ButtonInvisible(rect))
                node.expanded = !node.expanded;

            rect.y += (int)rect.height;

            if (node.expanded)
                foreach (var child in node.children)
                    Draw(child, depth + 1, ref rect);
        }

        public void AddCurrentNode(IHasLogger hasLogger)
        {
            var node = UINode.FromLogNode(hasLogger.Log.current);
            if (storeStackTrace) node.AttachStackTrace();
            nodes.Add(node);
        }
    }

}
