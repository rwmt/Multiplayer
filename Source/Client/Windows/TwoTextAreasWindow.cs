using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class TwoTextAreasWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(600, 500);

        private Vector2 scroll1;
        private Vector2 scroll2;

        private string left;
        private string right;

        public TwoTextAreasWindow(string left, string right)
        {
            absorbInputAroundWindow = true;
            doCloseX = true;

            this.left = left;
            this.right = right;
        }

        public override void DoWindowContents(Rect inRect)
        {
            TextAreaScrollable(inRect.LeftHalf(), left, ref scroll1);
            TextAreaScrollable(inRect.RightHalf(), right, ref scroll2);
        }

        private static string TextAreaScrollable(Rect rect, string text, ref Vector2 scrollbarPosition, bool readOnly = false)
        {
            Rect rect2 = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(Text.CalcHeight(text, rect.width) + 10f, rect.height));
            Widgets.BeginScrollView(rect, ref scrollbarPosition, rect2);
            string result = Widgets.TextArea(rect2, text, readOnly);
            Widgets.EndScrollView();
            return result;
        }
    }

}
