using Multiplayer.Common;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    public class DesyncedWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(680, 110);

        private string text;

        public DesyncedWindow(string text)
        {
            this.text = text;

            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(0, 0, inRect.width, 40), $"{"MpDesynced".Translate()}\n{text}");
            Text.Anchor = TextAnchor.UpperLeft;

            float buttonWidth = 120 * 5 + 10 * 4;
            var buttonRect = new Rect((inRect.width - buttonWidth) / 2, 40, buttonWidth, 35);

            GUI.BeginGroup(buttonRect);

            float x = 0;
            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpTryResync".Translate()))
            {
                Multiplayer.session.resyncing = true;

                TickPatch.SkipTo(
                    toTickUntil: true,
                    onFinish: () =>
                    {
                        Multiplayer.session.resyncing = false;
                        Multiplayer.Client.Send(Packets.Client_WorldReady);
                    },
                    cancelButtonKey: "Quit",
                    onCancel: GenScene.GoToMainMenu
                );

                Multiplayer.session.desynced = false;

                ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList(), false);
            }
            x += 120 + 10;

            // todo remove
            //REHOST
            /*if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpTryRehost".Translate()))
            {
                Rehost();
            }
            x += 120 + 10;*/
            //REHOST


            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Save".Translate()))
                Find.WindowStack.Add(new Dialog_SaveReplay());
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpChatButton".Translate()))
                Find.WindowStack.Add(new ChatWindow() { closeOnClickedOutside = true, absorbInputAroundWindow = true, saveSize = false });
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Quit".Translate()))
                MainMenuPatch.AskQuitToMainMenu();

            GUI.EndGroup();
        }
    }

}
