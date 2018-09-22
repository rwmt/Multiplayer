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

        private Vector2 chatScroll;
        private Vector2 playerListScroll;
        private Vector2 steamScroll;
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
            resizeable = true;
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

            const float infoWidth = 140f;
            Rect chat = new Rect(inRect.x, inRect.y, inRect.width - infoWidth - 10f, inRect.height);
            DrawChat(chat);

            GUI.BeginGroup(new Rect(chat.xMax + 10f, chat.y, infoWidth, inRect.height));
            DrawInfo(new Rect(0, 0, infoWidth, inRect.height));
            GUI.EndGroup();
        }

        private void DrawInfo(Rect inRect)
        {
            DrawOptions(ref inRect);

            Widgets.Label(inRect, Multiplayer.Client != null ? "Connected" : "Not connected");
            inRect.yMin += 30f;

            DrawList($"Players ({playerList.Length}):", playerList, ref inRect, ref playerListScroll, index =>
            {
                if (Multiplayer.LocalServer != null && Event.current.button == 1)
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>()
                    {
                        new FloatMenuOption("Kick", () =>
                        {
                            // todo
                        })
                    }));
            });

            inRect.yMin += 10f;

            List<string> names = Multiplayer.session.pendingSteam.Select(SteamFriends.GetFriendPersonaName).ToList();
            DrawList("Accept Steam players", names, ref inRect, ref steamScroll, AcceptSteamPlayer, true);
        }

        private void DrawOptions(ref Rect inRect)
        {
            if (Multiplayer.LocalServer == null) return;

            const float height = 20f;

            if (SteamManager.Initialized)
            {
                // todo sync this between players
                string label = "Steam";
                Rect optionsRect = new Rect(inRect.width, inRect.yMax - height, 0, height);
                optionsRect.xMin -= Text.CalcSize(label).x + 24f + 10f;
                Widgets.CheckboxLabeled(optionsRect, label, ref Multiplayer.session.allowSteam);
                inRect.yMax -= 20;
            }

            {
                string label = "LAN";
                Rect optionsRect = new Rect(inRect.width, inRect.yMax - height, 0, height);
                optionsRect.xMin -= Text.CalcSize(label).x + 24f + 10f;
                Widgets.CheckboxLabeled(optionsRect, label, ref Multiplayer.LocalServer.allowLan);
                inRect.yMax -= 20;
            }
        }

        private void AcceptSteamPlayer(int index)
        {
            CSteamID remoteId = Multiplayer.session.pendingSteam[index];

            IConnection conn = new SteamConnection(remoteId);
            conn.State = new ServerSteamState(conn);
            Multiplayer.LocalServer.OnConnected(conn);

            SteamNetworking.AcceptP2PSessionWithUser(remoteId);
            Multiplayer.session.pendingSteam.RemoveAt(index);
        }

        private void DrawList(string label, IList<string> entries, ref Rect inRect, ref Vector2 scroll, Action<int> click = null, bool hideNullOrEmpty = false)
        {
            if (hideNullOrEmpty && !entries.Any(s => !s.NullOrEmpty())) return;

            Widgets.Label(inRect, label);
            inRect.yMin += 20f;

            float entryHeight = Text.LineHeight;
            float height = entries.Count() * entryHeight;

            Rect outRect = new Rect(0, inRect.yMin, inRect.width, Math.Min(height, Math.Min(230, inRect.height)));
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, height);
            if (viewRect.height <= outRect.height)
                viewRect.width += 16f;

            Widgets.BeginScrollView(outRect, ref scroll, viewRect, true);
            GUI.color = new Color(1, 1, 1, 0.8f);

            float y = height - entryHeight;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                string entry = entries[i];
                if (hideNullOrEmpty && entry.NullOrEmpty()) continue;

                Rect entryRect = new Rect(0, y, viewRect.width, entryHeight);
                if (i % 2 == 0)
                    Widgets.DrawAltRect(entryRect);

                if (Mouse.IsOver(entryRect))
                {
                    GUI.DrawTexture(entryRect, SelectedMsg);
                    if (click != null && Event.current.type == EventType.mouseDown)
                    {
                        click(i);
                        Event.current.Use();
                    }
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect, entry);
                Text.Anchor = TextAnchor.UpperLeft;

                y -= entryHeight;
            }

            GUI.color = Color.white;
            Widgets.EndScrollView();

            inRect.yMin += outRect.height;
        }

        private void DrawChat(Rect inRect)
        {
            Rect outRect = new Rect(0f, 0f, inRect.width, inRect.height - 30f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, messagesHeight + 10f);
            float width = viewRect.width;
            Rect textField = new Rect(20f, outRect.yMax + 5f, width - 70f, 25f);

            GUI.BeginGroup(inRect);

            GUI.SetNextControlName("chat_input");
            currentMsg = Widgets.TextField(textField, currentMsg);
            currentMsg = currentMsg.Substring(0, Math.Min(currentMsg.Length, MaxChatMsgLength));

            Widgets.BeginScrollView(outRect, ref chatScroll, viewRect);

            float yPos = 0;
            GUI.color = Color.white;

            int i = 0;

            foreach (ChatMsg msg in messages)
            {
                float height = Text.CalcHeight(msg.Msg, width);
                float textWidth = Text.CalcSize(msg.Msg).x + 15;

                GUI.SetNextControlName("chat_msg_" + i++);

                Rect msgRect = new Rect(20f, yPos, width, height);
                if (Mouse.IsOver(msgRect))
                {
                    GUI.DrawTexture(msgRect, SelectedMsg);

                    if (msg.TimeStamp != null)
                        TooltipHandler.TipRegion(msgRect, msg.TimeStamp.ToLongTimeString());

                    if (msg.Clickable && Event.current.type == EventType.MouseUp)
                        msg.Click();
                }

                Color cursorColor = GUI.skin.settings.cursorColor;
                GUI.skin.settings.cursorColor = new Color(0, 0, 0, 0);

                msgRect.width = Math.Min(textWidth, msgRect.width);
                Widgets.TextArea(msgRect, msg.Msg, true);

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
            AddMsg(new ChatMsg_Text(msg, DateTime.Now));
        }

        public void AddMsg(ChatMsg msg)
        {
            messages.Add(msg);
            chatScroll.y = messagesHeight;
        }
    }

    public abstract class ChatMsg
    {
        public virtual bool Clickable => false;
        public abstract string Msg { get; }
        public abstract DateTime TimeStamp { get; }

        public virtual void Click() { }
    }

    public class ChatMsg_Text : ChatMsg
    {
        private string msg;
        private DateTime timestamp;

        public override string Msg => msg;
        public override DateTime TimeStamp => timestamp;

        public ChatMsg_Text(string msg, DateTime timestamp)
        {
            this.msg = msg;
            this.timestamp = timestamp;
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
            Multiplayer.Client.Close();

            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = null;
            }, "Entry", "LoadingLongEvent", true, null);
        }
    }

    public abstract class BaseConnectingWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        public virtual bool Ellipsis => result == null;
        public abstract string ConnectingString { get; }

        public string result;

        public override void DoWindowContents(Rect inRect)
        {
            string label = Ellipsis ? ConnectingString : result;

            Rect textRect = inRect;
            float textWidth = Text.CalcSize(label).x;
            textRect.x = (inRect.width - textWidth) / 2 - 5f;
            Widgets.Label(textRect, label);

            if (Ellipsis)
                Widgets.Label(textRect.Right(textWidth), GenText.MarchingEllipsis());

            Text.Font = GameFont.Small;
            Rect buttonRect = new Rect((inRect.width - 120f) / 2f, inRect.height - 55f, 120f, 40f);
            if (Widgets.ButtonText(buttonRect, "Cancel", true, false, true))
                Close();
        }

        public override void PostClose() => Multiplayer.Client?.Close();
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

            ClientUtil.TryConnect(address, port, conn =>
            {
                result = "Connected.";
            }, reason =>
            {
                if (result == null)
                    result = $"Couldn't connect to the server.\n{reason}";
            });
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
                    Messages.Message("Server creation failed.", MessageTypeDefOf.RejectInput, false);
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
            localServer.allowLan = true;
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
                        conn.Username = Multiplayer.username;
                        Multiplayer.session = new MultiplayerSession();
                        Multiplayer.session.client = conn;
                        conn.State = new ClientSteamState(conn);
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
            net.Stop();
            Log.Message("Server browser closed");
        }

        private void AddOrUpdate(NetEndPoint endpoint)
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
            public NetEndPoint endpoint;
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
