using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Multiplayer.Client.Networking;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Profile;
using Verse.Sound;
using Verse.Steam;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public class HostWindow : Window
    {
        enum Tab
        {
            Connecting, Gameplay
        }

        public override Vector2 InitialSize => new(550f, 460f);

        private SaveFile file;
        public bool returnToServerBrowser;
        private Tab tab;

        private bool asyncTimeLocked;
        private bool multifactionLocked;

        private float height;

        private ServerSettings serverSettings;

        public static void VerifyAndOpen(string path)
        {
            FileInfo fileInfo = new(path);
            var saveFile = SaveFile.ReadMpSave(fileInfo);
            ServerBrowser.CheckGameVersionAndMods(
                saveFile,
                () => { Find.WindowStack.Add(new HostWindow(saveFile) { returnToServerBrowser = false }); }
            );
        }

        public HostWindow(SaveFile file = null)
        {
            closeOnAccept = false;
            doCloseX = true;

            serverSettings = Multiplayer.settings.PreferredLocalServerSettings;

            this.file = file;
            serverSettings.gameName = file?.gameName ?? Multiplayer.session?.gameName ?? $"{Multiplayer.username}'s game";

            serverSettings.asyncTime = file?.asyncTime ?? Multiplayer.game?.gameComp.asyncTime ?? false;
            serverSettings.multifaction = file?.multifaction ?? Multiplayer.game?.gameComp.multifaction ?? false;

            if (serverSettings.asyncTime)
                asyncTimeLocked = true; // Once enabled in a save, cannot be disabled

            if (serverSettings.multifaction)
                multifactionLocked = true;

            var localAddr = Endpoints.GetLocalIpAddress() ?? "127.0.0.1";
            serverSettings.lanAddress = localAddr;

            if (MpVersion.IsDebug)
            {
                serverSettings.debugMode = true;
                serverSettings.desyncTraces = true;
            }
        }

        private string maxPlayersBuffer, autosaveBuffer;

        private const int MaxGameNameLength = 70;
        private const float LabelWidth = 110f;
        private const float CheckboxWidth = LabelWidth + 30f;

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            string title = file == null ? "MpHostIngame".Translate() : "MpHostSavefile".Translate();

            // Title
            Widgets.Label(inRect.Down(0), title);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            var entry = new Rect(0, 45, inRect.width, 30f);
            entry.xMin += 4;

            // Game name
            serverSettings.gameName = MpUI.TextEntryLabeled(entry, $"{"MpGameName".Translate()}:  ", serverSettings.gameName, LabelWidth);
            if (serverSettings.gameName.Length > MaxGameNameLength)
                serverSettings.gameName = serverSettings.gameName.Substring(0, MaxGameNameLength);

            entry = entry.Down(50);

            using (MpStyle.Set(TextAnchor.MiddleLeft))
            {
                DoTabButton(entry.Width(140).Height(40f), Tab.Connecting);
                DoTabButton(entry.Down(50f).Width(140).Height(40f), Tab.Gameplay);
            }

            if (tab == Tab.Connecting)
                DoConnecting(entry.MinX(entry.xMin + 150));
            else
                DoGameplay(entry.MinX(entry.xMin + 150));

            if (Event.current.type == EventType.Layout && height != entry.yMax)
            {
                height = entry.yMax;
                SetInitialSizeAndPosition();
            }

            var buttonRect = new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f);

            // Host button
            if (Widgets.ButtonText(buttonRect, "MpHostButton".Translate()))
            {
                TryHost();
            }
        }

        private void DoTabButton(Rect r, Tab tab)
        {
            Widgets.DrawOptionBackground(r, tab == this.tab);
            if (Widgets.ButtonInvisible(r, true))
            {
                this.tab = tab;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            float num = r.x + 10f;
            Rect rect = new Rect(num, r.y + (r.height - 20f) / 2f, 20f, 20f);
            GUI.DrawTexture(rect, tab == Tab.Connecting ? MultiplayerStatic.OptionsGeneral : MultiplayerStatic.OptionsGameplay);
            num += 30f;
            Widgets.Label(new Rect(num, r.y, r.width - num, r.height), tab == Tab.Connecting ? "MpHostTabConnecting".Translate() : "MpHostTabGameplay".Translate());
        }

        private void DoConnecting(Rect entry)
        {
            var buffers = new ServerSettingsUI.BufferSet
            {
                MaxPlayersBuffer = maxPlayersBuffer,
                AutosaveBuffer = autosaveBuffer
            };
            
            ServerSettingsUI.DrawNetworkingSettings(entry, serverSettings, buffers);
            
            maxPlayersBuffer = buffers.MaxPlayersBuffer;
            autosaveBuffer = buffers.AutosaveBuffer;
        }

        private void DoGameplay(Rect entry)
        {
            var buffers = new ServerSettingsUI.BufferSet
            {
                MaxPlayersBuffer = maxPlayersBuffer,
                AutosaveBuffer = autosaveBuffer
            };
            
            ServerSettingsUI.DrawGameplaySettings(entry, serverSettings, buffers, asyncTimeLocked, multifactionLocked);
            
            maxPlayersBuffer = buffers.MaxPlayersBuffer;
            autosaveBuffer = buffers.AutosaveBuffer;
        }



        private void TryHost()
        {
            var settings = MpUtil.ShallowCopy(serverSettings, new ServerSettings());

            if (settings.direct && !TryParseEndpoints(settings))
                return;

            if (settings.steam && !SteamManager.Initialized)
            {
                Messages.Message("MpSteamNotAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (settings.gameName.NullOrEmpty())
            {
                Messages.Message("MpInvalidGameName".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (settings.hasPassword && settings.password.NullOrEmpty())
            {
                Messages.Message("MpInvalidGamePassword".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!TryStartLocalServer(settings))
                return;

            if (file?.replay ?? Multiplayer.IsReplay)
                HostFromReplay(settings);
            else if (file == null)
                HostFromSpIngame(settings);
            else
                HostFromSpSaveFile(settings);

            // No need to return to the server browser since we successfully started a local server.
            returnToServerBrowser = false;
            Close();
        }

        static bool TryParseEndpoints(ServerSettings settings)
        {
            var invalidEndpoint = settings.TryParseEndpoints(out _);
            if (invalidEndpoint != null)
                Messages.Message(
                    "MpInvalidEndpoint".Translate(invalidEndpoint),
                    MessageTypeDefOf.RejectInput,
                    false
                );
            return invalidEndpoint == null;
        }

        static bool TryStartLocalServer(ServerSettings settings)
        {
            var localServer = new MultiplayerServer(settings);
            var success = true;

            if (settings.direct) {
                var liteNet = StartLiteNetManager(localServer, settings);
                if (liteNet == null) success = false;
                else localServer.netManagers.Add(liteNet);
            }

            if (settings.lan)
            {
                var lan = StartLanManager(localServer, settings);
                if (lan == null) success = false;
                else localServer.netManagers.Add(lan);
            }

            if (settings.steam)
            {
                var steam = StartSteamP2PManager(localServer, settings);
                if (steam == null) success = false;
                else localServer.netManagers.Add(steam);
            }

            if (!success)
            {
                localServer.netManagers.ForEach(man => man.Stop());
                return false;
            }

            Multiplayer.LocalServer = localServer;
            return true;
        }

        private static INetManager StartLiteNetManager(MultiplayerServer server, ServerSettings settings)
        {
            var invalidEndpoint = settings.TryParseEndpoints(out var endpoints);
            if (invalidEndpoint != null)
            {
                Messages.Message(
                    "MpInvalidEndpoint".Translate(invalidEndpoint),
                    MessageTypeDefOf.RejectInput,
                    false
                );
                return null;
            }

            if (LiteNetManager.Create(server, endpoints, out var liteNet)) return liteNet;

            foreach (var (endpoint, man) in liteNet.netManagers)
            {
                if (man.IsRunning) continue;
                Messages.Message($"Failed to bind direct on {endpoint}", MessageTypeDefOf.RejectInput, false);
            }

            liteNet.Stop();
            return null;
        }

        public static INetManager StartLanManager(MultiplayerServer server, ServerSettings settings)
        {
            if (!IPAddress.TryParse(settings.lanAddress, out var ipAddr))
            {
                Messages.Message(
                    "MpInvalidEndpoint".Translate(settings.lanAddress),
                    MessageTypeDefOf.RejectInput,
                    false
                );
                return null;
            }

            var man = LiteNetLanManager.Create(server, ipAddr);
            if (man != null) return man;

            Messages.Message($"Failed to bind LAN on {settings.lanAddress}", MessageTypeDefOf.RejectInput, false);
            return null;
        }

        public static INetManager StartSteamP2PManager(MultiplayerServer server, ServerSettings settings)
        {
            var man = SteamP2PNetManager.Create(server);
            if (man != null) return man;

            Messages.Message("Failed to start Steam networking", MessageTypeDefOf.RejectInput, false);
            return null;
        }

        public override void PostClose()
        {
            Multiplayer.settings.Write();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        private void HostFromSpSaveFile(ServerSettings settings)
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = new Game
                {
                    InitData = new GameInitData
                    {
                        gameToLoad = file.displayName
                    }
                };

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    LongEventHandler.QueueLongEvent(() => HostUtil.HostServer(settings, false), "MpLoading", false, null);
                });
            }, "Play", "LoadingLongEvent", true, null);
        }

        private void HostFromSpIngame(ServerSettings settings)
        {
            HostUtil.HostServer(settings, false);
        }

        private void HostFromReplay(ServerSettings settings)
        {
            void ReplayLoaded() => HostUtil.HostServer(settings, true);

            if (file != null)
            {
                Replay.LoadReplay(
                    file.file,
                    true,
                    ReplayLoaded,
                    GenScene.GoToMainMenu,
                    "MpSimulatingServer"
                );
            }
            else
            {
                ReplayLoaded();
            }
        }
        /// <summary>
        /// Start hosting programmatically for the bootstrap flow.
        /// </summary>
        public static bool HostProgrammatically(ServerSettings overrides, SaveFile file = null, bool randomDirectPort = true)
        {
            var settings = MpUtil.ShallowCopy(overrides, new ServerSettings());
            if (randomDirectPort)
                settings.directAddress = "0.0.0.0:0"; // OS assigns free port

            if (!TryStartLocalServer(settings))
                return false;

            if (file?.replay ?? Multiplayer.IsReplay)
                new HostWindow(file).HostFromReplay(settings);
            else if (file == null)
                new HostWindow().HostFromSpIngame(settings);
            else
                new HostWindow(file).HostFromSpSaveFile(settings);

            return true;
        }
    }
}
