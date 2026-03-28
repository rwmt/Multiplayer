using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace Multiplayer.Client;

public static class ServerSettingsUI
{
    private const float LabelWidth = 110f;
    private const float CheckboxWidth = LabelWidth + 30f;
    private static readonly Color CustomButtonColor = new(0.15f, 0.15f, 0.15f);

    public class BufferSet
    {
        public string MaxPlayersBuffer;
        public string AutosaveBuffer;
    }

    public static void DrawNetworkingSettings(Rect entry, ServerSettings settings, BufferSet buffers)
    {
        MpUI.TextFieldNumericLabeled(entry.Width(LabelWidth + 35f), $"{"MpMaxPlayers".Translate()}:  ", ref settings.maxPlayers,
            ref buffers.MaxPlayersBuffer, LabelWidth, 0, 999);
        entry = entry.Down(30);

        MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpHostGamePassword".Translate()}:  ", ref settings.hasPassword,
            order: ElementOrder.Right);
        if (settings.hasPassword)
            MpUI.DoPasswordField(entry.Right(CheckboxWidth + 10).MaxX(entry.xMax), "PasswordField", ref settings.password);
        entry = entry.Down(30);

        var directLabel = $"{"MpHostDirect".Translate()}:  ";
        MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), directLabel, ref settings.direct, order: ElementOrder.Right);
        TooltipHandler.TipRegion(entry.Width(LabelWidth), MpUtil.TranslateWithDoubleNewLines("MpHostDirectDesc", 4));
        if (settings.direct)
            settings.directAddress = Widgets.TextField(entry.Right(CheckboxWidth + 10).MaxX(entry.xMax), settings.directAddress);

        entry = entry.Down(30);

        var lanRect = entry.Width(CheckboxWidth);
        MpUI.CheckboxLabeled(lanRect, $"{"MpLan".Translate()}:  ", ref settings.lan, order: ElementOrder.Right);
        TooltipHandler.TipRegion(lanRect, $"{"MpLanDesc1".Translate()}\n\n{"MpLanDesc2".Translate(settings.lanAddress)}");

        entry = entry.Down(30);

        var steamRect = entry.Width(CheckboxWidth);
        if (!SteamManager.Initialized) settings.steam = false;
        MpUI.CheckboxLabeled(steamRect, $"{"MpSteam".Translate()}:  ", ref settings.steam, order: ElementOrder.Right,
            disabled: !SteamManager.Initialized);
        if (!SteamManager.Initialized)
            TooltipHandler.TipRegion(steamRect, "MpSteamNotAvailable".Translate());
        entry = entry.Down(30);

        TooltipHandler.TipRegion(entry.Width(CheckboxWidth), MpUtil.TranslateWithDoubleNewLines("MpSyncConfigsDescNew", 3));
        MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpSyncConfigs".Translate()}:  ", ref settings.syncConfigs,
            order: ElementOrder.Right);
    }

    public static void DrawGameplaySettingsOnly(Rect entry, ServerSettings settings, BufferSet buffers) =>
        DrawGameplaySettings(entry, settings, buffers);

    public static void DrawGameplaySettings(Rect entry, ServerSettings settings, BufferSet buffers,
        bool asyncTimeLocked = false, bool multifactionLocked = false)
    {
        var autosaveUnitKey = settings.autosaveUnit == AutosaveUnit.Days ? "MpAutosavesDays" : "MpAutosavesMinutes";
        var changeAutosaveUnit = false;

        LeftLabel(entry, $"{"MpAutosaves".Translate()}:  ");
        TooltipHandler.TipRegion(entry.Width(LabelWidth), MpUtil.TranslateWithDoubleNewLines("MpAutosavesDesc", 3));

        using (MpStyle.Set(TextAnchor.MiddleRight))
            DoRow(
                entry.Right(LabelWidth + 10),
                rect => MpUI.LabelFlexibleWidth(rect, "MpAutosavesEvery".Translate()) + 6,
                rect =>
                {
                    Widgets.TextFieldNumeric(rect.Width(50f), ref settings.autosaveInterval, ref buffers.AutosaveBuffer, 0, 999);
                    return 56f;
                },
                rect =>
                {
                    changeAutosaveUnit = CustomButton(rect, autosaveUnitKey.Translate(), out var width);
                    return width;
                });

        if (changeAutosaveUnit)
        {
            settings.autosaveUnit = settings.autosaveUnit.Cycle();
            settings.autosaveInterval *= settings.autosaveUnit == AutosaveUnit.Minutes ? 8f : 0.125f;
            buffers.AutosaveBuffer = settings.autosaveInterval.ToString();
        }

        entry = entry.Down(30);

        MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), "Multifaction:  ", ref settings.multifaction,
            order: ElementOrder.Right, disabled: multifactionLocked);
        entry = entry.Down(30);

        TooltipHandler.TipRegion(entry.Width(CheckboxWidth), $"{"MpAsyncTimeDesc".Translate()}\n\n{"MpExperimentalFeature".Translate()}");
        MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpAsyncTime".Translate()}:  ", ref settings.asyncTime,
            order: ElementOrder.Right, disabled: asyncTimeLocked);
        entry = entry.Down(30);

        LeftLabel(entry, $"{"MpTimeControl".Translate()}:  ");
        DoTimeControl(entry.Right(LabelWidth + 10), settings);
        entry = entry.Down(30);

        MpUI.CheckboxLabeledWithTipNoHighlight(entry.Width(CheckboxWidth), $"{"MpLogDesyncTraces".Translate()}:  ",
            MpUtil.TranslateWithDoubleNewLines("MpLogDesyncTracesDesc", 2), ref settings.desyncTraces,
            placeTextNearCheckbox: true);
        entry = entry.Down(30);

        if (MpVersion.IsDebug)
        {
            TooltipHandler.TipRegion(entry.Width(CheckboxWidth), "MpArbiterDesc".Translate());
            MpUI.CheckboxLabeled(entry.Width(CheckboxWidth), $"{"MpRunArbiter".Translate()}:  ", ref settings.arbiter,
                order: ElementOrder.Right);
            entry = entry.Down(30);
        }

        MpUI.CheckboxLabeledWithTipNoHighlight(entry.Width(CheckboxWidth), $"{"MpHostingDevMode".Translate()}:  ",
            MpUtil.TranslateWithDoubleNewLines("MpHostingDevModeDesc", 2), ref settings.debugMode,
            placeTextNearCheckbox: true);

        if (settings.debugMode && CustomButton(entry.Right(CheckboxWidth + 10f), $"MpHostingDevMode{settings.devModeScope}".Translate()))
        {
            settings.devModeScope = settings.devModeScope.Cycle();
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }

        entry = entry.Down(30);

        DrawJoinPointOptions(entry, settings);
        entry = entry.Down(30);

        LeftLabel(entry, $"{"MpPauseOnLetter".Translate()}:  ");
        DoPauseOnLetter(entry.Right(LabelWidth + 10), settings);
        entry = entry.Down(30);

        LeftLabel(entry, $"{"MpPauseOn".Translate()}:  ");
        DoRow(
            entry.Right(LabelWidth + 10),
            rect => MpUI.CheckboxLabeled(rect.Width(CheckboxWidth), "MpPauseOnJoin".Translate(), ref settings.pauseOnJoin,
                size: 20f, order: ElementOrder.Left).width + 15,
            rect => MpUI.CheckboxLabeled(rect.Width(CheckboxWidth), "MpPauseOnDesync".Translate(), ref settings.pauseOnDesync,
                size: 20f, order: ElementOrder.Left).width);
    }

    private static void DoTimeControl(Rect entry, ServerSettings settings)
    {
        if (CustomButton(entry, $"MpTimeControl{settings.timeControl}".Translate()))
            Find.WindowStack.Add(new FloatMenu(Options(settings).ToList()));

        IEnumerable<FloatMenuOption> Options(ServerSettings source)
        {
            foreach (var opt in Enum.GetValues(typeof(TimeControl)).OfType<TimeControl>())
                yield return new FloatMenuOption($"MpTimeControl{opt}".Translate(), () => source.timeControl = opt);
        }
    }

    private static void DoPauseOnLetter(Rect entry, ServerSettings settings)
    {
        if (CustomButton(entry, $"MpPauseOnLetter{settings.pauseOnLetter}".Translate()))
            Find.WindowStack.Add(new FloatMenu(Options(settings).ToList()));

        IEnumerable<FloatMenuOption> Options(ServerSettings source)
        {
            foreach (var opt in Enum.GetValues(typeof(PauseOnLetter)).OfType<PauseOnLetter>())
                yield return new FloatMenuOption($"MpPauseOnLetter{opt}".Translate(), () => source.pauseOnLetter = opt);
        }
    }

    private static void DrawJoinPointOptions(Rect entry, ServerSettings settings)
    {
        LeftLabel(entry, $"{"MpAutoJoinPoints".Translate()}:  ", MpUtil.TranslateWithDoubleNewLines("MpAutoJoinPointsDesc", 3));

        var flags = Enum.GetValues(typeof(AutoJoinPointFlags))
            .OfType<AutoJoinPointFlags>()
            .Where(flag => settings.autoJoinPoint.HasFlag(flag))
            .Select(flag => $"MpAutoJoinPoints{flag}".Translate())
            .Join(", ");
        if (flags.Length == 0)
            flags = "Off";

        if (CustomButton(entry.Right(LabelWidth + 10), flags))
            Find.WindowStack.Add(new FloatMenu(FlagOptions(settings).ToList()));

        IEnumerable<FloatMenuOption> FlagOptions(ServerSettings source)
        {
            foreach (var flag in Enum.GetValues(typeof(AutoJoinPointFlags)).OfType<AutoJoinPointFlags>())
                yield return new FloatMenuOption($"MpAutoJoinPoints{flag}".Translate(), () =>
                {
                    if (source.autoJoinPoint.HasFlag(flag))
                        source.autoJoinPoint &= ~flag;
                    else
                        source.autoJoinPoint |= flag;
                });
        }
    }

    private static float LeftLabel(Rect entry, string text, string desc = null)
    {
        using (MpStyle.Set(TextAnchor.MiddleRight))
            MpUI.LabelWithTip(entry.Width(LabelWidth + 1), text, desc);
        return Text.CalcSize(text).x;
    }

    private static void DoRow(Rect inRect, params Func<Rect, float>[] drawers)
    {
        foreach (var drawer in drawers)
            inRect.xMin += drawer(inRect);
    }

    private static bool CustomButton(Rect rect, string label) => CustomButton(rect, label, out _);

    private static bool CustomButton(Rect rect, string label, out float width)
    {
        using var _ = MpStyle.Set(TextAnchor.MiddleLeft);
        var labelWidth = Text.CalcSize(label).x;
        const float btnMargin = 5f;

        var buttonRect = rect.Width(labelWidth + btnMargin * 2);
        Widgets.DrawRectFast(buttonRect.Height(24).Down(3), CustomButtonColor);
        Widgets.DrawHighlightIfMouseover(buttonRect.Height(24).Down(3));
        MpUI.Label(rect.Right(btnMargin).Width(labelWidth), label);

        width = buttonRect.width;
        return Widgets.ButtonInvisible(buttonRect);
    }
}