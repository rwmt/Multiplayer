using Multiplayer.Common;
using RimWorld;
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
using Verse.Profile;
using Verse.Steam;

namespace Multiplayer.Client
{
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
}
