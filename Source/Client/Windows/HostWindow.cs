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
using Verse.Sound;
using Verse.Steam;
using Multiplayer.Client;
using Multiplayer.Client.Util;
using Multiplayer.Common.Util;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class HostWindow : Window
    {
        public override Vector2 InitialSize => new(450f, height + 45f);

        private SaveFile file;
        public bool returnToServerBrowser;
        private bool withSimulation;
        private bool asyncTime;
        private bool asyncTimeLocked;

        private float height;

        private ServerSettings serverSettings;

        public HostWindow(SaveFile file = null, bool withSimulation = false)
        {
            closeOnAccept = false;
            doCloseX = true;

            serverSettings = Multiplayer.settings.serverSettings;

            this.withSimulation = withSimulation;
            this.file = file;
            serverSettings.gameName = file?.gameName ?? Multiplayer.session?.gameName ?? $"{Multiplayer.username}'s game";

            asyncTime = file?.asyncTime ?? Multiplayer.game?.gameComp.asyncTime ?? false;

            if (asyncTime)
                asyncTimeLocked = true; // Once enabled in a save, cannot be disabled

            var localAddr = MpUtil.GetLocalIpAddress() ?? "127.0.0.1";
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

            // Game name
            serverSettings.gameName = MpUI.TextEntryLabeled(entry, $"{"MpGameName".Translate()}:  ", serverSettings.gameName, LabelWidth);
            if (serverSettings.gameName.Length > MaxGameNameLength)
                serverSettings.gameName = serverSettings.gameName.Substring(0, MaxGameNameLength);

            entry = entry.Down(40);

            // Max players
            MpUI.TextFieldNumericLabeled(entry.Width(LabelWidth + 30f), $"{"MpMaxPlayers".Translate()}:  ", ref serverSettings.maxPlayers, ref maxPlayersBuffer, LabelWidth, 0, 999);

            // Autosave interval
            var autosaveRect = entry.MinX(entry.x + LabelWidth + 30f + 10f);
            var autosaveKey = serverSettings.autosaveUnit == AutosaveUnit.Days
                ? "MpAutosaveIntervalDays"
                : "MpAutosaveIntervalMinutes";

            var changeAutosaveUnit = MpUI.TextFieldNumericLabeled(
                autosaveRect,
                $"{autosaveKey.Translate()}: ",
                ref serverSettings.autosaveInterval,
                ref autosaveBuffer,
                200f,
                0,
                999,
                true,
                MpUtil.TranslateWithDoubleNewLines("MpAutosaveIntervalDesc", 3)
            );

            if (changeAutosaveUnit)
            {
                serverSettings.autosaveUnit = serverSettings.autosaveUnit.Cycle();
                serverSettings.autosaveInterval *=
                    serverSettings.autosaveUnit == AutosaveUnit.Minutes ?
                    8f : // Days to minutes
                    0.125f; // Minutes to days
                autosaveBuffer = serverSettings.autosaveInterval.ToString();
            }

            entry = entry.Down(40);

            /*const char passChar = '\u2022';
            if (Event.current.type == EventType.Repaint || Event.current.isMouse)
                TextEntryLabeled(entry.Width(200), "Password:  ", new string(passChar, password.Length), labelWidth);
            else
                password = TextEntryLabeled(entry.Width(200), "Password:  ", password, labelWidth);
            entry = entry.Down(40);*/

            var checkboxWidth = LabelWidth + 30f;

            // Direct hosting
            var directLabel = $"{"MpDirect".Translate()}:  ";
            var directLabelWidth = Text.CalcSize(directLabel).x;
            MpUI.CheckboxLabeled(entry.Width(checkboxWidth), directLabel, ref serverSettings.direct, placeTextNearCheckbox: true);
            if (serverSettings.direct)
                serverSettings.directAddress = Widgets.TextField(entry.Right(checkboxWidth + 10).MaxX(inRect.xMax), serverSettings.directAddress);

            entry = entry.Down(30);

            // LAN hosting
            var lanRect = entry.Width(checkboxWidth);
            MpUI.CheckboxLabeled(lanRect, $"{"MpLan".Translate()}:  ", ref serverSettings.lan, placeTextNearCheckbox: true);
            TooltipHandler.TipRegion(lanRect, $"{"MpLanDesc1".Translate()}\n\n{"MpLanDesc2".Translate(serverSettings.lanAddress)}");

            entry = entry.Down(30);

            // Steam hosting
            if (SteamManager.Initialized)
            {
                MpUI.CheckboxLabeled(entry.Width(checkboxWidth), $"{"MpSteam".Translate()}:  ", ref serverSettings.steam, placeTextNearCheckbox: true);
                entry = entry.Down(30);
            }

            // Async time
            {
                TooltipHandler.TipRegion(entry.Width(checkboxWidth), $"{"MpAsyncTimeDesc".Translate()}\n\n{"MpExperimentalFeature".Translate()}");
                MpUI.CheckboxLabeled(entry.Width(checkboxWidth), $"{"MpAsyncTime".Translate()}:  ", ref asyncTime, placeTextNearCheckbox: true, disabled: asyncTimeLocked);
                entry = entry.Down(30);
            }

            // Log desync traces
            MpUI.CheckboxLabeledWithTipNoHighlight(
                entry.Width(checkboxWidth),
                $"{"MpLogDesyncTraces".Translate()}:  ",
                MpUtil.TranslateWithDoubleNewLines("MpLogDesyncTracesDesc", 2),
                ref serverSettings.desyncTraces,
                placeTextNearCheckbox: true
            );
            entry = entry.Down(30);

            // Arbiter
            if (MpVersion.IsDebug) {
                TooltipHandler.TipRegion(entry.Width(checkboxWidth), "MpArbiterDesc".Translate());
                MpUI.CheckboxLabeled(entry.Width(checkboxWidth), $"{"MpRunArbiter".Translate()}:  ", ref serverSettings.arbiter, placeTextNearCheckbox: true);
                entry = entry.Down(30);
            }

            // Dev mode
            MpUI.CheckboxLabeledWithTipNoHighlight(
                entry.Width(checkboxWidth),
                $"{"MpHostingDevMode".Translate()}:  ",
                MpUtil.TranslateWithDoubleNewLines("MpHostingDevModeDesc", 2),
                ref serverSettings.debugMode,
                placeTextNearCheckbox: true
            );

            // Dev mode scope
            if (serverSettings.debugMode
                && CustomButton(entry.Right(checkboxWidth + 10f), $"MpHostingDevMode{serverSettings.devModeScope}".Translate()))
            {
                serverSettings.devModeScope = serverSettings.devModeScope.Cycle();
            }

            entry = entry.Down(30);

            // Sync configs
            TooltipHandler.TipRegion(entry.Width(checkboxWidth), MpUtil.TranslateWithDoubleNewLines("MpSyncConfigsDesc", 3));
            MpUI.CheckboxLabeled(entry.Width(checkboxWidth), $"{"MpSyncConfigs".Translate()}:  ", ref serverSettings.syncConfigs, placeTextNearCheckbox: true);
            entry = entry.Down(30);

            // Auto join-points
            DrawJoinPointOptions(entry);
            entry = entry.Down(30);

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

        private static Color CustomButtonColor = new(0.15f, 0.15f, 0.15f);

        private void DrawJoinPointOptions(Rect entry)
        {
            using (MpStyle.Set(TextAnchor.MiddleRight))
                MpUI.LabelWithTip(
                    entry.Width(LabelWidth + 1),
                    $"{"MpAutoJoinPoints".Translate()}:  ",
                    MpUtil.TranslateWithDoubleNewLines("MpAutoJoinPointsDesc", 3)
                );

            var flags = Enum.GetValues(typeof(AutoJoinPointFlags))
                .OfType<AutoJoinPointFlags>()
                .Where(f => serverSettings.autoJoinPoint.HasFlag(f))
                .Select(f => $"MpAutoJoinPoints{f}".Translate())
                .Join(", ");
            if (flags.Length == 0) flags = "Off";

            if (CustomButton(entry.Right(LabelWidth + 10), flags))
                Find.WindowStack.Add(new FloatMenu(Flags().ToList()));

            IEnumerable<FloatMenuOption> Flags()
            {
                foreach (var flag in Enum.GetValues(typeof(AutoJoinPointFlags)).OfType<AutoJoinPointFlags>())
                    yield return new FloatMenuOption($"MpAutoJoinPoints{flag}".Translate(), () =>
                    {
                        if (serverSettings.autoJoinPoint.HasFlag(flag))
                            serverSettings.autoJoinPoint &= ~flag;
                        else
                            serverSettings.autoJoinPoint |= flag;
                    });
            }
        }

        private static bool CustomButton(Rect rect, string label)
        {
            using var _ = MpStyle.Set(TextAnchor.MiddleLeft);
            var flagsWidth = Text.CalcSize(label).x;

            const float btnMargin = 5f;

            var flagsBtn = rect.Width(flagsWidth + btnMargin * 2);
            Widgets.DrawRectFast(flagsBtn.Height(24).Down(3), CustomButtonColor);
            Widgets.DrawHighlightIfMouseover(flagsBtn.Height(24).Down(3));
            MpUI.Label(rect.Right(btnMargin).Width(flagsWidth), label);

            return Widgets.ButtonInvisible(flagsBtn);
        }

        private void TryHost()
        {
            var settings = MpUtil.ShallowCopy(serverSettings, new ServerSettings());

            if (settings.direct && TryParseEndpoints(serverSettings.directAddress) is false)
                return;

            if (settings.gameName.NullOrEmpty())
            {
                Messages.Message("MpInvalidGameName".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (TryStartLocalServer(settings) is false)
                return;

            if (file?.replay ?? Multiplayer.IsReplay)
                HostFromMultiplayerSave(settings);
            else if (file == null)
                HostUtil.HostServer(settings, false, false, asyncTime);
            else
                HostFromSingleplayer(settings);

            Close();
        }

        static bool TryParseEndpoints(string endpoints)
        {
            var split = endpoints.Split(MultiplayerServer.EndpointSeparator);
            var success = true;

            foreach (var endpoint in split)
                if (!Endpoints.TryParse(endpoint, MultiplayerServer.DefaultPort, out _))
                {
                    Messages.Message(
                        "MpInvalidEndpoint".Translate(endpoint),
                        MessageTypeDefOf.RejectInput,
                        false
                    );
                    success = false;
                }

            return success;
        }

        static bool TryStartLocalServer(ServerSettings settings)
        {
            var localServer = new MultiplayerServer(settings);
            localServer.net.StartNet();

            var failed = false;

            if (settings.direct && localServer.net.netManagers.Any(m => m.Item2.IsRunning is false))
            {
                foreach (var (endpoint, man) in localServer.net.netManagers)
                    if (man.IsRunning is false)
                        Messages.Message("Failed to bind direct on " + endpoint, MessageTypeDefOf.RejectInput, false);
                failed = true;
            }

            if (settings.lan && !localServer.net.lanManager.IsRunning)
            {
                Messages.Message("Failed to bind LAN on " + settings.lanAddress, MessageTypeDefOf.RejectInput, false);
                failed = true;
            }

            if (failed)
                localServer.net.StopNet();
            else
                Multiplayer.LocalServer = localServer;

            return !failed;
        }

        public override void PostClose()
        {
            Multiplayer.WriteSettingsToDisk();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        private void HostFromSingleplayer(ServerSettings settings)
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
                    LongEventHandler.QueueLongEvent(() => HostUtil.HostServer(settings, false, false, asyncTime), "MpLoading", false, null);
                });
            }, "Play", "LoadingLongEvent", true, null);
        }

        private void HostFromMultiplayerSave(ServerSettings settings)
        {
            void ReplayLoaded() => HostUtil.HostServer(settings, true, withSimulation, asyncTime);

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
    }
}
