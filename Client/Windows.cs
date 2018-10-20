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
    public class PacketLogWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);

        public List<LogNode> nodes = new List<LogNode>();
        private int logHeight;
        private Vector2 scrollPos;

        public PacketLogWindow()
        {
            doCloseX = true;
        }

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

    public class CustomSelectLandingSite : Page_SelectStartingSite
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

    public abstract class BaseConnectingWindow : Window, IConnectionStatusListener
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        public virtual bool Ellipsis => result == null;
        public abstract string ConnectingString { get; }

        public string result;

        public override void DoWindowContents(Rect inRect)
        {
            string label = Ellipsis ? (ConnectingString + Multiplayer.FixedEllipsis()) : result;

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
                Close();
        }

        public override void PostClose() => Multiplayer.Client?.Close();

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
            localServer.coopFactionId = Faction.OfPlayer.loadID;
            MultiplayerServer.instance = localServer;
            session.localServer = localServer;
            session.myFactionId = Faction.OfPlayer.loadID;

            Multiplayer.game = new MultiplayerGame
            {
                dummyFaction = dummyFaction,
                worldComp = comp
            };

            localServer.nextUniqueId = GetMaxUniqueId();
            comp.globalIdBlock = localServer.NextIdBlock(1_000_000_000);

            foreach (FactionWorldData data in comp.factionData.Values)
            {
                foreach (DrugPolicy p in data.drugPolicyDatabase.policies)
                    p.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (Outfit o in data.outfitDatabase.outfits)
                    o.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (FoodRestriction o in data.foodRestrictionDatabase.foodRestrictions)
                    o.id = Multiplayer.GlobalIdBlock.NextId();
            }

            foreach (Map map in Find.Maps)
            {
                //mapComp.mapIdBlock = localServer.NextIdBlock();

                BeforeMapGeneration.SetupMap(map);

                MapAsyncTimeComp async = map.AsyncTime();
                async.mapTicks = Find.TickManager.TicksGame;
                async.TimeSpeed = Find.TickManager.CurTimeSpeed;
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
            }, "MpSaving", false, null);
        }

        private void SetupLocalClient()
        {
            LocalClientConnection localClient = new LocalClientConnection(Multiplayer.username);
            LocalServerConnection localServerConn = new LocalServerConnection(Multiplayer.username);

            localServerConn.client = localClient;
            localClient.server = localServerConn;

            localClient.State = ConnectionStateEnum.ClientPlaying;
            localServerConn.State = ConnectionStateEnum.ServerPlaying;

            ServerPlayer serverPlayer = new ServerPlayer(localServerConn);
            localServerConn.serverPlayer = serverPlayer;

            Multiplayer.LocalServer.players.Add(serverPlayer);
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
