using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace ServerMod
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
            ServerMod.client.Close();

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
                int port = ServerMod.DEFAULT_PORT;
                string[] ipport = ip.Split(':');
                if (ipport.Length == 2)
                    int.TryParse(ipport[1], out port);
                else
                    port = ServerMod.DEFAULT_PORT;

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
                    ServerMod.client = null;
                    return;
                }

                lock (textlock)
                    text = "Connected to server.";

                ServerMod.client = conn;
                conn.username = ServerMod.username;
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
                ServerMod.client?.Close();
                ServerMod.client = null;
                Close();
            }

            if (ServerMod.savedWorld != null)
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
                    ServerMod.server = new Server(local, ServerMod.DEFAULT_PORT, (conn) =>
                    {
                        conn.SetState(new ServerWorldState(conn));
                    });

                    LocalServerConnection localServer = new LocalServerConnection() { username = ServerMod.username };
                    LocalClientConnection localClient = new LocalClientConnection() { username = ServerMod.username };

                    localServer.client = localClient;
                    localClient.server = localServer;

                    localClient.SetState(new ClientPlayingState(localClient));
                    localServer.SetState(new ServerPlayingState(localServer));

                    ServerMod.server.GetConnections().Add(localServer);
                    ServerMod.client = localClient;
                    ServerMod.localServerConnection = localServer;

                    if (ServerMod.highestUniqueId == -1)
                        ServerMod.highestUniqueId = Find.UniqueIDsManager.GetNextThingID();

                    ServerMod.mainBlock = ServerMod.NextIdBlock();
                    Faction.OfPlayer.Name = ServerMod.username + "'s faction";
                    Find.World.GetComponent<ServerModWorldComp>().playerFactions[ServerMod.username] = Faction.OfPlayer;

                    Messages.Message("Server started. Listening at " + local.ToString() + ":" + ServerMod.DEFAULT_PORT, MessageTypeDefOf.SilentInput);
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
        }

        private Vector2 scrollPos = Vector2.zero;

        public override void DoWindowContents(Rect inRect)
        {
            Rect rect = new Rect(0, 0, inRect.width, inRect.height - CloseButSize.y);

            IEnumerable<string> players;
            lock (ServerMod.server.GetConnections())
                players = ServerMod.server.GetConnections().Select(conn => conn.ToString());

            Widgets.LabelScrollable(rect, String.Format("Connected players ({0}):\n{1}", players.Count(), String.Join("\n", players.ToArray())), ref scrollPos);
        }
    }
}
