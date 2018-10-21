using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    public class ServerBrowser : Window
    {
        private NetManager net;
        private List<LanServer> servers = new List<LanServer>();

        public override Vector2 InitialSize => new Vector2(600f, 400f);

        public ServerBrowser()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            listener.NetworkReceiveUnconnectedEvent += (endpoint, data, type) =>
            {
                if (type != UnconnectedMessageType.DiscoveryRequest) return;

                string s = Encoding.UTF8.GetString(data.GetRemainingBytes());
                if (s == "mp-server")
                    AddOrUpdate(endpoint);
            };

            net = new NetManager(listener);
            net.DiscoveryEnabled = true;
            net.ReuseAddress = true;
            net.UpdateTime = 500;
            net.Start(5100);

            doCloseX = true;
            closeOnAccept = false;
        }

        private Vector2 lanScroll;
        private Vector2 steamScroll;
        private Tabs tab;

        enum Tabs
        {
            Lan, Direct, Steam, Replays
        }

        public override void DoWindowContents(Rect inRect)
        {
            List<TabRecord> tabs = new List<TabRecord>()
            {
                new TabRecord("LAN", () => tab = Tabs.Lan,  tab == Tabs.Lan),
                new TabRecord("Direct", () => tab = Tabs.Direct, tab == Tabs.Direct),
                new TabRecord("Steam", () => tab = Tabs.Steam, tab == Tabs.Steam),
                new TabRecord("Replays", () => tab = Tabs.Replays, tab == Tabs.Replays),
            };

            inRect.yMin += 35f;
            TabDrawer.DrawTabs(inRect, tabs);

            GUI.BeginGroup(new Rect(0, inRect.yMin, inRect.width, inRect.height));
            {
                Rect groupRect = new Rect(0, 0, inRect.width, inRect.height);

                if (tab == Tabs.Lan)
                    DrawLan(groupRect);
                else if (tab == Tabs.Direct)
                    DrawDirect(groupRect);
                else if (tab == Tabs.Steam)
                    DrawSteam(groupRect);
                //else if (tab == Tabs.Replays)
                    //Multiplayer.LoadReplay();
            }
            GUI.EndGroup();
        }

        private List<SteamPersona> friends = new List<SteamPersona>();
        private static readonly Color SteamGreen = new Color32(144, 186, 60, 255);

        private void DrawSteam(Rect inRect)
        {
            string info = null;
            if (!SteamManager.Initialized)
                info = "Not connected to Steam";
            else if (friends.Count == 0)
                info = "No friends currently playing RimWorld";

            if (info != null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0, 8, inRect.width, 40f), info);

                Text.Anchor = TextAnchor.UpperLeft;
                inRect.yMin += 40f;
            }

            float margin = 80;
            Rect outRect = new Rect(margin, inRect.yMin + 10, inRect.width - 2 * margin, inRect.height - 20);

            float height = friends.Count * 40;
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, height);

            Widgets.BeginScrollView(outRect, ref steamScroll, viewRect, true);

            float y = 0;
            int i = 0;

            foreach (SteamPersona friend in friends)
            {
                Rect entryRect = new Rect(0, y, viewRect.width, 40);
                if (i % 2 == 0)
                    Widgets.DrawAltRect(entryRect);

                if (Event.current.type == EventType.repaint)
                    GUI.DrawTextureWithTexCoords(new Rect(5, entryRect.y + 4, 32, 32), SteamImages.GetTexture(friend.avatar), new Rect(0, 1, 1, -1));

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect.Right(45).Up(5), friend.username);

                GUI.color = SteamGreen;
                Text.Font = GameFont.Tiny;
                Widgets.Label(entryRect.Right(45).Down(8), "Playing RimWorld");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                Text.Anchor = TextAnchor.UpperLeft;

                Text.Anchor = TextAnchor.MiddleCenter;

                if (friend.serverHost != CSteamID.Nil)
                {
                    Rect playButton = new Rect(entryRect.xMax - 85, entryRect.y + 5, 80, 40 - 10);
                    if (Widgets.ButtonText(playButton, "Join"))
                    {
                        Close(false);

                        Find.WindowStack.Add(new SteamConnectingWindow(friend.serverHost));

                        SteamConnection conn = new SteamConnection(friend.serverHost);
                        conn.username = Multiplayer.username;
                        Multiplayer.session = new MultiplayerSession();
                        Multiplayer.session.client = conn;
                        conn.State = ConnectionStateEnum.ClientSteam;
                    }
                }
                else
                {
                    Rect playButton = new Rect(entryRect.xMax - 125, entryRect.y + 5, 120, 40 - 10);
                    Widgets.ButtonText(playButton, "Not in multiplayer", false, false, false);
                }

                Text.Anchor = TextAnchor.UpperLeft;

                y += entryRect.height;
                i++;
            }

            Widgets.EndScrollView();
        }

        private string ip = "127.0.0.1";

        private void DrawDirect(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(inRect.center.x - 200 / 2, 15f, 200, 35f), ip);

            if (Widgets.ButtonText(new Rect(inRect.center.x - 100f / 2, 60f, 100f, 35f), "Connect"))
            {
                int port = MultiplayerServer.DefaultPort;
                string[] ipport = ip.Split(':');
                if (ipport.Length == 2)
                    int.TryParse(ipport[1], out port);
                else
                    port = MultiplayerServer.DefaultPort;

                if (!IPAddress.TryParse(ipport[0], out IPAddress address))
                {
                    Messages.Message("Invalid IP address.", MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    this.Close(true);
                    Find.WindowStack.Add(new ConnectingWindow(address, port));
                }
            }
        }

        private void DrawLan(Rect inRect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inRect.x, 8f, inRect.width, 40), "Searching" + Multiplayer.FixedEllipsis());
            Text.Anchor = TextAnchor.UpperLeft;
            inRect.yMin += 40f;

            float margin = 100;
            Rect outRect = new Rect(margin, inRect.yMin + 10, inRect.width - 2 * margin, inRect.height - 20);

            float height = servers.Count * 40;
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, height);

            Widgets.BeginScrollView(outRect, ref lanScroll, viewRect, true);

            float y = 0;
            int i = 0;

            foreach (LanServer server in servers)
            {
                Rect entryRect = new Rect(0, y, viewRect.width, 40);
                if (i % 2 == 0)
                    Widgets.DrawAltRect(entryRect);

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect.Right(5), "" + server.endpoint);

                Text.Anchor = TextAnchor.MiddleCenter;
                Rect playButton = new Rect(entryRect.xMax - 75, entryRect.y + 5, 70, 40 - 10);
                if (Widgets.ButtonText(playButton, ">>"))
                {
                    Close(false);
                    Find.WindowStack.Add(new ConnectingWindow(server.endpoint.Address, server.endpoint.Port));
                }

                Text.Anchor = TextAnchor.UpperLeft;

                y += entryRect.height;
                i++;
            }

            Widgets.EndScrollView();
        }

        public override void WindowUpdate()
        {
            UpdateLan();

            if (SteamManager.Initialized)
                UpdateSteam();
        }

        private void UpdateLan()
        {
            net.PollEvents();

            for (int i = servers.Count - 1; i >= 0; i--)
            {
                LanServer server = servers[i];
                if (Environment.TickCount - server.lastUpdate > 5000)
                    servers.RemoveAt(i);
            }
        }

        private int lastFriendUpdate = 0;

        private void UpdateSteam()
        {
            if (Environment.TickCount - lastFriendUpdate > 2000)
            {
                friends.Clear();

                int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < friendCount; i++)
                {
                    CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t friendGame);
                    bool playingRimworld = friendGame.m_gameID.AppID() == Multiplayer.RimWorldAppId;
                    if (!playingRimworld) continue;

                    int avatar = SteamFriends.GetSmallFriendAvatar(friend);
                    string username = SteamFriends.GetFriendPersonaName(friend);
                    string connectValue = SteamFriends.GetFriendRichPresence(friend, "connect");

                    CSteamID serverHost = CSteamID.Nil;
                    if (connectValue != null &&
                        connectValue.Contains(Multiplayer.SteamConnectStart) &&
                        ulong.TryParse(connectValue.Substring(Multiplayer.SteamConnectStart.Length), out ulong hostId))
                    {
                        serverHost = (CSteamID)hostId;
                    }

                    friends.Add(new SteamPersona()
                    {
                        id = friend,
                        avatar = avatar,
                        username = username,
                        playingRimworld = playingRimworld,
                        serverHost = serverHost,
                    });
                }

                friends.SortByDescending(f => f.serverHost != CSteamID.Nil);

                lastFriendUpdate = Environment.TickCount;
            }
        }

        public override void PostClose()
        {
            ThreadPool.QueueUserWorkItem(s => net.Stop());
        }

        private void AddOrUpdate(IPEndPoint endpoint)
        {
            LanServer server = servers.Find(s => s.endpoint.Equals(endpoint));

            if (server == null)
            {
                servers.Add(new LanServer()
                {
                    endpoint = endpoint,
                    lastUpdate = Environment.TickCount
                });
            }
            else
            {
                server.lastUpdate = Environment.TickCount;
            }
        }

        class LanServer
        {
            public IPEndPoint endpoint;
            public int lastUpdate;
        }
    }

    public class SteamPersona
    {
        public CSteamID id;
        public string username;
        public int avatar;

        public bool playingRimworld;
        public CSteamID serverHost = CSteamID.Nil;
    }

}
