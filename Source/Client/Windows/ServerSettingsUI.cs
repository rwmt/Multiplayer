using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace Multiplayer.Client
{
    /// <summary>
    /// Shared UI components for drawing ServerSettings fields.
    /// Used by both HostWindow and BootstrapConfiguratorWindow.
    /// </summary>
    public static class ServerSettingsUI
    {
        private const float LabelWidth = 110f;
        private const float CheckboxWidth = LabelWidth + 30f;
        private static Color CustomButtonColor = new(0.15f, 0.15f, 0.15f);

        // Buffer references - caller must manage these
        public class BufferSet
        {
            public string MaxPlayersBuffer;
            public string AutosaveBuffer;
        }

        /// <summary>
        /// Draw networking-related settings (max players, password, direct/LAN/steam, sync configs).
        /// </summary>
        public static void DrawNetworkingSettings(Rect entry, ServerSettings settings, BufferSet buffers)
        {
            // Max players
            MpUI.TextFieldNumericLabeled(entry.Width(LabelWidth + 35f), $"{"MpMaxPlayers".Translate()}:  ", ref settings.maxPlayers, ref buffers.MaxPlayersBuffer, LabelWidth, 0, 999);
            entry = entry.Down(30);

            // Password
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpHostGamePassword".Translate()}:  ", ref settings.hasPassword, order: ElementOrder.Right);
            if (settings.hasPassword)
                MpUI.DoPasswordField(entry.Right(CheckboxWidth + 10).MaxX(entry.xMax), "PasswordField", ref settings.password);
            entry = entry.Down(30);

            // Direct hosting
            var directLabel = $"{"MpHostDirect".Translate()}:  ";
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), directLabel, ref settings.direct, order: ElementOrder.Right);
            TooltipHandler.TipRegion(entry.Width(LabelWidth), MpUtil.TranslateWithDoubleNewLines("MpHostDirectDesc", 4));
            if (settings.direct)
                settings.directAddress = Widgets.TextField(entry.Right(CheckboxWidth + 10).MaxX(entry.xMax), settings.directAddress);

            entry = entry.Down(30);

            // LAN hosting
            var lanRect = entry.Width(CheckboxWidth);
            MpUI.CheckboxLabeled(lanRect, $"{"MpLan".Translate()}:  ", ref settings.lan, order: ElementOrder.Right);
            TooltipHandler.TipRegion(lanRect, $"{"MpLanDesc1".Translate()}\n\n{"MpLanDesc2".Translate(settings.lanAddress)}");

            entry = entry.Down(30);

            // Steam hosting
            var steamRect = entry.Width(CheckboxWidth);
            if (!SteamManager.Initialized) settings.steam = false;
            MpUI.CheckboxLabeled(steamRect, $"{"MpSteam".Translate()}:  ", ref settings.steam, order: ElementOrder.Right, disabled: !SteamManager.Initialized);
            if (!SteamManager.Initialized) TooltipHandler.TipRegion(steamRect, "MpSteamNotAvailable".Translate());
            entry = entry.Down(30);

            // Sync configs
            TooltipHandler.TipRegion(entry.Width(CheckboxWidth), MpUtil.TranslateWithDoubleNewLines("MpSyncConfigsDescNew", 3));
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpSyncConfigs".Translate()}:  ", ref settings.syncConfigs, order: ElementOrder.Right);
        }

        /// <summary>
        /// Draw gameplay-related settings (autosave, multifaction, async time, time control, etc.).
        /// </summary>
        public static void DrawGameplaySettings(Rect entry, ServerSettings settings, BufferSet buffers, bool asyncTimeLocked = false, bool multifactionLocked = false)
        {
            // Autosave interval
            var autosaveUnitKey = settings.autosaveUnit == AutosaveUnit.Days
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
                            ref settings.autosaveInterval,
                            ref buffers.AutosaveBuffer,
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
                settings.autosaveUnit = settings.autosaveUnit.Cycle();
                settings.autosaveInterval *=
                    settings.autosaveUnit == AutosaveUnit.Minutes ?
                        8f : // Days to minutes
                        0.125f; // Minutes to days
                buffers.AutosaveBuffer = settings.autosaveInterval.ToString();
            }

            entry = entry.Down(30);

            // Multifaction
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"Multifaction:  ", ref settings.multifaction, order: ElementOrder.Right, disabled: multifactionLocked);
            entry = entry.Down(30);

            // Async time
            TooltipHandler.TipRegion(entry.Width(CheckboxWidth), $"{"MpAsyncTimeDesc".Translate()}\n\n{"MpExperimentalFeature".Translate()}");
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpAsyncTime".Translate()}:  ", ref settings.asyncTime, order: ElementOrder.Right, disabled: asyncTimeLocked);
            entry = entry.Down(30);

            // Time control
            LeftLabel(entry, $"{"MpTimeControl".Translate()}:  ");
            DoTimeControl(entry.Right(LabelWidth + 10), settings);

            entry = entry.Down(30);

            // Log desync traces
            MpUI.CheckboxLabeledWithTipNoHighlight(
                entry.Width(CheckboxWidth),
                $"{"MpLogDesyncTraces".Translate()}:  ",
                MpUtil.TranslateWithDoubleNewLines("MpLogDesyncTracesDesc", 2),
                ref settings.desyncTraces,
                placeTextNearCheckbox: true
            );
            entry = entry.Down(30);

            // Arbiter (debug only)
            if (MpVersion.IsDebug)
            {
                TooltipHandler.TipRegion(entry.Width(CheckboxWidth), "MpArbiterDesc".Translate());
                MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpRunArbiter".Translate()}:  ", ref settings.arbiter, order: ElementOrder.Right);
                entry = entry.Down(30);
            }

            // Dev mode
            MpUI.CheckboxLabeledWithTipNoHighlight(
                entry.Width(CheckboxWidth),
                $"{"MpHostingDevMode".Translate()}:  ",
                MpUtil.TranslateWithDoubleNewLines("MpHostingDevModeDesc", 2),
                ref settings.debugMode,
                placeTextNearCheckbox: true
            );

            // Dev mode scope
            if (settings.debugMode)
                if (CustomButton(entry.Right(CheckboxWidth + 10f), $"MpHostingDevMode{settings.devModeScope}".Translate()))
                {
                    settings.devModeScope = settings.devModeScope.Cycle();
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }

            entry = entry.Down(30);

            // Auto join-points
            DrawJoinPointOptions(entry, settings);
            entry = entry.Down(30);

            // Pause on letter
            LeftLabel(entry, $"{"MpPauseOnLetter".Translate()}:  ");
            DoPauseOnLetter(entry.Right(LabelWidth + 10), settings);
            entry = entry.Down(30);

            // Pause on (join, desync)
            LeftLabel(entry, $"{"MpPauseOn".Translate()}:  ");
            DoRow(
                entry.Right(LabelWidth + 10),
                rect => MpUI.CheckboxLabeled(
                    rect.Width(CheckboxWidth),
                    "MpPauseOnJoin".Translate(),
                    ref settings.pauseOnJoin,
                    size: 20f,
                    order: ElementOrder.Left).width + 15,
                rect => MpUI.CheckboxLabeled(
                    rect.Width(CheckboxWidth),
                    "MpPauseOnDesync".Translate(),
                    ref settings.pauseOnDesync,
                    size: 20f,
                    order: ElementOrder.Left).width
            );
        }

        /// <summary>
        /// Draw just the gameplay settings (no networking) - useful when networking is in a separate context.
        /// </summary>
        public static void DrawGameplaySettingsOnly(Rect entry, ServerSettings settings, BufferSet buffers, bool asyncTimeLocked = false, bool multifactionLocked = false)
        {
            DrawGameplaySettings(entry, settings, buffers, asyncTimeLocked, multifactionLocked);
        }

        // Helper methods

        private static void DoTimeControl(Rect entry, ServerSettings settings)
        {
            if (CustomButton(entry, $"MpTimeControl{settings.timeControl}".Translate()))
                Find.WindowStack.Add(new FloatMenu(Options(settings).ToList()));

            IEnumerable<FloatMenuOption> Options(ServerSettings s)
            {
                foreach (var opt in Enum.GetValues(typeof(TimeControl)).OfType<TimeControl>())
                    yield return new FloatMenuOption($"MpTimeControl{opt}".Translate(), () =>
                    {
                        s.timeControl = opt;
                    });
            }
        }

        private static void DoPauseOnLetter(Rect entry, ServerSettings settings)
        {
            if (CustomButton(entry, $"MpPauseOnLetter{settings.pauseOnLetter}".Translate()))
                Find.WindowStack.Add(new FloatMenu(Options(settings).ToList()));

            IEnumerable<FloatMenuOption> Options(ServerSettings s)
            {
                foreach (var opt in Enum.GetValues(typeof(PauseOnLetter)).OfType<PauseOnLetter>())
                    yield return new FloatMenuOption($"MpPauseOnLetter{opt}".Translate(), () =>
                    {
                        s.pauseOnLetter = opt;
                    });
            }
        }

        private static void DrawJoinPointOptions(Rect entry, ServerSettings settings)
        {
            LeftLabel(entry, $"{"MpAutoJoinPoints".Translate()}:  ", MpUtil.TranslateWithDoubleNewLines("MpAutoJoinPointsDesc", 3));

            var flags = Enum.GetValues(typeof(AutoJoinPointFlags))
                .OfType<AutoJoinPointFlags>()
                .Where(f => settings.autoJoinPoint.HasFlag(f))
                .Select(f => $"MpAutoJoinPoints{f}".Translate())
                .Join(", ");
            if (flags.Length == 0) flags = "Off";

            if (CustomButton(entry.Right(LabelWidth + 10), flags))
                Find.WindowStack.Add(new FloatMenu(Flags(settings).ToList()));

            IEnumerable<FloatMenuOption> Flags(ServerSettings s)
            {
                foreach (var flag in Enum.GetValues(typeof(AutoJoinPointFlags)).OfType<AutoJoinPointFlags>())
                    yield return new FloatMenuOption($"MpAutoJoinPoints{flag}".Translate(), () =>
                    {
                        if (s.autoJoinPoint.HasFlag(flag))
                            s.autoJoinPoint &= ~flag;
                        else
                            s.autoJoinPoint |= flag;
                    });
            }
        }

        private static float LeftLabel(Rect entry, string text, string desc = null)
        {
            using (MpStyle.Set(TextAnchor.MiddleRight))
                MpUI.LabelWithTip(
                    entry.Width(LabelWidth + 1),
                    text,
                    desc
                );
            return Text.CalcSize(text).x;
        }

        private static void DoRow(Rect inRect, params Func<Rect, float>[] drawers)
        {
            foreach (var drawer in drawers)
            {
                inRect.xMin += drawer(inRect);
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
    }
}
