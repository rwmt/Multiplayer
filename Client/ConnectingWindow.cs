using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    public abstract class BaseConnectingWindow : Window, IConnectionStatusListener
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        public virtual bool Ellipsis => result == null;
        public abstract string ConnectingString { get; }

        public bool returnToServerBrowser;

        public string result;

        public BaseConnectingWindow()
        {
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            string label = Ellipsis ? (ConnectingString + MpUtil.FixedEllipsis()) : result;

            const float buttonHeight = 40f;
            const float buttonWidth = 120f;

            Rect textRect = inRect;
            textRect.yMax -= (buttonHeight + 10f);
            float textWidth = Text.CalcSize(label).x;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(textRect, label);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.height - buttonHeight - 10f, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(buttonRect, "Cancel", true, false, true))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            Multiplayer.Client?.Close();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        public void Connected() => result = "Connected.";
        public void Disconnected() => result = Multiplayer.session.disconnectServerReason ?? Multiplayer.session.disconnectNetReason;
    }

    public class ConnectingWindow : BaseConnectingWindow
    {
        public override string ConnectingString => $"Connecting to {address}:{port}";

        private IPAddress address;
        private int port;

        public ConnectingWindow(IPAddress address, int port)
        {
            this.address = address;
            this.port = port;

            ClientUtil.TryConnect(address, port);
        }
    }

    public class SteamConnectingWindow : BaseConnectingWindow
    {
        public override string ConnectingString => (host.NullOrEmpty() ? "" : $"Connecting to a game hosted by {host}\n") + "Waiting for host to accept";

        public CSteamID hostId;
        public string host;

        public SteamConnectingWindow(CSteamID hostId)
        {
            this.hostId = hostId;
            host = SteamFriends.GetFriendPersonaName(hostId);
        }
    }
}
