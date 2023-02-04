using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Profile;
using Verse.Sound;
using Verse.Steam;
using Multiplayer.Client.Util;
using Multiplayer.Common.Util;

namespace Multiplayer.Client
{

    [StaticConstructorOnStartup]
    public class HostWindow : Window
    {
        enum Tab
        {
            Connecting, Gameplay
        }

        public override Vector2 InitialSize => new(550f, 430f);

        private SaveFile file;
        public bool returnToServerBrowser;
        private bool withSimulation;
        private bool asyncTime;
        private bool asyncTimeLocked;
        private Tab tab;

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
            Texture2D texture2D = ContentFinder<Texture2D>.Get(tab == Tab.Connecting ? "UI/Icons/Options/OptionsGeneral" : "UI/Icons/Options/OptionsGameplay");
            GUI.DrawTexture(rect, texture2D);
            num += 30f;
            Widgets.Label(new Rect(num, r.y, r.width - num, r.height), tab == Tab.Connecting ? "MpHostTabConnecting".Translate() : "MpHostTabGameplay".Translate());
        }

        private void DoConnecting(Rect entry)
        {
            // Max players
            MpUI.TextFieldNumericLabeled(entry.Width(LabelWidth + 35f), $"{"MpMaxPlayers".Translate()}:  ", ref serverSettings.maxPlayers, ref maxPlayersBuffer, LabelWidth, 0, 999);
            entry = entry.Down(30);

            // Password
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpHostGamePassword".Translate()}:  ", ref serverSettings.hasPassword, order: ElementOrder.Right);
            if (serverSettings.hasPassword)
                MpUI.DoPasswordField(entry.Right(CheckboxWidth + 10).MaxX(entry.xMax), "PasswordField", ref serverSettings.password);
            entry = entry.Down(30);

