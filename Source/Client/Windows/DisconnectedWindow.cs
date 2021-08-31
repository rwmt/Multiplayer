using System;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class DisconnectedWindow : Window
    {
        public override Vector2 InitialSize => new(320f, info.specialButtonTranslated != null ? 210f : 160f);

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

            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = inRect;

            labelRect.yMax -= ButtonHeight + ButtonSpacing;
            if (info.specialButtonTranslated != null)
                labelRect.yMax -= ButtonHeight + ButtonSpacing;

            var text = info.descTranslated.NullOrEmpty()
                ? info.titleTranslated
                : $"<b>{info.titleTranslated}</b>\n{info.descTranslated}";
            Widgets.Label(labelRect, text);

            Text.Anchor = TextAnchor.UpperLeft;

            DrawButtons(inRect);
        }

        private void DrawButtons(Rect inRect)
        {
            var isPlaying = Current.ProgramState != ProgramState.Entry;

            var buttonWidth = isPlaying ? 140f : 120f;
            var buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.height - ButtonHeight - ButtonSpacing, buttonWidth, ButtonHeight);
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
