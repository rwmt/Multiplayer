using Multiplayer.Client.Networking;
using Steamworks;
using System.Linq;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public abstract class BaseConnectingWindow : Window, IConnectionStatusListener
    {
        public override Vector2 InitialSize => new(400f, 150f);

        protected bool IsConnecting => result == null;
        protected abstract string ConnectingString { get; }

        public bool returnToServerBrowser;
        protected string result;

        // Only show this window if there aren't any others during connecting
        private bool ShouldShow => Find.WindowStack.Windows.Count(w => w.layer == WindowLayer.Dialog) == 1;

        public BaseConnectingWindow()
        {
            closeOnAccept = false;
            closeOnCancel = false;
        }

        // ExtraOnGUI is called before drawing the window shadow and WindowOnGUI
        public override void ExtraOnGUI()
        {
            drawShadow = ShouldShow;
        }

        public override void WindowOnGUI()
        {
            if (ShouldShow)
                base.WindowOnGUI();
        }

        public override void DoWindowContents(Rect inRect)
        {
            string label;

            if (Multiplayer.Client?.StateObj is ClientJoiningState { subState: JoiningState.Waiting })
                label = "MpWaitingForGameData".Translate() + MpUI.FixedEllipsis();
            else if (Multiplayer.Client?.StateObj is ClientJoiningState { subState: JoiningState.Downloading })
                label = "MpDownloading".Translate(Multiplayer.Client.FragmentProgress);
            else
                label = IsConnecting ? (ConnectingString + MpUI.FixedEllipsis()) : result;

            const float buttonHeight = 40f;
            const float buttonWidth = 120f;

            Rect textRect = inRect;
            textRect.yMax -= (buttonHeight + 10f);
            Text.Anchor = TextAnchor.MiddleCenter;

            Widgets.Label(textRect, label);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.height - buttonHeight - 10f, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(buttonRect, "CancelButton".Translate(), true, false, true))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            Multiplayer.StopMultiplayer();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        public void Connected() => result = "MpConnected".Translate();
        public void Disconnected() { }
    }

    public class RejoiningWindow : BaseConnectingWindow
    {
        protected override string ConnectingString => "MpJoining".Translate();
    }

    public class ConnectingWindow : BaseConnectingWindow
    {
        protected override string ConnectingString => string.Format("MpConnectingTo".Translate("{0}", port), address);

        private string address;
        private int port;

        public ConnectingWindow(string address, int port)
        {
            this.address = address;
            this.port = port;
        }
    }

    public class SteamConnectingWindow : BaseConnectingWindow
    {
        protected override string ConnectingString => (hostUsername.NullOrEmpty() ? "" : $"{"MpSteamConnectingTo".Translate(hostUsername)}\n") + "MpSteamConnectingWaiting".Translate();

        public string hostUsername;

        public SteamConnectingWindow(CSteamID hostId)
        {
            hostUsername = SteamFriends.GetFriendPersonaName(hostId);
        }
    }

}
