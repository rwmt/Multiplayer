extern alias zip;

using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Steam;
using HarmonyLib;
using Verse.Sound;
using Multiplayer.Client.Util;
using Multiplayer.Common.Util;

namespace Multiplayer.Client
{

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
        }

        private Vector2 lanScroll;
        private Vector2 steamScroll;
        private Vector2 hostScroll;
        private static Tabs tab;

        enum Tabs
        {
            Lan, Direct, Steam, Host
        }

        private WidgetRow widgetRow = new WidgetRow();

        public override void DoWindowContents(Rect inRect)
        {
            DrawInfoButtons();
            inRect.yMin += 35f;

            List<TabRecord> tabs = new List<TabRecord>()
            {
                new("MpLan".Translate(), () => tab = Tabs.Lan,  tab == Tabs.Lan),
                new("MpDirect".Translate(), () => tab = Tabs.Direct, tab == Tabs.Direct),
                new("MpSteam".Translate(), () => tab = Tabs.Steam, tab == Tabs.Steam),
                new("MpHostTab".Translate(), () => tab = Tabs.Host, tab == Tabs.Host),
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

        private void DrawInfoButtons()
        {
            float x = 0;

            const string WebsiteLink = "https://rimworldmultiplayer.com";
            const string DiscordLink = "https://discord.gg/n5E2cb2Y4Z";

            bool Button(Texture2D icon, string labelKey, string tip, Color baseIconColor, float iconSize = 24f)
            {
                var label = labelKey.Translate();
                var labelWidth = Text.CalcSize(label).x;
                var btn = new Rect(x, 0, 24 + 1 + labelWidth, 24);
                var mouseOver = Mouse.IsOver(btn);

                MouseoverSounds.DoRegion(btn);
                TooltipHandler.TipRegion(btn, tip);

                using (MpStyle.Set(mouseOver ? Color.yellow : baseIconColor))
                {
                    GUI.DrawTexture(new Rect(x += (24 - iconSize) / 2, (24 - iconSize) / 2, iconSize, iconSize), icon);
                    x += 24;
                }

                x += 1;

                using (MpStyle.Set(mouseOver ? Color.yellow : Color.white))
                using (MpStyle.Set(TextAnchor.MiddleCenter))
                    MpUI.Label(new Rect(x, 0, labelWidth, 24f), labelKey.Translate());

                x += labelWidth;
                x += 10;

                return Widgets.ButtonInvisible(btn);
            }

            const string compatLabel = "MpCompatibilityButton";
            const string compatLabelDesc = "MpCompatibilityButtonDesc";

            if (Button(TexButton.ToggleLog, compatLabel, MpUtil.TranslateWithDoubleNewLines(compatLabelDesc, 2), Color.grey, 20))
                Find.WindowStack.Add(new ModCompatWindow(null, false, false, null));

            if (Button(MultiplayerStatic.WebsiteIcon, "MpWebsiteButton", "MpLinkButtonDesc".Translate() + " " + WebsiteLink, Color.grey, 20))
                Application.OpenURL(WebsiteLink);

            if (Button(MultiplayerStatic.DiscordIcon, "MpDiscordButton", "MpLinkButtonDesc".Translate() + " " + DiscordLink, Color.white))
                Application.OpenURL(DiscordLink);

            if (false) // todo
                Button(
                    TexButton.NewItem,
                    "MpActiveConfigsButton",
                    "MpActiveConfigsButtonDesc1".Translate("Player's game") + "\n\n" + "MpActiveConfigsButtonDesc2".Translate(),
                    Color.grey,
                    20
                );
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

            using (MpStyle.Set(GameFont.Medium))
            {
                float textHeight1 = Text.CalcHeight("MpMultiplayerSaves".Translate(), inRect.width);
                Widgets.Label(viewRect.Right(18f), "MpMultiplayerSaves".Translate());
                y += textHeight1 + 10;
            }

            if (!mpCollapsed)
            {
                DrawSaveList(reader.MpSaves, viewRect.width, ref y);
                y += 25;
            }

            collapseRect.y += y;

            if (Widgets.ButtonImage(collapseRect, spCollapsed ? TexButton.Reveal : TexButton.Collapse))
                spCollapsed = !spCollapsed;

            viewRect.y = y;
            using (MpStyle.Set(GameFont.Medium))
            {
                float textHeight2 = Text.CalcHeight("MpSingleplayerSaves".Translate(), inRect.width);
                Widgets.Label(viewRect.Right(18), "MpSingleplayerSaves".Translate());
                y += textHeight2 + 10;
            }

            if (!spCollapsed)
                DrawSaveList(reader.SpSaves, viewRect.width, ref y);

            if (Event.current.type == EventType.Layout)
                hostHeight = y;

            Widgets.EndScrollView();

            if (selectedFile == null)
            {
                bool noSaves = reader.SpSaves.Count == 0 && reader.MpSaves.Count == 0;

                using (MpStyle.Set(TextAnchor.MiddleCenter))
                    Widgets.Label(new Rect(outRect.x, outRect.yMax, outRect.width, 80), noSaves ? "MpNoSaves".Translate() : "MpNothingSelected".Translate());
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
            if (file.HasRwVersion)
            {
                if (file.replay && Multiplayer.ShowDevInfo)
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
                    .Do(w => w.buttonAText = "MpContinueAnyway".Translate());
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

                using (MpStyle.Set(TextAnchor.MiddleLeft))
                    Widgets.Label(entryRect.Right(10), data?.displayName ?? "Loading...");

                using var _ = MpStyle.Set(new Color(0.6f, 0.6f, 0.6f));
                using var __ = MpStyle.Set(GameFont.Tiny);

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

                    if (!data.HasRwVersion)
                    {
                        var rect = new Rect(infoText.x - 80, infoText.y + 8f, 80, 24f);
                        GUI.color = Color.red;

                        Widgets.Label(rect, $"({"EItemUpdateStatus_k_EItemUpdateStatusInvalid".Translate()})");
                        TooltipHandler.TipRegion(rect, new TipSignal("SaveIsUnknownFormat".Translate()));
                    }
                    else if (data.replay && !data.MajorAndMinorVerEqualToCurrent)
                    {
                        GUI.color = new Color(0.8f, 0.8f, 0, 0.6f);
                        var outdated = new Rect(infoText.x - 80, infoText.y + 8f, 80, 24f);
                        Widgets.Label(outdated, "MpSaveOutdated".Translate());

                        var text = "MpSaveOutdatedDesc".Translate(data.rwVersion, VersionControl.CurrentVersionString);
                        TooltipHandler.TipRegion(outdated, text);
                    }

                    Text.Font = GameFont.Small;
                    GUI.color = Color.white;

                    if (Widgets.ButtonInvisible(entryRect, false))
                    {
                        if (Event.current.button == 0)
                            selectedFile = file;
                        else if (Event.current.button == 1 && data.HasRwVersion)
                            Find.WindowStack.Add(new FloatMenu(SaveFloatMenu(data).ToList()));
                    }
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
                Find.WindowStack.Add(new TwoTextAreasWindow($"RimWorld {save.rwVersion}\nSave mod list:\n\n{saveMods}", $"RimWorld {VersionControl.CurrentVersionString}\nActive mod list:\n\n{activeMods}"));
            });

            yield return new FloatMenuOption("MpOpenSaveFolder".Translate(), () =>
            {
                ShellOpenDirectory.Execute(save.file.DirectoryName);
            });

            yield return new FloatMenuOption("MpFileRename".Translate(), () =>
            {
                Find.WindowStack.Add(new RenameFileWindow(save.file, ReloadFiles));
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
                    // todo these aren't steam ids
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
                using (MpStyle.Set(TextAnchor.MiddleCenter))
                    Widgets.Label(new Rect(0, 8, inRect.width, 40f), info);

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

                using (MpStyle.Set(TextAnchor.MiddleLeft))
                    Widgets.Label(entryRect.Right(45).Up(5), friend.username);

                using (MpStyle.Set(GameFont.Tiny))
                using (MpStyle.Set(TextAnchor.MiddleLeft))
                using (MpStyle.Set(SteamGreen))
                    Widgets.Label(entryRect.Right(45).Down(8), "MpPlayingRimWorld".Translate());

                if (friend.serverHost != CSteamID.Nil)
                {
                    Rect playButton = new Rect(entryRect.xMax - 85, entryRect.y + 5, 80, 40 - 10);
                    if (Widgets.ButtonText(playButton, "MpJoinButton".Translate()))
                    {
                        Close(false);
                        ClientUtil.TrySteamConnectWithWindow(friend.serverHost);
                    }
                }
                else
                {
                    Rect playButton = new Rect(entryRect.xMax - 125, entryRect.y + 5, 120, 40 - 10);
                    Widgets.ButtonText(playButton, "MpNotInMultiplayer".Translate(), false, false, false);
                }

                y += entryRect.height;
                i++;
            }

            Widgets.EndScrollView();
        }

        private void DrawDirect(Rect inRect)
        {
            Multiplayer.settings.serverAddress = Widgets.TextField(new Rect(inRect.center.x - 200 / 2, 15f, 200, 35f), Multiplayer.settings.serverAddress);

            const float btnWidth = 115f;

            if (Widgets.ButtonText(new Rect(inRect.center.x - btnWidth / 2, 60f, btnWidth, 35f), "MpConnectButton".Translate()) &&
                DirectConnect(Multiplayer.settings.serverAddress.Trim()))
                Close(false);
        }

        private static bool DirectConnect(string addr)
        {
            var port = MultiplayerServer.DefaultPort;

            // If IPv4 or IPv6 address with optional port
            if (Endpoints.TryParse(addr, MultiplayerServer.DefaultPort, out var endpoint))
            {
                addr = endpoint.Address.ToString();
                port = endpoint.Port;
            }
            // Hostname with optional port
            else
            {
                var split = addr.Split(':');
                addr = split[0];
                if (split.Length == 2 && int.TryParse(split[1], out var parsedPort) && parsedPort is > IPEndPoint.MinPort and < IPEndPoint.MaxPort)
                    port = parsedPort;
            }

            Log.Message("Connecting directly");

            try
            {
                ClientUtil.TryConnectWithWindow(addr, port);
                Multiplayer.settings.Write();
                return true;
            }
            catch (Exception e)
            {
                Messages.Message("MpInvalidAddress".Translate(), MessageTypeDefOf.RejectInput, false);
                Log.Error($"Exception while connecting directly {e}");
            }

            return false;
        }

        private void DrawLan(Rect inRect)
        {
            using (MpStyle.Set(TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(inRect.x, 8f, inRect.width, 40), "MpLanSearching".Translate() + MpUI.FixedEllipsis());
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

                using (MpStyle.Set(TextAnchor.MiddleLeft))
                    Widgets.Label(entryRect.Right(5), "" + server.endpoint);

                Rect playButton = new Rect(entryRect.xMax - 75, entryRect.y + 5, 70, 40 - 10);
                if (Widgets.ButtonText(playButton, ">>"))
                {
                    Close(false);
                    Log.Message("Connecting to lan server");

                    var address = server.endpoint.Address.ToString();
                    var port = server.endpoint.Port;
                    ClientUtil.TryConnectWithWindow(address, port);
                }

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
                if (Multiplayer.clock.ElapsedMilliseconds - server.lastUpdate > 5000)
                    servers.RemoveAt(i);
            }
        }

        private long lastFriendUpdate = 0;

        private void UpdateSteam()
        {
            if (Multiplayer.clock.ElapsedMilliseconds - lastFriendUpdate < 2000) return;

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

            lastFriendUpdate = Multiplayer.clock.ElapsedMilliseconds;
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
                    lastUpdate = Multiplayer.clock.ElapsedMilliseconds
                });
            }
            else
            {
                server.lastUpdate = Multiplayer.clock.ElapsedMilliseconds;
            }
        }

        class LanServer
        {
            public IPEndPoint endpoint;
            public long lastUpdate;
        }

        public override void OnAcceptKeyPressed()
        {
            if (tab == Tabs.Direct)
            {
                DirectConnect(Multiplayer.settings.serverAddress.Trim());
                Close(false);
            }
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
