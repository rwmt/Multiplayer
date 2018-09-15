using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Verse.Steam;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public class ChatWindow : Window
    {
        public const int MaxChatMsgLength = 128;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);

        private static readonly Texture2D SelectedMsg = SolidColorMaterials.NewSolidColorTexture(new Color(0.17f, 0.17f, 0.17f, 0.85f));

        private Vector2 scrollPos;
        private float messagesHeight;
        private List<ChatMsg> messages = new List<ChatMsg>();
        private string currentMsg = "";
        private bool focused;

        public string[] playerList = new string[0];

        public ChatWindow()
        {
            absorbInputAroundWindow = false;
            draggable = true;
            soundClose = null;
            preventCameraMotion = false;
            focusWhenOpened = true;
            doCloseX = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return)
                {
                    SendMsg();
                    Event.current.Use();
                }
            }

            Rect chat = new Rect(inRect.x, inRect.y, inRect.width - 120f, inRect.height);
            DrawChat(chat);

            Rect info = new Rect(chat);
            info.x = chat.xMax + 10f;
            DrawInfo(info);
        }

        public void DrawInfo(Rect rect)
        {
            GUI.color = new Color(1, 1, 1, 0.8f);

            if (Event.current.type == EventType.repaint)
            {
                StringBuilder builder = new StringBuilder();

                builder.Append(Multiplayer.Client != null ? "Connected" : "Not connected").Append("\n");

                if (playerList.Length > 0)
                {
                    builder.Append("\nPlayers (").Append(playerList.Length).Append("):\n");
                    builder.Append(String.Join("\n", playerList));
                }

                Widgets.Label(rect, builder.ToString());
            }
        }

        public void DrawChat(Rect rect)
        {
            Rect outRect = new Rect(0f, 0f, rect.width, rect.height - 30f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, messagesHeight + 10f);
            float width = viewRect.width;
            Rect textField = new Rect(20f, outRect.yMax + 5f, width - 70f, 25f);

            GUI.BeginGroup(rect);

            GUI.SetNextControlName("chat_input");
            currentMsg = Widgets.TextField(textField, currentMsg);
            currentMsg = currentMsg.Substring(0, Math.Min(currentMsg.Length, MaxChatMsgLength));

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            float yPos = 0;
            GUI.color = Color.white;

            int i = 0;

            foreach (ChatMsg msg in messages)
            {
                float height = Text.CalcHeight(msg.msg, width);
                float textWidth = Text.CalcSize(msg.msg).x + 15;

                GUI.SetNextControlName("chat_msg_" + i++);

                Rect msgRect = new Rect(20f, yPos, width, height);
                if (Mouse.IsOver(msgRect))
                {
                    GUI.DrawTexture(msgRect, SelectedMsg);
                    TooltipHandler.TipRegion(msgRect, msg.timestamp.ToLongTimeString());
                }

                Color cursorColor = GUI.skin.settings.cursorColor;
                GUI.skin.settings.cursorColor = new Color(0, 0, 0, 0);

                msgRect.width = Math.Min(textWidth, msgRect.width);
                Widgets.TextArea(msgRect, msg.msg, true);

                GUI.skin.settings.cursorColor = cursorColor;

                yPos += height;
            }

            if (Event.current.type == EventType.layout)
                messagesHeight = yPos;

            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(textField.xMax + 5f, textField.y, 55f, textField.height), "Send"))
                SendMsg();

            GUI.EndGroup();

            if (Event.current.type == EventType.mouseDown && !GUI.GetNameOfFocusedControl().NullOrEmpty())
                UI.UnfocusCurrentControl();

            if (!focused)
            {
                GUI.FocusControl("chat_input");
                TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                editor.OnFocus();
                editor.MoveTextEnd();
                focused = true;
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            focused = false;
        }

        public void SendMsg()
        {
            currentMsg = currentMsg.Trim();

            if (currentMsg.NullOrEmpty()) return;

            if (Multiplayer.Client == null)
                AddMsg(Multiplayer.username + ": " + currentMsg);
            else
                Multiplayer.Client.Send(Packets.CLIENT_CHAT, currentMsg);

            currentMsg = "";
        }

        public void AddMsg(string msg)
        {
            messages.Add(new ChatMsg(msg, DateTime.Now));
            scrollPos.y = messagesHeight;
        }

        private class ChatMsg
        {
            public string msg;
            public DateTime timestamp;

            public ChatMsg(string msg, DateTime timestamp)
            {
                this.msg = msg;
                this.timestamp = timestamp;
            }
        }
    }

    public class PacketLogWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);

        public List<LogNode> nodes = new List<LogNode>();
        private int logHeight;
        private Vector2 scrollPos;

        public override void DoWindowContents(Rect rect)
        {
            GUI.BeginGroup(rect);

            Text.Font = GameFont.Tiny;
            Rect outRect = new Rect(0f, 0f, rect.width, rect.height - 30f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, logHeight + 10f);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            Rect nodeRect = new Rect(0f, 0f, viewRect.width, 20f);
            foreach (LogNode node in nodes)
                Draw(node, 0, ref nodeRect);

            if (Event.current.type == EventType.layout)
                logHeight = (int)nodeRect.y;

            Widgets.EndScrollView();

            GUI.EndGroup();
        }

        public void Draw(LogNode node, int depth, ref Rect rect)
        {
            string text = node.text;
            if (depth == 0)
                text = node.children[0].text;

            rect.x = depth * 15;
            if (node.children.Count > 0)
            {
                Widgets.Label(rect, node.expand ? "[-]" : "[+]");
                rect.x += 15;
            }

            rect.height = Text.CalcHeight(text, rect.width);
            Widgets.Label(rect, text);
            if (Widgets.ButtonInvisible(rect))
                node.expand = !node.expand;
            rect.y += (int)rect.height;

            if (node.expand)
                foreach (LogNode child in node.children)
                    Draw(child, depth + 1, ref rect);
        }
    }

    public class CustomSelectLandingSite : Page_SelectLandingSite
    {
        public CustomSelectLandingSite()
        {
            this.forcePause = false;
            this.closeOnCancel = false;
        }

        public override void DoBack()
        {
            Multiplayer.Client.Close("");

            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = null;
            }, "Entry", "LoadingLongEvent", true, null);
        }
    }

    public class ConnectingWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private IPAddress address;
        private int port;

        public string text;
        private bool connecting;

        public ConnectingWindow(IPAddress address, int port)
        {
            this.address = address;
            this.port = port;

            connecting = true;

            ClientUtil.TryConnect(address, port, conn =>
            {
                connecting = false;
                text = "Connected.";
            }, reason =>
            {
                connecting = false;
                text = "Couldn't connect to the server.\n" + reason;
            });
        }

        public override void DoWindowContents(Rect inRect)
        {
            string label = text;
            if (connecting)
                label = "Connecting to " + address.ToString() + ":" + port + GenText.MarchingEllipsis();
            Widgets.Label(inRect, label);

            Text.Font = GameFont.Small;
            Rect rect2 = new Rect(inRect.width / 2f - CloseButSize.x / 2f, inRect.height - 55f, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(rect2, "Cancel", true, false, true))
            {
                OnMainThread.StopMultiplayer();
                Close();
            }
        }
    }

    public class HostWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private string ip = "127.0.0.1";

        public override void DoWindowContents(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(0, 15f, inRect.width, 35f), ip);

            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f), "Host") && IPAddress.TryParse(ip, out IPAddress ipAddr))
            {
                try
                {
                    HostServer(ipAddr);
                }
                catch (SocketException)
                {
                    Messages.Message("Server creation failed.", MessageTypeDefOf.RejectInput);
                }

                Close(true);
            }
        }

        private void HostServer(IPAddress addr)
        {
            MpLog.Log("Starting a server");

            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();
            Faction dummyFaction = new Faction() { loadID = -1, def = Multiplayer.dummyFactionDef };

            foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                dummyFaction.TryMakeInitialRelationsWith(other);

            Faction.OfPlayer.Name = Multiplayer.username + "'s faction";

            comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();
            comp.factionData[dummyFaction.loadID] = FactionWorldData.New(dummyFaction.loadID);

            Find.FactionManager.Add(dummyFaction);

            MultiplayerSession session = Multiplayer.session = new MultiplayerSession();
            MultiplayerServer localServer = new MultiplayerServer(addr);
            localServer.lan = true;
            MultiplayerServer.instance = localServer;
            session.localServer = localServer;
            session.myFactionId = Faction.OfPlayer.loadID;

            Multiplayer.game = new MultiplayerGame
            {
                dummyFaction = dummyFaction,
                worldComp = comp
            };

            localServer.nextUniqueId = GetMaxUniqueId();
            comp.globalIdBlock = localServer.NextIdBlock();

            foreach (FactionWorldData data in comp.factionData.Values)
            {
                foreach (DrugPolicy p in data.drugPolicyDatabase.policies)
                    p.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (Outfit o in data.outfitDatabase.outfits)
                    o.uniqueId = Multiplayer.GlobalIdBlock.NextId();
            }

            foreach (Map map in Find.Maps)
            {
                MultiplayerMapComp mapComp = map.GetComponent<MultiplayerMapComp>();
                mapComp.mapIdBlock = localServer.NextIdBlock();

                mapComp.factionMapData[map.ParentFaction.loadID] = FactionMapData.FromMap(map);

                mapComp.factionMapData[dummyFaction.loadID] = FactionMapData.New(dummyFaction.loadID, map);
                mapComp.factionMapData[dummyFaction.loadID].areaManager.AddStartingAreas();
            }

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;

            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
                if (settlement.HasMap)
                    localServer.mapTiles[settlement.Tile] = settlement.Map.uniqueID;

            SetupLocalClient();

            Find.MainTabsRoot.EscapeCurrentTab(false);
            session.chat = new ChatWindow();

            LongEventHandler.QueueLongEvent(() =>
            {
                Multiplayer.CacheAndSendGameData(Multiplayer.SaveAndReload());

                localServer.StartListening();

                session.serverThread = new Thread(localServer.Run)
                {
                    Name = "Local server thread"
                };
                session.serverThread.Start();

                MultiplayerServer.instance.UpdatePlayerList();

                Messages.Message("Server started. Listening at " + addr.ToString() + ":" + MultiplayerServer.DefaultPort, MessageTypeDefOf.SilentInput, false);
            }, "Saving", false, null);
        }

        private void SetupLocalClient()
        {
            LocalClientConnection localClient = new LocalClientConnection(Multiplayer.username);
            LocalServerConnection localServerConn = new LocalServerConnection(Multiplayer.username);

            localServerConn.client = localClient;
            localClient.server = localServerConn;

            localClient.State = new ClientPlayingState(localClient);
            localServerConn.State = new ServerPlayingState(localServerConn);

            Multiplayer.LocalServer.players.Add(new ServerPlayer(localServerConn));
            Multiplayer.LocalServer.host = Multiplayer.username;

            Multiplayer.session.client = localClient;
        }

        private static int GetMaxUniqueId()
        {
            return typeof(UniqueIDsManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int))
                .Select(f => (int)f.GetValue(Find.UniqueIDsManager))
                .Max();
        }
    }

    public static class SteamImages
    {
        public static Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();

        // Remember to flip it
        public static Texture2D GetTexture(int id)
        {
            if (cache.TryGetValue(id, out Texture2D tex))
                return tex;

            if (!SteamUtils.GetImageSize(id, out uint width, out uint height))
            {
                cache[id] = null;
                return null;
            }

            uint sizeInBytes = width * height * 4;
            byte[] data = new byte[sizeInBytes];

            if (!SteamUtils.GetImageRGBA(id, data, (int)sizeInBytes))
            {
                cache[id] = null;
                return null;
            }

            tex = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(data);
            tex.Apply();

            cache[id] = tex;

            return tex;
        }
    }

    public class ServerBrowser : Window
    {
        private NetManager net;
        private List<LanServer> servers = new List<LanServer>();

        public override Vector2 InitialSize => new Vector2(600f, 400f);

        public ServerBrowser()
        {
            Log.Message("Server browser created");

            EventBasedNetListener listener = new EventBasedNetListener();
            listener.NetworkReceiveUnconnectedEvent += (endpoint, data, type) =>
            {
                if (type != UnconnectedMessageType.DiscoveryRequest) return;

                string s = Encoding.UTF8.GetString(data.GetRemainingBytes());
                if (s == "mp-server")
                    AddOrUpdate(endpoint);
            };

            net = new NetManager(listener, 0, "");
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
            Lan, Direct, Steam, Host
        }

        public override void DoWindowContents(Rect inRect)
        {
            List<TabRecord> tabs = new List<TabRecord>()
            {
                new TabRecord("LAN", ()=> tab = Tabs.Lan,  tab == Tabs.Lan),
                new TabRecord("Direct", ()=> tab = Tabs.Direct, tab == Tabs.Direct),
                new TabRecord("Steam", ()=> tab = Tabs.Steam, tab == Tabs.Steam),
                new TabRecord("Host", ()=> tab = Tabs.Host, tab == Tabs.Host),
            };

            inRect.yMin += 35f;
            TabDrawer.DrawTabs(inRect, tabs);

            GUI.BeginGroup(new Rect(0, inRect.yMin, inRect.width, inRect.height));

            Rect groupRect = new Rect(0, 0, inRect.width, inRect.height);
            if (tab == Tabs.Lan)
                DrawLan(groupRect);
            else if (tab == Tabs.Direct)
                DrawDirect(groupRect);
            else if (tab == Tabs.Steam)
                DrawSteam(groupRect);

            GUI.EndGroup();
        }

        private List<SteamFriend> friends = new List<SteamFriend>();
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
                Widgets.Label(new Rect(0, 0, inRect.width, 40f), info);

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

            foreach (SteamFriend friend in friends)
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

                if (friend.onServer)
                {
                    Rect playButton = new Rect(entryRect.xMax - 85, entryRect.y + 5, 80, 40 - 10);
                    if (Widgets.ButtonText(playButton, "Join"))
                    {
                        Close(false);
                        // connect
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

        class SteamFriend
        {
            public CSteamID id;
            public string username;
            public int avatar;

            public bool playingRimworld;
            public bool onServer;
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
                    Messages.Message("Invalid IP address.", MessageTypeDefOf.RejectInput);
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
            Text.Anchor = TextAnchor.MiddleLeft;
            float textWidth = Text.CalcSize("Searching").x;
            Widgets.Label(new Rect((inRect.width - textWidth) / 2, 8f, textWidth + 20, 40), "Searching" + GenText.MarchingEllipsis());
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
                    Find.WindowStack.Add(new ConnectingWindow(IPAddress.Parse(server.endpoint.Host), server.endpoint.Port));
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
                    //if (!playingRimworld) continue;

                    int avatar = SteamFriends.GetSmallFriendAvatar(friend);
                    string username = SteamFriends.GetFriendPersonaName(friend);
                    string connectValue = SteamFriends.GetFriendRichPresence(friend, "connect");

                    friends.Add(new SteamFriend()
                    {
                        id = friend,
                        avatar = avatar,
                        username = username,
                        playingRimworld = playingRimworld,
                        onServer = connectValue != null && connectValue.Contains("-mpserver=")
                    });
                }

                friends.SortBy(f => f.onServer);

                lastFriendUpdate = Environment.TickCount;
            }
        }

        public override void PostClose()
        {
            net.Stop();
            Log.Message("Server browser closed");
        }

        private void AddOrUpdate(NetEndPoint endpoint)
        {
            LanServer server = servers.FirstOrDefault(s => s.endpoint.Equals(endpoint));

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
            public NetEndPoint endpoint;
            public int lastUpdate;
        }
    }

    public class Dialog_JumpTo : Dialog_Rename
    {
        private Action<string> action;

        public Dialog_JumpTo(Action<string> action)
        {
            this.action = action;
        }

        public override void SetName(string name)
        {
            action(name);
        }
    }

}
