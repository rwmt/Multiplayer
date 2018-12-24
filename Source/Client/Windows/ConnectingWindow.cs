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
            if (Widgets.ButtonText(buttonRect, "CancelButton".Translate(), true, false, true))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            OnMainThread.StopMultiplayer();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        public void Connected() => result = "MpConnected".Translate();
        public void Disconnected() => result = Multiplayer.session.disconnectServerReason ?? Multiplayer.session.disconnectNetReason;
    }

    public class ConnectingWindow : BaseConnectingWindow
    {
        public override string ConnectingString => "MpConnectingTo".Translate(addressStr, port);

        private IPAddress address;
        private int port;
        private string addressStr;

        public ConnectingWindow(IPAddress address, int port)
        {
            this.address = address;
            this.port = port;

            addressStr = address?.ToString();

            ClientUtil.TryConnect(address, port);
        }
    }

    public class SteamConnectingWindow : BaseConnectingWindow
    {
        public override string ConnectingString => (host.NullOrEmpty() ? "" : $"{"MpSteamConnectingTo".Translate(host)}\n") + "MpSteamConnectingWaiting".Translate();

        public CSteamID hostId;
        public string host;

        public SteamConnectingWindow(CSteamID hostId)
        {
            this.hostId = hostId;
            host = SteamFriends.GetFriendPersonaName(hostId);
        }
    }

    public class DisconnectedWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private string reason;

        public DisconnectedWindow(string reason)
        {
            this.reason = reason;

            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float ButtonWidth = 140f;
            const float ButtonHeight = 40f;

            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.MiddleCenter;
            Rect labelRect = inRect;
            labelRect.yMax -= ButtonHeight;
            Widgets.Label(labelRect, reason);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect buttonRect = new Rect((inRect.width - ButtonWidth) / 2f, inRect.height - ButtonHeight - 10f, ButtonWidth, ButtonHeight);
            if (Widgets.ButtonText(buttonRect, "QuitToMainMenu".Translate(), true, false, true))
            {
                GenScene.GoToMainMenu();
            }
        }
    }

}
