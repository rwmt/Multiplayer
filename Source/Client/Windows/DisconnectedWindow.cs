using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class DisconnectedWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private string reason;
        private string desc;

        public bool returnToServerBrowser;

        public DisconnectedWindow(string reason, string desc = null)
        {
            this.reason = reason;
            this.desc = desc;

            if (reason.NullOrEmpty())
                reason = "Disconnected";

            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float ButtonHeight = 40f;

            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = inRect;
            labelRect.yMax -= ButtonHeight;
            Widgets.Label(labelRect, desc.NullOrEmpty() ? reason : $"<b>{reason}</b>\n{desc}");
            Text.Anchor = TextAnchor.UpperLeft;

            var entry = Current.ProgramState == ProgramState.Entry;

            var buttonWidth = entry ? 120f : 140f;
            var buttonText = entry ? "CloseButton" : "QuitToMainMenu";
            Rect buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.height - ButtonHeight - 10f, buttonWidth, ButtonHeight);

            if (Widgets.ButtonText(buttonRect, buttonText.Translate(), true, false, true))
            {
                if (entry)
                    Close(true);
                else
                    GenScene.GoToMainMenu();
            }
        }

        public override void PostClose()
        {
            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }
    }

}