            // Direct hosting
            var directLabel = $"{"MpHostDirect".Translate()}:  ";
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), directLabel, ref serverSettings.direct, order: ElementOrder.Right);
            TooltipHandler.TipRegion(entry.Width(LabelWidth), MpUtil.TranslateWithDoubleNewLines("MpHostDirectDesc", 4));
            if (serverSettings.direct)
                serverSettings.directAddress = Widgets.TextField(entry.Right(CheckboxWidth + 10).MaxX(entry.xMax), serverSettings.directAddress);

            entry = entry.Down(30);

            // LAN hosting
            var lanRect = entry.Width(CheckboxWidth);
            MpUI.CheckboxLabeled(lanRect, $"{"MpLan".Translate()}:  ", ref serverSettings.lan, order: ElementOrder.Right);
            TooltipHandler.TipRegion(lanRect, $"{"MpLanDesc1".Translate()}\n\n{"MpLanDesc2".Translate(serverSettings.lanAddress)}");

            entry = entry.Down(30);

            // Steam hosting
            if (SteamManager.Initialized)
            {
                MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpSteam".Translate()}:  ", ref serverSettings.steam, order: ElementOrder.Right);
                entry = entry.Down(30);
            }

            // Sync configs
            TooltipHandler.TipRegion(entry.Width(CheckboxWidth), MpUtil.TranslateWithDoubleNewLines("MpSyncConfigsDescNew", 3));
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpSyncConfigs".Translate()}:  ", ref serverSettings.syncConfigs, order: ElementOrder.Right);
            entry = entry.Down(30);
        }

        private void DoGameplay(Rect entry)
        {
            // Autosave interval
            var autosaveUnitKey = serverSettings.autosaveUnit == AutosaveUnit.Days
                ? "MpAutosavesDays"
                : "MpAutosavesMinutes";

            bool changeAutosaveUnit = false;

            LeftLabel(entry, $"{"MpAutosaves".Translate()}:  ");
            TooltipHandler.TipRegion(entry.Width(LabelWidth), MpUtil.TranslateWithDoubleNewLines("MpAutosavesDesc", 3));

            using (MpStyle.Set(TextAnchor.MiddleRight))
                DoRow(
                    entry.Right(LabelWidth + 10),
                    rect => MpUI.LabelFlexibleWidth(rect, "MpAutosavesEvery".Translate()) + 6,
                    rect =>
                    {
                        Widgets.TextFieldNumeric(
                            rect.Width(50f),
                            ref serverSettings.autosaveInterval,
                            ref autosaveBuffer,
                            0,
                            999
                        );
                        return 50f + 6;
                    },
                    rect =>
                    {
                        changeAutosaveUnit = CustomButton(rect, autosaveUnitKey.Translate(), out var width);
                        return width;
                    }
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

            entry = entry.Down(30);

            // Async time
            TooltipHandler.TipRegion(entry.Width(CheckboxWidth), $"{"MpAsyncTimeDesc".Translate()}\n\n{"MpExperimentalFeature".Translate()}");
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpAsyncTime".Translate()}:  ", ref asyncTime, order: ElementOrder.Right, disabled: asyncTimeLocked);
            entry = entry.Down(30);

            // Time control
            LeftLabel(entry, $"{"MpTimeControl".Translate()}:  ");
            DoTimeControl(entry.Right(LabelWidth + 10));

            entry = entry.Down(30);

            // Log desync traces
            MpUI.CheckboxLabeledWithTipNoHighlight(
                entry.Width(CheckboxWidth),
                $"{"MpLogDesyncTraces".Translate()}:  ",
                MpUtil.TranslateWithDoubleNewLines("MpLogDesyncTracesDesc", 2),
                ref serverSettings.desyncTraces,
                placeTextNearCheckbox: true
            );
            entry = entry.Down(30);

            // Arbiter
            if (MpVersion.IsDebug) {
                TooltipHandler.TipRegion(entry.Width(CheckboxWidth), "MpArbiterDesc".Translate());
                MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpRunArbiter".Translate()}:  ", ref serverSettings.arbiter, order: ElementOrder.Right);
                entry = entry.Down(30);
            }

            // Dev mode
            MpUI.CheckboxLabeledWithTipNoHighlight(
                entry.Width(CheckboxWidth),
                $"{"MpHostingDevMode".Translate()}:  ",
                MpUtil.TranslateWithDoubleNewLines("MpHostingDevModeDesc", 2),
                ref serverSettings.debugMode,
                placeTextNearCheckbox: true
            );

            // Dev mode scope
            if (serverSettings.debugMode)
                if (CustomButton(entry.Right(CheckboxWidth + 10f), $"MpHostingDevMode{serverSettings.devModeScope}".Translate()))
                {
                    serverSettings.devModeScope = serverSettings.devModeScope.Cycle();
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }

            entry = entry.Down(30);

            // Auto join-points
            DrawJoinPointOptions(entry);
            entry = entry.Down(30);

            // Pause on letter
            LeftLabel(entry, $"{"MpPauseOnLetter".Translate()}:  ");
            DoPauseOnLetter(entry.Right(LabelWidth + 10));
            entry = entry.Down(30);

            // Pause on (join, desync)
            LeftLabel(entry, $"{"MpPauseOn".Translate()}:  ");
            DoRow(
                entry.Right(LabelWidth + 10),
                rect => MpUI.CheckboxLabeled(
                    rect.Width(CheckboxWidth),
                    "MpPauseOnJoin".Translate(),
                    ref serverSettings.pauseOnJoin,
                    size: 20f,
                    order: ElementOrder.Left).width + 15,
                rect => MpUI.CheckboxLabeled(
                    rect.Width(CheckboxWidth),
                    "MpPauseOnDesync".Translate(),
                    ref serverSettings.pauseOnDesync,
                    size: 20f,
                    order: ElementOrder.Left).width
            );

            entry = entry.Down(30);
        }

        private void DoTimeControl(Rect entry)
        {
            if (CustomButton(entry, $"MpTimeControl{serverSettings.timeControl}".Translate()))
                Find.WindowStack.Add(new FloatMenu(Options().ToList()));

            IEnumerable<FloatMenuOption> Options()
            {
                foreach (var opt in Enum.GetValues(typeof(TimeControl)).OfType<TimeControl>())
                    yield return new FloatMenuOption($"MpTimeControl{opt}".Translate(), () =>
                    {
                        serverSettings.timeControl = opt;
                    });
            }
        }

        private void DoPauseOnLetter(Rect entry)
        {
            if (CustomButton(entry, $"MpPauseOnLetter{serverSettings.pauseOnLetter}".Translate()))
                Find.WindowStack.Add(new FloatMenu(Options().ToList()));

            IEnumerable<FloatMenuOption> Options()
            {
                foreach (var opt in Enum.GetValues(typeof(PauseOnLetter)).OfType<PauseOnLetter>())
                    yield return new FloatMenuOption($"MpPauseOnLetter{opt}".Translate(), () =>
                    {
                        serverSettings.pauseOnLetter = opt;
                    });
            }
        }

        static float LeftLabel(Rect entry, string text, string desc = null)
        {
            using (MpStyle.Set(TextAnchor.MiddleRight))
                MpUI.LabelWithTip(
                    entry.Width(LabelWidth + 1),
                    text,
                    desc
                );
            return Text.CalcSize(text).x;
        }

        static void DoRow(Rect inRect, params Func<Rect, float>[] drawers)
        {
            foreach (var drawer in drawers)
            {
                inRect.xMin += drawer(inRect);
            }
        }

        private static Color CustomButtonColor = new(0.15f, 0.15f, 0.15f);

        private void DrawJoinPointOptions(Rect entry)
        {
            LeftLabel(entry, $"{"MpAutoJoinPoints".Translate()}:  ", MpUtil.TranslateWithDoubleNewLines("MpAutoJoinPointsDesc", 3));

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
            => CustomButton(rect, label, out _);

        private static bool CustomButton(Rect rect, string label, out float width)
        {
            using var _ = MpStyle.Set(TextAnchor.MiddleLeft);
            var flagsWidth = Text.CalcSize(label).x;

            const float btnMargin = 5f;

            var flagsBtn = rect.Width(flagsWidth + btnMargin * 2);
            Widgets.DrawRectFast(flagsBtn.Height(24).Down(3), CustomButtonColor);
            Widgets.DrawHighlightIfMouseover(flagsBtn.Height(24).Down(3));
            MpUI.Label(rect.Right(btnMargin).Width(flagsWidth), label);

            width = flagsBtn.width;

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

            if (settings.hasPassword && settings.password.NullOrEmpty())
            {
                Messages.Message("MpInvalidGamePassword".Translate(), MessageTypeDefOf.RejectInput, false);
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
            localServer.liteNet.StartNet();

            var failed = false;

            if (settings.direct && localServer.liteNet.netManagers.Any(m => m.Item2.IsRunning is false))
            {
                foreach (var (endpoint, man) in localServer.liteNet.netManagers)
                    if (man.IsRunning is false)
                        Messages.Message("Failed to bind direct on " + endpoint, MessageTypeDefOf.RejectInput, false);
                failed = true;
            }

            if (settings.lan && !localServer.liteNet.lanManager.IsRunning)
            {
                Messages.Message("Failed to bind LAN on " + settings.lanAddress, MessageTypeDefOf.RejectInput, false);
                failed = true;
            }

            if (failed)
                localServer.liteNet.StopNet();
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
