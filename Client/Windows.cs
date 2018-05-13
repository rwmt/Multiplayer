using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
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

                builder.Append(Multiplayer.client != null ? "Connected" : "Not connected").Append("\n");

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

            if (Multiplayer.client == null)
                AddMsg(Multiplayer.username + ": " + currentMsg);
            else
                Multiplayer.client.Send(Packets.CLIENT_CHAT, currentMsg);

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
            this.closeOnEscapeKey = false;
        }

        protected override void DoBack()
        {
            Multiplayer.client.Close("");

            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = null;
            }, "Entry", "LoadingLongEvent", true, null);
        }
    }

    public class ConnectWindow : Window
    {
        private string ip = "127.0.0.1";

        public override Vector2 InitialSize => new Vector2(300f, 150f);

        public ConnectWindow()
        {
            this.doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(0, 15f, inRect.width, 35f), ip);

            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f), "Connect"))
            {
                int port = MultiplayerServer.DEFAULT_PORT;
                string[] ipport = ip.Split(':');
                if (ipport.Length == 2)
                    int.TryParse(ipport[1], out port);
                else
                    port = MultiplayerServer.DEFAULT_PORT;

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

            Client.TryConnect(address, port, conn =>
            {
                connecting = false;
                text = "Connected.";

                Multiplayer.chat = new ChatWindow();
                Multiplayer.client = conn;

                conn.Username = Multiplayer.username;
                conn.State = new ClientWorldState(conn);
            }, reason =>
            {
                // todo decouple from window
                connecting = false;
                text = "Couldn't connect to the server.\n" + reason;
                Multiplayer.client = null;
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
                if (Multiplayer.netClient != null)
                {
                    Multiplayer.netClient.Stop();
                    Multiplayer.netClient = null;
                }

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
                    MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();

                    Faction.OfPlayer.Name = Multiplayer.username + "'s faction";
                    comp.playerFactions[Multiplayer.username] = Faction.OfPlayer;

                    Multiplayer.localServer = new MultiplayerServer(ipAddr);
                    MultiplayerServer.instance = Multiplayer.localServer;

                    Multiplayer.localServer.highestUniqueId = Find.UniqueIDsManager.GetNextThingID();
                    Multiplayer.globalIdBlock = Multiplayer.localServer.NextIdBlock();

                    foreach (Settlement settlement in Find.WorldObjects.Settlements)
                        if (settlement.HasMap)
                        {
                            Multiplayer.localServer.mapTiles[settlement.Tile] = settlement.Map.uniqueID;
                            settlement.Map.GetComponent<MultiplayerMapComp>().mapIdBlock = Multiplayer.localServer.NextIdBlock();
                        }

                    LocalClientConnection localClient = new LocalClientConnection()
                    {
                        Username = Multiplayer.username
                    };

                    LocalServerConnection localServerConn = new LocalServerConnection()
                    {
                        Username = Multiplayer.username
                    };

                    localServerConn.client = localClient;
                    localClient.server = localServerConn;

                    localClient.State = new ClientPlayingState(localClient);
                    localServerConn.State = new ServerPlayingState(localServerConn);

                    Multiplayer.localServer.players.Add(new ServerPlayer(localServerConn));

                    Multiplayer.localServer.host = Multiplayer.username;
                    Multiplayer.client = localClient;

                    Find.MainTabsRoot.EscapeCurrentTab(false);

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        Multiplayer.SendGameData(Multiplayer.SaveAndReload());

                        Multiplayer.serverThread = new Thread(Multiplayer.localServer.Run)
                        {
                            Name = "Local server thread"
                        };
                        Multiplayer.serverThread.Start();

                        Multiplayer.chat = new ChatWindow();
                        MultiplayerServer.instance.UpdatePlayerList();

                        Messages.Message("Server started. Listening at " + ipAddr.ToString() + ":" + MultiplayerServer.DEFAULT_PORT, MessageTypeDefOf.SilentInput);
                    }, "Saving", false, null);
                }
                catch (SocketException)
                {
                    Messages.Message("Server creation failed.", MessageTypeDefOf.RejectInput);
                }

                this.Close(true);
            }
        }
    }

    public class Dialog_JumpTo : Dialog_Rename
    {
        private Action<string> action;

        public Dialog_JumpTo(Action<string> action)
        {
            this.action = action;
        }

        protected override void SetName(string name)
        {
            action(name);
        }
    }

}
