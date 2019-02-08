using Multiplayer.Common;
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
    public abstract class BaseConnectingWindow : Window, IConnectionStatusListener
    {
        public override Vector2 InitialSize => new Vector2(400f, 150f);

        public virtual bool IsConnecting => result == null;
        public abstract string ConnectingString { get; }

        public bool returnToServerBrowser;

        protected string result;
        protected string desc;

        public BaseConnectingWindow()
        {
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            string label = IsConnecting ? (ConnectingString + MpUtil.FixedEllipsis()) : result;

            if (Multiplayer.Client?.StateObj is ClientJoiningState joining && joining.state == JoiningState.Downloading)
                label = $"MpDownloading".Translate(Multiplayer.Client.FragmentProgress);

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
            OnMainThread.StopMultiplayer();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        public void Connected() => result = "MpConnected".Translate();
        public void Disconnected() { }
    }

    public class ConnectingWindow : BaseConnectingWindow
    {
        public override string ConnectingString => string.Format("MpConnectingTo".Translate("{0}", port), address);

        private string address;
        private int port;

        public ConnectingWindow(string address, int port)
        {
            this.address = address;
            this.port = port;

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

}
