using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace Multiplayer
{
    public class CustomSelectLandingSite : Page_SelectLandingSite
    {
        public CustomSelectLandingSite()
        {
            this.forcePause = false;
            this.closeOnEscapeKey = false;
        }

        protected override void DoBack()
        {
            Multiplayer.client.Close();

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

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(300f, 150f);
            }
        }

        public ConnectWindow()
        {
            this.doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(0, 15f, inRect.width, 35f), ip);

            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f), "Connect"))
            {
                int port = Multiplayer.DEFAULT_PORT;
                string[] ipport = ip.Split(':');
                if (ipport.Length == 2)
                    int.TryParse(ipport[1], out port);
                else
                    port = Multiplayer.DEFAULT_PORT;

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
        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(300f, 150f);
            }
        }

        private IPAddress address;
        private int port;
        private string text;
        private object textlock = new object();

        public ConnectingWindow(IPAddress address, int port)
        {
            this.address = address;
            this.port = port;
            this.text = "Connecting to " + address.ToString() + ":" + port;

            Client.TryConnect(address, port, (conn, e) =>
            {
                if (e != null)
                {
                    lock (textlock)
                        text = "Error: Connection failed.";
                    Multiplayer.client = null;
                    return;
                }

                lock (textlock)
                    text = "Connected to server.";

                Multiplayer.client = conn;
                conn.username = Multiplayer.username;
                conn.SetState(new ClientWorldState(conn));
            });
        }

        public override void DoWindowContents(Rect inRect)
        {
            lock (textlock)
                Widgets.Label(inRect, text);

            Text.Font = GameFont.Small;
            Rect rect2 = new Rect(inRect.width / 2f - this.CloseButSize.x / 2f, inRect.height - 55f, this.CloseButSize.x, this.CloseButSize.y);
            if (Widgets.ButtonText(rect2, "Cancel", true, false, true))
            {
                Multiplayer.client?.Close();
                Multiplayer.client = null;
                Close();
            }

            if (Multiplayer.savedWorld != null)
                Close(false);
        }
    }

    public class HostWindow : Window
    {
        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(300f, 150f);
            }
        }

        private string ip = "127.0.0.1";

        public override void DoWindowContents(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(0, 15f, inRect.width, 35f), ip);

            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f), "Host") && IPAddress.TryParse(ip, out IPAddress local))
            {
                try
                {
                    MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();
                    comp.sessionId = new System.Random().Next();

                    Multiplayer.server = new Server(local, Multiplayer.DEFAULT_PORT, (conn) =>
                    {
                        conn.SetState(new ServerWorldState(conn));

                        conn.connectionClosed += () => OnMainThread.Enqueue(() => Messages.Message(conn.username + " disconnected", MessageTypeDefOf.SilentInput));
                    });

                    LocalServerConnection localServer = new LocalServerConnection() { username = Multiplayer.username };
                    LocalClientConnection localClient = new LocalClientConnection() { username = Multiplayer.username };

                    localServer.client = localClient;
                    localClient.server = localServer;

                    localClient.SetState(new ClientPlayingState(localClient));
                    localServer.SetState(new ServerPlayingState(localServer));

                    Multiplayer.server.GetConnections().Add(localServer);
                    Multiplayer.client = localClient;
                    Multiplayer.localServerConnection = localServer;

                    if (Multiplayer.highestUniqueId == -1)
                        Multiplayer.highestUniqueId = Find.UniqueIDsManager.GetNextThingID();

                    Multiplayer.mainBlock = Multiplayer.NextIdBlock();
                    Faction.OfPlayer.Name = Multiplayer.username + "'s faction";
                    comp.playerFactions[Multiplayer.username] = Faction.OfPlayer;

                    Messages.Message("Server started. Listening at " + local.ToString() + ":" + Multiplayer.DEFAULT_PORT, MessageTypeDefOf.SilentInput);
                }
                catch (SocketException)
                {
                    Messages.Message("Server creation failed.", MessageTypeDefOf.RejectInput);
                }

                this.Close(true);
            }
        }
    }

    public class ServerInfoWindow : Window
    {
        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(400f, 300f);
            }
        }

        public ServerInfoWindow()
        {
            this.doCloseButton = true;

            Thread.Sleep(2000);
        }

        private Vector2 scrollPos = Vector2.zero;

        public override void DoWindowContents(Rect inRect)
        {
            Rect rect = new Rect(0, 0, inRect.width, inRect.height - CloseButSize.y);

            IEnumerable<string> players;
            lock (Multiplayer.server.GetConnections())
                players = Multiplayer.server.GetConnections().Select(conn => conn.ToString());

            Widgets.LabelScrollable(rect, String.Format("Connected players ({0}):\n{1}", players.Count(), String.Join("\n", players.ToArray())), ref scrollPos);
        }
    }

    public class Dialog_JumpTo : Dialog_Rename
    {
        private Action<int> action;

        public Dialog_JumpTo(Action<int> action)
        {
            this.action = action;
        }

        protected override void SetName(string name)
        {
            if (int.TryParse(name, out int tile))
            {
                action(tile);
            }
        }
    }

}
