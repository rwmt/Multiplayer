using UnityEngine;
using Verse;

namespace Multiplayer.Client
{

    public class DisconnectedWindow : Window
    {
        public override Vector2 InitialSize => new(info.wideWindow ? 430f : 320f, height);

        public override float Margin => 26f;

        private float height;
        protected SessionDisconnectInfo info;
        public bool returnToServerBrowser;

        public DisconnectedWindow(SessionDisconnectInfo info)
        {
            this.info = info;

            if (this.info.titleTranslated.NullOrEmpty())
                this.info.titleTranslated = "MpDisconnected".Translate();

            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        const float ButtonHeight = 40f;
        const float ButtonSpacing = 10f;

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;

            var text = info.descTranslated.NullOrEmpty()
                ? info.titleTranslated
                : $"<b>{info.titleTranslated}</b>\n{info.descTranslated}";

            var buttonHeight = ButtonHeight + ButtonSpacing;
            if (info.specialButtonTranslated != null)
                buttonHeight += ButtonHeight + ButtonSpacing;
            var textHeight = Text.CalcHeight(text, inRect.width);
            height = textHeight + buttonHeight + Margin * 2;

            SetInitialSizeAndPosition();

            Widgets.Label(inRect, text);
            Text.Anchor = TextAnchor.UpperLeft;

            DrawButtons(inRect);
        }

        private void DrawButtons(Rect inRect)
        {
            var isPlaying = Current.ProgramState != ProgramState.Entry;

            var buttonWidth = isPlaying ? 140f : 120f;
            var buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.height - ButtonHeight, buttonWidth, ButtonHeight);
            var buttonText = isPlaying ? "QuitToMainMenu" : "CloseButton";

            if (Widgets.ButtonText(buttonRect, buttonText.Translate()))
            {
                if (isPlaying)
                    GenScene.GoToMainMenu();
                else
                    Close();
            }

            if (info.specialButtonTranslated == null)
                return;

            var connectAsBtn = buttonRect;
            connectAsBtn.y -= ButtonHeight + ButtonSpacing;
            connectAsBtn.width = Text.CalcSize(info.specialButtonTranslated).x + 30;
            connectAsBtn = connectAsBtn.CenteredOnXIn(buttonRect);

            if (Widgets.ButtonText(connectAsBtn, info.specialButtonTranslated))
            {
                returnToServerBrowser = false;
                info.specialButtonAction();
                Close();
            }
        }

        public override void PostClose()
        {
            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }
    }
}
