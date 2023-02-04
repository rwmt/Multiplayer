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
            Widgets.TextAreaScrollable(inRect.LeftHalf(), left, ref scroll1);
            Widgets.TextAreaScrollable(inRect.RightHalf(), right, ref scroll2);
        }
    }

}
