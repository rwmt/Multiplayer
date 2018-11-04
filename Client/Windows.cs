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

    [HotSwappable]
    public abstract class BaseConnectingWindow : Window, IConnectionStatusListener
    {
        public override Vector2 InitialSize => new Vector2(350f, 150f);

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
            if (Widgets.ButtonText(buttonRect, "Cancel", true, false, true))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            Multiplayer.Client?.Close();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

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

    [HotSwappable]
    public class HostWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(450f, 300f);

        private SaveFile file;
        public bool returnToServerBrowser;

        public HostWindow(SaveFile file = null)
        {
            closeOnAccept = false;

            this.file = file;
            gameName = file?.gameName ?? string.Empty;

            var localAddr = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Loopback;
            ip = localAddr.ToString();
        }

        private string ip;
        private string gameName;
        private int maxPlayers = 8;
        private int autosaveInterval = 8;
        private bool lan = true;
        private bool steam;

        private string maxPlayersBuffer;
        private string autosaveBuffer;

        public override void DoWindowContents(Rect inRect)
        {
            var entry = new Rect(0, 10f, inRect.width, 30f);

            var labelWidth = 100f;

            gameName = TextEntryLabeled(entry, "Name:  ", gameName, labelWidth);
            entry = entry.Down(40);

            ip = TextEntryLabeled(entry, "Address:  ", ip, labelWidth);
            entry = entry.Down(40);

            TextFieldNumericLabeled(entry.Width(labelWidth + 50f), "Max players:  ", ref maxPlayers, ref maxPlayersBuffer, labelWidth);

            TextFieldNumericLabeled(entry.Right(200f).Width(labelWidth + 35f), "Autosave every ", ref autosaveInterval, ref autosaveBuffer, labelWidth + 5f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(entry.Right(200f).Right(labelWidth + 35f), " minutes");
            Text.Anchor = TextAnchor.UpperLeft;
            entry = entry.Down(40);

            Widgets.CheckboxLabeled(entry.Right(labelWidth - Text.CalcSize("LAN:  ").x).Width(120), "LAN:  ", ref lan, placeCheckboxNearText: true);
            entry = entry.Down(30);

            if (SteamManager.Initialized)
                Widgets.CheckboxLabeled(entry.Right(labelWidth - Text.CalcSize("Steam:  ").x).Width(120), "Steam:  ", ref steam, placeCheckboxNearText: true);

            var buttonRect = new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f);

            if (Widgets.ButtonText(buttonRect, "Host") && TryParseIp(ip, out IPAddress addr, out int port))
            {
                if (file != null)
                    HostFromSave(addr, port);
                else
                    HostServer(addr, port);

                Close(true);
            }
        }

        private bool TryParseIp(string ip, out IPAddress addr, out int port)
        {
            port = 0;
            string[] parts = ip.Split(':');

            if (!IPAddress.TryParse(parts[0], out addr))
            {
                Messages.Message("MpInvalidAddress", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (parts.Length >= 2 && !int.TryParse(parts[1], out port))
            {
                Messages.Message("MpInvalidPort", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }

        public static string TextEntryLabeled(Rect rect, string label, string text, float labelWidth)
        {
            Rect labelRect = rect.Rounded();
            labelRect.width = labelWidth;
            Rect fieldRect = rect;
            fieldRect.xMin += labelWidth;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(labelRect, label);
            Text.Anchor = anchor;
            return Widgets.TextField(fieldRect, text);
        }

        public static void TextFieldNumericLabeled(Rect rect, string label, ref int val, ref string buffer, float labelWidth)
        {
            Rect labelRect = rect;
            labelRect.width = labelWidth;
            Rect fieldRect = rect;
            fieldRect.xMin += labelWidth;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(labelRect, label);
            Text.Anchor = anchor;
            Widgets.TextFieldNumeric(fieldRect, ref val, ref buffer);
        }

        public override void PostClose()
        {
            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        private void HostFromSave(IPAddress addr, int port)
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.InitData.gameToLoad = file.name;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    LongEventHandler.QueueLongEvent(() => HostServer(addr, port), "MpLoading", false, null);
                });
            }, "Play", "LoadingLongEvent", true, null);
        }

        private static void HostServer(IPAddress addr, int port)
        {
            MpLog.Log("Starting a server");

            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();
            Faction dummyFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == -1);

            if (dummyFaction == null)
            {
                dummyFaction = new Faction() { loadID = -1, def = Multiplayer.DummyFactionDef };

                foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                    dummyFaction.TryMakeInitialRelationsWith(other);

                Find.FactionManager.Add(dummyFaction);
            }

            Faction.OfPlayer.Name = Multiplayer.username + "'s faction";

            comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();
            comp.factionData[dummyFaction.loadID] = FactionWorldData.New(dummyFaction.loadID);

            MultiplayerSession session = Multiplayer.session = new MultiplayerSession();
            MultiplayerServer localServer = new MultiplayerServer(addr, port);
            localServer.hostUsername = Multiplayer.username;
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

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;
            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

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

        private static void SetupLocalClient()
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
