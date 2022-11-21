using Multiplayer.Common;
using System.Linq;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class DesyncedWindow : Window
    {
        const int NumButtons = 5;
        const float ButtonsWidth = 120 * NumButtons + 10 * (NumButtons - 1);

        public override Vector2 InitialSize => new(30 + 130 * NumButtons, 110);

        private string text;
        private float openedAt;
        private bool infoWritten;
        private string localTraces;

        public DesyncedWindow(string text, string localTraces)
        {
            this.text = text;
            this.localTraces = localTraces;

            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            absorbInputAroundWindow = true;
            openedAt = Time.realtimeSinceStartup;

            layer = WindowLayer.Super;

#if DEBUG
            doCloseX = true;
#endif
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(0, 0, inRect.width, 40), $"{"MpDesynced".Translate()}\n{text}");
            Text.Anchor = TextAnchor.UpperLeft;

            var buttonsRect = new Rect((inRect.width - ButtonsWidth) / 2, 40, ButtonsWidth, 35);

            GUI.BeginGroup(buttonsRect);

            float x = 0;
            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpTryResync".Translate()))
            {
                Log.Message("Multiplayer: requesting rejoin");
                Multiplayer.Client.Send(Packets.Client_RequestRejoin);
            }

            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Save".Translate()))
                Find.WindowStack.Add(new SaveGameWindow(Multiplayer.session.gameName));
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpChatButton".Translate()))
                Find.WindowStack.Add(new ChatWindow() { closeOnClickedOutside = true, absorbInputAroundWindow = true, saveSize = false });
            x += 120 + 10;

            var openDesyncsFolder = MpUI.ButtonTextWithTip(
                new Rect(x, 0, 120, 35),
                "MpOpenDesyncFolder".Translate(),
                infoWritten ? null : "MpDesyncedWaiting".Translate() + MpUI.FixedEllipsis(), !infoWritten, 9793021
            );
            if (openDesyncsFolder)
                ShellOpenDirectory.Execute(Multiplayer.DesyncsDir);
            x += 120 + 10;

            if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Quit".Translate()))
                MainMenuPatch.AskQuitToMainMenu();

            GUI.EndGroup();
        }

        public override void WindowUpdate()
        {
            const float maxWait = 5f;

            var shouldWrite = Multiplayer.session?.desyncTracesFromHost != null || Time.realtimeSinceStartup - openedAt > maxWait;
            if (!infoWritten && shouldWrite)
            {
                Multiplayer.game?.sync.WriteDesyncInfo(localTraces);
                infoWritten = true;
            }
        }
    }

}
