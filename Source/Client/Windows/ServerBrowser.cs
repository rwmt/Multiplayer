extern alias zip;

using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Steam;
using HarmonyLib;
using zip::Ionic.Zip;
using System.Collections.Concurrent;
using Verse.Sound;
using Multiplayer.Client.Networking;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class ServerBrowser : Window
    {
        private NetManager lanListener;
        private List<LanServer> servers = new List<LanServer>();

        public override Vector2 InitialSize => new Vector2(800f, 500f);

        public ServerBrowser()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            listener.NetworkReceiveUnconnectedEvent += (endpoint, data, type) =>
            {
                if (type != UnconnectedMessageType.Broadcast) return;

                string s = Encoding.UTF8.GetString(data.GetRemainingBytes());
                if (s == "mp-server")
                    AddOrUpdate(endpoint);
            };

            lanListener = new NetManager(listener)
            {
                BroadcastReceiveEnabled = true,
                ReuseAddress = true,
                IPv6Enabled = IPv6Mode.Disabled
            };

            lanListener.Start(5100);

            doCloseX = true;
            closeOnAccept = false;
        }

        private Vector2 lanScroll;
        private Vector2 steamScroll;
        private Vector2 hostScroll;
        private static Tabs tab;

        enum Tabs
        {
            Lan, Direct, Steam, Host
        }

        public override void DoWindowContents(Rect inRect)
        {
            List<TabRecord> tabs = new List<TabRecord>()
            {
                new TabRecord("MpLan".Translate(), () => tab = Tabs.Lan,  tab == Tabs.Lan),
                new TabRecord("MpDirect".Translate(), () => tab = Tabs.Direct, tab == Tabs.Direct),
                new TabRecord("MpSteam".Translate(), () => tab = Tabs.Steam, tab == Tabs.Steam),
                new TabRecord("MpHostTab".Translate(), () => tab = Tabs.Host, tab == Tabs.Host),
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
                else if (tab == Tabs.Host)
                    DrawHost(groupRect);
            }
            GUI.EndGroup();
        }

        private bool filesRead;
        private SaveFileReader reader;

        private void ReloadFiles()
        {
            selectedFile = null;
            reader?.WaitTasks();

            reader = new SaveFileReader();
            reader.StartReading();
        }

        private bool mpCollapsed, spCollapsed;
        private float hostHeight;

        private FileInfo selectedFile;
        private float fileButtonsWidth;

        private void DrawHost(Rect inRect)
        {
            if (!filesRead)
            {
                ReloadFiles();
                filesRead = true;
            }

            inRect.y += 8;

            float margin = 80;
            Rect outRect = new Rect(margin, inRect.yMin + 10, inRect.width - 2 * margin, inRect.height - 80);
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, hostHeight);

            Widgets.BeginScrollView(outRect, ref hostScroll, viewRect, true);

            Rect collapseRect = new Rect(0, 4f, 18f, 18f);
            if (Widgets.ButtonImage(collapseRect, mpCollapsed ? TexButton.Reveal : TexButton.Collapse))
                mpCollapsed = !mpCollapsed;

            float y = 0;
            Text.Font = GameFont.Medium;
            float textHeight1 = Text.CalcHeight("MpMultiplayer".Translate(), inRect.width);
            Widgets.Label(viewRect.Right(18f), "MpMultiplayer".Translate());
            Text.Font = GameFont.Small;
            y += textHeight1 + 10;

            if (!mpCollapsed)
            {
                DrawSaveList(reader.MpSaves, viewRect.width, ref y);
                y += 25;
            }

            collapseRect.y += y;

            if (Widgets.ButtonImage(collapseRect, spCollapsed ? TexButton.Reveal : TexButton.Collapse))
                spCollapsed = !spCollapsed;

            viewRect.y = y;
            Text.Font = GameFont.Medium;
            float textHeight2 = Text.CalcHeight("MpSingleplayer".Translate(), inRect.width);
            Widgets.Label(viewRect.Right(18), "MpSingleplayer".Translate());
            Text.Font = GameFont.Small;
            y += textHeight2 + 10;

            if (!spCollapsed)
                DrawSaveList(reader.SpSaves, viewRect.width, ref y);

            if (Event.current.type == EventType.Layout)
                hostHeight = y;

            Widgets.EndScrollView();

            if (selectedFile == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;

                bool noSaves = reader.SpSaves.Count == 0 && reader.MpSaves.Count == 0;
                Widgets.Label(new Rect(outRect.x, outRect.yMax, outRect.width, 80), noSaves ? "MpNoSaves".Translate() : "MpNothingSelected".Translate());

                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                float width = 0;

                GUI.BeginGroup(new Rect(outRect.x + (outRect.width - fileButtonsWidth) / 2, outRect.yMax + 20, fileButtonsWidth, 40));
                DrawFileButtons(reader.GetData(selectedFile), ref width);
                GUI.EndGroup();

                if (Event.current.type == EventType.Layout)
                {
                    fileButtonsWidth = width;
                }
            }
        }

        private void DrawFileButtons(SaveFile file, ref float width)
        {
            if (file.Valid)
            {
                if (file.replay)
                {
                    if (Widgets.ButtonText(new Rect(width, 0, 120, 40), "MpWatchReplay".Translate()))
                    {
                        CheckGameVersionAndMods(
                            file,
                            () => { Close(false); Replay.LoadReplay(file.file); }
                        );
                    }

                    width += 120 + 10;
                }

                if (Widgets.ButtonText(new Rect(width, 0, 120, 40), "MpHostButton".Translate()))
                {
                    CheckGameVersionAndMods(
                        file,
                        () => { Close(false); Find.WindowStack.Add(new HostWindow(file) { returnToServerBrowser = true }); }
                    );
                }

                width += 120 + 10;
            }

            if (Widgets.ButtonText(new Rect(width, 0, 120, 40), "Delete".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmDelete".Translate(file.displayName), () =>
                {
                    file.file.Delete();
                    reader.RemoveFile(file.file);
                    selectedFile = null;
                }, true));
            }

            width += 120;
        }
        
        private static void CheckGameVersionAndMods(SaveFile file, Action action)
        {
            ScribeMetaHeaderUtility.lastMode = ScribeMetaHeaderUtility.ScribeHeaderMode.Map;
            ScribeMetaHeaderUtility.loadedGameVersion = file.rwVersion;
            ScribeMetaHeaderUtility.loadedModIdsList = file.modIds.ToList();
            ScribeMetaHeaderUtility.loadedModNamesList = file.modNames.ToList();

            if (!ScribeMetaHeaderUtility.TryCreateDialogsForVersionMismatchWarnings(action))
            {
                action();
            }
            else
            {
                Find.WindowStack.Windows
                    .OfType<Dialog_MessageBox>()
                    .Where(w => w.buttonAText == "LoadAnyway".Translate())
                    .Do(w => w.buttonAText = "Continue anyway");
            }
        }

        private void DrawSaveList(List<FileInfo> saves, float width, ref float y)
        {
            for (int i = 0; i < saves.Count; i++)
            {
                var file = saves[i];
                var data = reader.GetData(file);
                Rect entryRect = new Rect(0, y, width, 40);

                if (file == selectedFile)
                {
                    Widgets.DrawRectFast(entryRect, new Color(1f, 1f, 0.7f, 0.1f));

                    var lineColor = new Color(1, 1, 1, 0.3f);
                    Widgets.DrawLine(entryRect.min, entryRect.TopRightCorner(), lineColor, 2f);
                    Widgets.DrawLine(entryRect.min - new Vector2(-1, -5), entryRect.BottomLeftCorner() - new Vector2(-1, -2), lineColor, 2f);
                    Widgets.DrawLine(entryRect.BottomLeftCorner(), entryRect.max, lineColor, 2f);
                    Widgets.DrawLine(entryRect.TopRightCorner() - new Vector2(1, -5), entryRect.max - new Vector2(1, -2), lineColor, 2f);
                }
                else if (i % 2 == 0)
                {
                    Widgets.DrawAltRect(entryRect);
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect.Right(10), data?.displayName ?? "Loading...");
                Text.Anchor = TextAnchor.UpperLeft;

                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;

                var infoText = new Rect(entryRect.xMax - 120, entryRect.yMin + 3, 120, entryRect.height);
                Widgets.Label(infoText, file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));

                if (data != null)
                {
                    if (data.gameName != null)
                    {
                        Widgets.Label(infoText.Down(16), data.gameName.Truncate(110));
                    }
                    else
                    {
                        GUI.color = data.VersionColor;
                        Widgets.Label(infoText.Down(16), (data.rwVersion ?? "???").Truncate(110));
                    }

                    if (!data.Valid)
                    {
                        var rect = new Rect(infoText.x - 80, infoText.y + 8f, 80, 24f);
                        GUI.color = Color.red;

                        Widgets.Label(rect, $"({"EItemUpdateStatus_k_EItemUpdateStatusInvalid".Translate()})");
                        TooltipHandler.TipRegion(rect, new TipSignal("SaveIsUnknownFormat".Translate()));
                    }
                    else if (data.replay && data.protocol != MpVersion.Protocol)
                    {
                        bool autosave = data.replaySections > 1;

                        GUI.color = autosave ? new Color(0.8f, 0.8f, 0, 0.6f) : new Color(0.8f, 0.8f, 0);
                        var outdated = new Rect(infoText.x - 80, infoText.y + 8f, 80, 24f);
                        Widgets.Label(outdated, "MpReplayOutdated".Translate());

                        string text = "MpReplayOutdatedDesc1".Translate(data.protocol, MpVersion.Protocol) + "\n\n" + "MpReplayOutdatedDesc2".Translate() + "\n" + "MpReplayOutdatedDesc3".Translate();
                        if (autosave)
                            text += "\n\n" + "MpReplayOutdatedDesc4".Translate();

                        TooltipHandler.TipRegion(outdated, text);
                    }
                }

                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(entryRect))
                {
                    if (Event.current.button == 0)
                        selectedFile = file;
                    else if (data != null && data.Valid)
                        Find.WindowStack.Add(new FloatMenu(SaveFloatMenu(data).ToList()));
                }

                y += 40;
            }
        }

        private IEnumerable<FloatMenuOption> SaveFloatMenu(SaveFile save)
        {
            var saveMods = new StringBuilder();
            for (int i = 0; i < save.modIds.Length; i++)
            {
                var modName = save.modNames[i];
                var modId = save.modIds[i];
                var prefix = ModLister.AllInstalledMods.Any(m => m.PackageId == modId) ? "+" : "-";
                saveMods.Append($"{prefix} {modName}\n");
            }

            var activeMods = LoadedModManager.RunningModsListForReading.Join(m => "+ " + m.Name, "\n");

            yield return new FloatMenuOption("MpSeeModList".Translate(), () =>
            {
                Find.WindowStack.Add(new TwoTextAreas_Window($"RimWorld {save.rwVersion}\nSave mod list:\n\n{saveMods}", $"RimWorld {VersionControl.CurrentVersionString}\nActive mod list:\n\n{activeMods}"));
            });

            yield return new FloatMenuOption("Rename".Translate(), () =>
            {
                Find.WindowStack.Add(new Dialog_RenameFile(save.file, () => ReloadFiles()));
            });

            if (!MpVersion.IsDebug) yield break;

            yield return new FloatMenuOption("Debug info", () =>
            {
                Find.WindowStack.Add(new DebugTextWindow(UserReadableDesyncInfo.GenerateFromReplay(Replay.ForLoading(save.file))));
            });

            yield return new FloatMenuOption("Subscribe to Steam mods", () =>
            {
                for (int i = 0; i < save.modIds.Length; i++)
                {
                    if (!ulong.TryParse(save.modIds[i], out ulong id)) continue;
                    Log.Message($"Subscribed to: {save.modNames[i]}");
                    SteamUGC.SubscribeItem(new PublishedFileId_t(id));
                }
            });
        }

        private List<SteamPersona> friends = new List<SteamPersona>();
        private static readonly Color SteamGreen = new Color32(144, 186, 60, 255);

        private void DrawSteam(Rect inRect)
        {
            string info = null;
            if (!SteamManager.Initialized)
                info = "MpNotConnectedToSteam".Translate();
            else if (friends.Count == 0)
                info = "MpNoFriendsPlaying".Translate();

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

                if (Event.current.type == EventType.Repaint)
                    GUI.DrawTextureWithTexCoords(new Rect(5, entryRect.y + 4, 32, 32), SteamImages.GetTexture(friend.avatar), new Rect(0, 1, 1, -1));

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect.Right(45).Up(5), friend.username);

                GUI.color = SteamGreen;
                Text.Font = GameFont.Tiny;
                Widgets.Label(entryRect.Right(45).Down(8), "MpPlayingRimWorld".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                Text.Anchor = TextAnchor.MiddleCenter;

                if (friend.serverHost != CSteamID.Nil)
                {
                    Rect playButton = new Rect(entryRect.xMax - 85, entryRect.y + 5, 80, 40 - 10);
                    if (Widgets.ButtonText(playButton, "MpJoinButton".Translate()))
                    {
                        Close(false);
                        ConnectOnSteam(friend);
                    }
                }
                else
                {
                    Rect playButton = new Rect(entryRect.xMax - 125, entryRect.y + 5, 120, 40 - 10);
                    Widgets.ButtonText(playButton, "MpNotInMultiplayer".Translate(), false, false, false);
                }

                Text.Anchor = TextAnchor.UpperLeft;

                y += entryRect.height;
                i++;
            }

            Widgets.EndScrollView();
        }

        private void ConnectOnSteam(SteamPersona friend)
        {
            Log.Message("Connecting through Steam");

            Find.WindowStack.Add(new SteamConnectingWindow(friend.serverHost) { returnToServerBrowser = true });

            var conn = new SteamClientConn(friend.serverHost);
            conn.username = Multiplayer.username;
            Multiplayer.session = new MultiplayerSession();

            Multiplayer.session.client = conn;
            Multiplayer.session.ReapplyPrefs();

            conn.State = ConnectionStateEnum.ClientSteam;
        }

        private void DrawDirect(Rect inRect)
        {
            MultiplayerMod.settings.serverAddress = Widgets.TextField(new Rect(inRect.center.x - 200 / 2, 15f, 200, 35f), MultiplayerMod.settings.serverAddress);

            const float btnWidth = 115f;

            if (Widgets.ButtonText(new Rect(inRect.center.x - btnWidth / 2, 60f, btnWidth, 35f), "MpConnectButton".Translate()))
            {
                string addr = MultiplayerMod.settings.serverAddress.Trim();
                int port = MultiplayerServer.DefaultPort;
                string[] hostport = addr.Split(':');
                if (hostport.Length == 2)
                    int.TryParse(hostport[1], out port);
                else
                    port = MultiplayerServer.DefaultPort;

                Log.Message("Connecting directly");
                try
                {
                    Find.WindowStack.Add(new ConnectingWindow(hostport[0], port) { returnToServerBrowser = true });
                    MultiplayerMod.settings.Write();
                    Close(false);
                }
                catch (Exception)
                {
                    Messages.Message("MpInvalidAddress".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        private void DrawLan(Rect inRect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inRect.x, 8f, inRect.width, 40), "MpLanSearching".Translate() + MpUtil.FixedEllipsis());
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
                    Log.Message("Connecting to lan server");
                    Find.WindowStack.Add(new ConnectingWindow(server.endpoint.Address.ToString(), server.endpoint.Port) { returnToServerBrowser = true });
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
            lanListener.PollEvents();

            for (int i = servers.Count - 1; i >= 0; i--)
            {
                LanServer server = servers[i];
                if (Multiplayer.Clock.ElapsedMilliseconds - server.lastUpdate > 5000)
                    servers.RemoveAt(i);
            }
        }

        private long lastFriendUpdate = 0;

        private void UpdateSteam()
        {
            if (Multiplayer.Clock.ElapsedMilliseconds - lastFriendUpdate < 2000) return;

            friends.Clear();

            int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < friendCount; i++)
            {
                CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);

                SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t friendGame);
                bool playingRimworld = friendGame.m_gameID.AppID() == SteamIntegration.RimWorldAppId;

                if (!playingRimworld) continue;

                int avatar = SteamFriends.GetSmallFriendAvatar(friend);
                string username = SteamFriends.GetFriendPersonaName(friend);
                string connectValue = SteamFriends.GetFriendRichPresence(friend, "connect");

                CSteamID serverHost = CSteamID.Nil;
                if (connectValue != null &&
                    connectValue.Contains(SteamIntegration.SteamConnectStart) &&
                    ulong.TryParse(connectValue.Substring(SteamIntegration.SteamConnectStart.Length), out ulong hostId))
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

            lastFriendUpdate = Multiplayer.Clock.ElapsedMilliseconds;
        }

        public override void PostClose()
        {
            Cleanup(false);
        }

        public void Cleanup(bool sync)
        {
            WaitCallback stop = s => lanListener.Stop();

            if (sync)
                stop(null);
            else
                ThreadPool.QueueUserWorkItem(stop);
        }

        private void AddOrUpdate(IPEndPoint endpoint)
        {
            LanServer server = servers.Find(s => s.endpoint.Equals(endpoint));

            if (server == null)
            {
                servers.Add(new LanServer()
                {
                    endpoint = endpoint,
                    lastUpdate = Multiplayer.Clock.ElapsedMilliseconds
                });
            }
            else
            {
                server.lastUpdate = Multiplayer.Clock.ElapsedMilliseconds;
            }
        }

        class LanServer
        {
            public IPEndPoint endpoint;
            public long lastUpdate;
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
