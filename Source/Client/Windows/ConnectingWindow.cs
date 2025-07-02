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

            switch (Multiplayer.Client?.StateObj)
            {
                case ClientLoadingState { subState: LoadingState.Waiting }:
                    label = "MpWaitingForGameData".Translate() + MpUI.FixedEllipsis();
                    break;
                case ClientLoadingState { subState: LoadingState.Downloading, WorldExpectedSize: 0 } state:
                    label = "MpDownloading".Translate();
                    label += $"\n{state.WorldReceivedSize / 1000}KB";
                    break;
                case ClientLoadingState { subState: LoadingState.Downloading } state:
                    label = "MpDownloading".Translate() + $" ({state.DownloadProgressPercent}%)";
                    var leftToDownloadKBps = (state.WorldExpectedSize - state.WorldReceivedSize) / 1000;
                    if (state.DownloadSpeedKBps != 0)
                    {
                        var timeLeftSecs = leftToDownloadKBps / state.DownloadSpeedKBps;
                        label +=
                            $"\n{timeLeftSecs}s – ";
                    }
                    else
                        label += "\n";

                    label +=
                        $"{state.WorldReceivedSize / 1000}/{state.WorldExpectedSize / 1000} KB ({state.DownloadSpeedKBps} KB/s)";
                    break;
                default:
                    label = result ?? (ConnectingString + MpUI.FixedEllipsis());
                    break;
            }

            const float buttonHeight = 40f;
            const float buttonWidth = 120f;

            var isDownloading = Multiplayer.Client?.StateObj is ClientLoadingState { subState: LoadingState.Downloading };

            Rect textRect = new Rect(inRect);
            if (isDownloading)
            {
                var textSize = Text.CalcSize(label);
                textRect.height = textSize.y;
            } else
                textRect.height = 60f;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(textRect, label);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.yMax - buttonHeight - 10f, buttonWidth, buttonHeight);
            if (Multiplayer.Client?.StateObj is ClientLoadingState { subState: LoadingState.Downloading } state2)
            {
                Rect progressBarRect = new Rect(inRect)
                {
                    y = textRect.yMax + 10f,
                    height = 30f
                };
                buttonRect.y = progressBarRect.yMax + 10f;
                buttonRect.height = buttonHeight;
                var oldHeight = inRect.height;
                inRect.yMax = buttonRect.yMax + 10f;
                windowRect.height += inRect.height - oldHeight;

                Widgets.FillableBar(progressBarRect, state2.DownloadProgress, Widgets.BarFullTexHor,
                    Widgets.DefaultBarBgTex, doBorder: true);
            }
            else
            {
                windowRect.height = InitialSize.y;
            }

            if (Widgets.ButtonText(buttonRect, "CancelButton".Translate(), true, false))
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

    public class ConnectingWindow(string address, int port) : BaseConnectingWindow
    {
        protected override string ConnectingString =>
            string.Format("MpConnectingTo".Translate("{0}", port), address);
    }

    public class SteamConnectingWindow(CSteamID hostId) : BaseConnectingWindow
    {
        protected override string ConnectingString =>
            (hostUsername.NullOrEmpty() ? "" : $"{"MpSteamConnectingTo".Translate(hostUsername)}\n") +
            "MpSteamConnectingWaiting".Translate();

        public string hostUsername = SteamFriends.GetFriendPersonaName(hostId);
    }
}
