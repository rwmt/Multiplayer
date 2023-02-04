using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client.AsyncTime;


[HarmonyPatch(typeof(GlobalControlsUtility), nameof(GlobalControlsUtility.DoTimespeedControls))]
public static class TimeControlPatch
{
    private static TimeVote[] GameSpeeds = { TimeVote.Paused, TimeVote.Normal, TimeVote.Fast, TimeVote.Superfast };

    private static bool ShouldReset => Event.current.shift && Multiplayer.GameComp.IsLowestWins;

    private static ITickable Tickable =>
        !WorldRendererUtility.WorldRenderedNow && Multiplayer.GameComp.asyncTime
            ? Find.CurrentMap.AsyncTime()
            : Multiplayer.WorldComp;

    private static TimeVote CurTimeSpeedGame =>
        (TimeVote)(Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : Tickable.TimeSpeed);

    private static TimeVote? CurTimeSpeedUI =>
        Multiplayer.IsReplay ? (TimeVote?)TickPatch.replayTimeSpeed :
        Multiplayer.GameComp.IsLowestWins ? Multiplayer.GameComp.LocalPlayerDataOrNull?.GetTimeVoteOrNull(Tickable.TickableId) : (TimeVote?)Tickable.TimeSpeed;

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
    {
        foreach (var inst in insts)
        {
            if (inst.operand == AccessTools.Method(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI)))
                inst.operand = AccessTools.Method(typeof(TimeControlPatch), nameof(DoTimeControlsGUI));

            yield return inst;

            if (inst.operand == AccessTools.Constructor(typeof(Rect),
                    new[] { typeof(float), typeof(float), typeof(float), typeof(float) }))
            {
                yield return new CodeInstruction(OpCodes.Ldloca_S, 1);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TimeControlPatch), nameof(ModifyRect)));
            }
        }
    }

    static void ModifyRect(ref Rect timerRect)
    {
        // Make space for time speed vote summary
        if (Multiplayer.Client != null && Multiplayer.GameComp.IsLowestWins)
            timerRect.yMin -= TimeControls.TimeButSize.y;
    }

    // Based on TimeControls.DoTimeControlsGUI
    private static void DoTimeControlsGUI(Rect timerRect)
    {
        if (Multiplayer.Client == null)
        {
            TimeControls.DoTimeControlsGUI(timerRect);
            return;
        }

        Widgets.BeginGroup(timerRect);
        Rect rect = new Rect(0f, 0f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);

        if (AnyVotes(Tickable.TickableId))
        {
            var descRect = rect.Down(TimeControls.TimeButSize.y).Width(timerRect.width);

            Widgets.DrawHighlightIfMouseover(descRect);
            TooltipHandler.TipRegion(descRect, new TipSignal(
                MpUtil.TranslateWithDoubleNewLines("MpLowestWinsButtonsDesc", 3) + VoteCountDetailed(),
                483922
            ));

            if (Widgets.ButtonInvisible(descRect, false) && ShouldReset && Multiplayer.LocalServer != null)
                SendTimeVote(TimeVote.Reset);
        }

        foreach (var speed in GameSpeeds)
        {
            if (Widgets.ButtonImage(rect, TexButton.SpeedButtonTextures[(uint)speed]))
            {
                // todo Move the host check to the server?
                if (ShouldReset && Multiplayer.LocalServer != null)
                    SendTimeVote(TimeVote.Reset);
                else if (speed == TimeVote.Paused)
                    SendTimeVote(TogglePaused(CurTimeSpeedUI));
                else
                    SendTimeVote(speed);
            }

            if (speed == CurTimeSpeedGame)
                GUI.DrawTexture(rect, TexUI.HighlightTex);

            if (Multiplayer.GameComp.IsLowestWins)
                using (MpStyle.Set(TextAnchor.MiddleCenter))
                    Widgets.Label(rect.Down(TimeControls.TimeButSize.y), VoteCountLabel(speed));

            rect.x += rect.width;
        }

        float normalSpeed = Tickable.ActualRateMultiplier(TimeSpeed.Normal);
        float fastSpeed = Tickable.ActualRateMultiplier(TimeSpeed.Fast);

        if (normalSpeed == 0f) // Completely paused
            Widgets.DrawLineHorizontal(rect.width, rect.height / 2f, rect.width * 3f);
        else if (normalSpeed == fastSpeed) // Slowed down
            Widgets.DrawLineHorizontal(rect.width * 2f, rect.height / 2f, rect.width * 2f);

        Widgets.EndGroup();
        GenUI.AbsorbClicksInRect(timerRect);
        UIHighlighter.HighlightOpportunity(timerRect, "TimeControls");

        if (Event.current.type != EventType.KeyDown)
            return;

        // Prevent multiple players changing the speed too quickly
        if (Time.realtimeSinceStartup - MultiplayerWorldComp.lastSpeedChange < 0.4f)
            return;

        if (KeyBindingDefOf.TogglePause.KeyDownEvent)
        {
            SendTimeVote(ShouldReset ? TimeVote.PlayerReset : TogglePaused(CurTimeSpeedUI));
            if (ShouldReset)
                prePauseTimeSpeed = null;
        }

        if (!Find.WindowStack.WindowsForcePause)
        {
            if (KeyBindingDefOf.TimeSpeed_Normal.KeyDownEvent)
                SendTimeVote(TimeVote.Normal);

            if (KeyBindingDefOf.TimeSpeed_Fast.KeyDownEvent)
                SendTimeVote(TimeVote.Fast);

            if (KeyBindingDefOf.TimeSpeed_Superfast.KeyDownEvent)
                SendTimeVote(TimeVote.Superfast);

            if (KeyBindingDefOf.TimeSpeed_Slower.KeyDownEvent && CurTimeSpeedUI != null && CurTimeSpeedUI != TimeVote.Paused)
                SendTimeVote(CurTimeSpeedUI.Value - 1);

            if (KeyBindingDefOf.TimeSpeed_Faster.KeyDownEvent && CurTimeSpeedUI != null && (TimeSpeed)CurTimeSpeedUI < TimeSpeed.Superfast)
                SendTimeVote(CurTimeSpeedUI.Value + 1);
        }

        if (Prefs.DevMode)
        {
            if (KeyBindingDefOf.TimeSpeed_Ultrafast.KeyDownEvent)
                SendTimeVote(TimeVote.Ultrafast);

            // Only for replaying
            if (KeyBindingDefOf.Dev_TickOnce.KeyDownEvent && CurTimeSpeedGame == TimeVote.Paused)
            {
                DoSingleTick();
                SoundDefOf.Clock_Stop.PlayOneShotOnCamera();
            }
        }
    }

    static bool AnyVotes(int tickableId)
    {
        return Multiplayer.GameComp.playerData.Values.Any(p => p.GetTimeVoteOrNull(tickableId) != null);
    }

    static IEnumerable<string> PlayerVotes(int tickableId, TimeVote vote)
    {
        return Multiplayer.GameComp.playerData
            .Where(p => p.Value.GetTimeVoteOrNull(Tickable.TickableId) == vote)
            .Select(p => Multiplayer.session.GetPlayerInfo(p.Key)?.username)
            .AllNotNull();
    }

    static string VoteCountLabel(TimeVote speed)
    {
        int votes = PlayerVotes(Tickable.TickableId, speed).Count();
        if (votes == 0) return "";
        return (CurTimeSpeedUI == speed ? $"{votes} ▲" : $"{votes}");
    }

    static string VoteCountDetailed()
    {
        if (!AnyVotes(Tickable.TickableId)) return "";
        var result = "\n";
        for (var speed = TimeVote.Paused; speed <= TimeVote.Ultrafast; speed++)
        {
            var players = PlayerVotes(Tickable.TickableId, speed).JoinStringsAtMost();
            if (!players.NullOrEmpty())
                result += $"\n{speed}: {players}";
        }
        return result;
    }

    public static TimeVote? prePauseTimeSpeed;

    private static TimeVote TogglePaused(TimeVote? fromSpeed)
    {
        if (fromSpeed == null)
        {
            if (CurTimeSpeedGame != TimeVote.Paused)
            {
                prePauseTimeSpeed = CurTimeSpeedGame;
                return TimeVote.Paused;
            }

            var lowestNotPaused = Multiplayer.GameComp.GetLowestTimeVote(Tickable.TickableId, true);
            if (lowestNotPaused != TimeSpeed.Paused)
                return (TimeVote)lowestNotPaused;
        }

        if (fromSpeed != null && fromSpeed != TimeVote.Paused)
        {
            prePauseTimeSpeed = fromSpeed;
            return TimeVote.Paused;
        }

        if (prePauseTimeSpeed != null && prePauseTimeSpeed != fromSpeed)
            return prePauseTimeSpeed.Value;

        return TimeVote.Normal;
    }

    private static void SendTimeVote(TimeVote vote)
    {
        if (Multiplayer.IsReplay)
            TickPatch.replayTimeSpeed = (TimeSpeed)vote;

        if (vote != CurTimeSpeedUI)
            if (Multiplayer.GameComp.IsLowestWins)
                Multiplayer.Client.SendCommand(CommandType.TimeSpeedVote, ScheduledCommand.Global, (byte)vote, Tickable.TickableId);
            else
                MpTimeControls.SendTimeChange(Tickable, (TimeSpeed)vote);

        if (Event.current.type == EventType.KeyDown)
            Event.current.Use();

        TimeControls.PlaySoundOf(vote >= TimeVote.PlayerReset ? TimeSpeed.Paused : (TimeSpeed)vote);
    }

    private static void DoSingleTick()
    {
        if (TickPatch.Timer < TickPatch.tickUntil)
        {
            var replaySpeed = TickPatch.replayTimeSpeed;
            TickPatch.replayTimeSpeed = TimeSpeed.Normal;
            TickPatch.accumulator = 1;

            TickPatch.Tick(out _);

            TickPatch.accumulator = 0;
            TickPatch.replayTimeSpeed = replaySpeed;
        }
    }
}

[HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ShowGroupFrames), MethodType.Getter)]
static class AlwaysShowColonistBarFrames
{
    static void Postfix(ref bool __result)
    {
        if (Multiplayer.Client == null) return;
        __result = true;
    }
}


[HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
public static class ColonistBarTimeControl
{
    public static Color normalBgColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
    public static Color pauseBgColor = new Color(1f, 0.5f, 0.5f, 0.4f);
    public static float btnWidth = TimeControls.TimeButSize.x;
    public static float btnHeight = TimeControls.TimeButSize.y;

    static void Prefix(ref bool __state)
    {
        if (Event.current.type is EventType.MouseDown or EventType.MouseUp)
        {
            DrawButtons();
            __state = true;
        }
    }

    static void Postfix(bool __state)
    {
        if (!__state)
            DrawButtons();
    }

    static void DrawButtons()
    {
        if (Multiplayer.Client == null) return;

        ColonistBar bar = Find.ColonistBar;
        if (bar.Entries.Count == 0) return;

        int curGroup = -1;
        foreach (var entry in bar.Entries)
        {
            if (curGroup == entry.group) continue;

            ITickable entryTickable = entry.map?.AsyncTime();
            if (entryTickable == null) entryTickable = Multiplayer.WorldComp;

            Rect groupBar = bar.drawer.GroupFrameRect(entry.group);
            float drawXPos = groupBar.x;
            Color bgColor = (entryTickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f) ? pauseBgColor : normalBgColor;

            if (Multiplayer.GameComp.asyncTime)
            {
                Rect button = new Rect(drawXPos, groupBar.yMax, btnWidth, btnHeight);

                if (entry.map != null)
                {
                    MpTimeControls.TimeControlButton(button, bgColor, entryTickable);
                    drawXPos += TimeControls.TimeButSize.x;
                }
                else if (entryTickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f)
                {
                    MpTimeControls.TimeIndicateBlockingPause(button, bgColor);
                    drawXPos += TimeControls.TimeButSize.x;
                }
            }
            else if (entryTickable.TickRateMultiplier(TimeSpeed.Normal) == 0f)
            {
                Rect button = new Rect(drawXPos, groupBar.yMax, btnWidth, btnHeight);
                MpTimeControls.TimeIndicateBlockingPause(button, bgColor);
                drawXPos += TimeControls.TimeButSize.x;
            }

            List<FloatMenuOption> options = GetBlockingWindowOptions(entry, entryTickable);
            if (!options.NullOrEmpty())
                DrawWindowShortcuts(new Rect(drawXPos, groupBar.yMax, 70, btnHeight), bgColor, options);

            curGroup = entry.group;
        }
    }

    static void DrawWindowShortcuts(Rect button, Color bgColor, List<FloatMenuOption> options)
    {
        Widgets.DrawRectFast(button, bgColor);

        using (MpStyle.Set(GameFont.Tiny))
            if (Widgets.ButtonText(button, "MpDialogsButton".Translate()))
                Find.WindowStack.Add(new FloatMenu(options));
    }

    static List<FloatMenuOption> GetBlockingWindowOptions(ColonistBar.Entry entry, ITickable tickable)
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>();
        var split = Multiplayer.WorldComp.splitSession;

        if (split != null && split.Caravan.pawns.Contains(entry.pawn))
        {
            options.Add(new FloatMenuOption("MpCaravanSplittingSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                CameraJumper.TryJumpAndSelect(entry.pawn);
                Multiplayer.WorldComp.splitSession.OpenWindow();
            }));
        }

        if (Multiplayer.WorldComp.trading.FirstOrDefault(t => t.playerNegotiator?.Map == entry.map) is { } trade)
        {
            options.Add(new FloatMenuOption("MpTradingSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                CameraJumper.TryJumpAndSelect(trade.playerNegotiator);
                Find.WindowStack.Add(new TradingWindow()
                    { selectedTab = Multiplayer.WorldComp.trading.IndexOf(trade) });
            }));
        }

        if (entry.map?.MpComp().transporterLoading != null)
        {
            options.Add(new FloatMenuOption("MpTransportLoadingSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                entry.map.MpComp().transporterLoading.OpenWindow();
            }));
        }

        if (entry.map?.MpComp().caravanForming != null)
        {
            options.Add(new FloatMenuOption("MpCaravanFormingSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                entry.map.MpComp().caravanForming.OpenWindow();
            }));
        }

        if (entry.map?.MpComp().ritualSession != null)
        {
            options.Add(new FloatMenuOption("MpRitualSession".Translate(), () =>
            {
                SwitchToMapOrWorld(entry.map);
                entry.map.MpComp().ritualSession.OpenWindow();
            }));
        }

        return options;
    }

    static void SwitchToMapOrWorld(Map map)
    {
        if (map == null)
        {
            Find.World.renderer.wantedMode = WorldRenderMode.Planet;
        }
        else
        {
            if (WorldRendererUtility.WorldRenderedNow) CameraJumper.TryHideWorld();
            Current.Game.CurrentMap = map;
        }
    }
}

[HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.DoButton))]
static class MainButtonWorldTimeControl
{
    static void Prefix(MainButtonWorker __instance, Rect rect, ref Rect? __state)
    {
        if (Multiplayer.Client == null) return;
        if (__instance.def != MainButtonDefOf.World) return;
        if (__instance.Disabled) return;
        if (Find.CurrentMap == null) return;
        if (!Multiplayer.GameComp.asyncTime) return;

        Rect button = new Rect(rect.xMax - TimeControls.TimeButSize.x - 5f,
            rect.y + (rect.height - TimeControls.TimeButSize.y) / 2f, TimeControls.TimeButSize.x,
            TimeControls.TimeButSize.y);
        __state = button;

        if (Event.current.type is EventType.MouseDown or EventType.MouseUp)
            MpTimeControls.TimeControlButton(__state.Value, ColonistBarTimeControl.normalBgColor, Multiplayer.WorldComp);
    }

    static void Postfix(MainButtonWorker __instance, Rect? __state)
    {
        if (__state == null) return;

        if (Event.current.type == EventType.Repaint)
            MpTimeControls.TimeControlButton(__state.Value, ColonistBarTimeControl.normalBgColor, Multiplayer.WorldComp);
    }
}

static class MpTimeControls
{
    public static void TimeIndicateBlockingPause(Rect button, Color bgColor)
    {
        Widgets.DrawRectFast(button, bgColor);
        Widgets.ButtonImage(button, TexButton.SpeedButtonTextures[0], doMouseoverSound: false);
    }

    public static void TimeControlButton(Rect button, Color bgColor, ITickable tickable)
    {
        int speed = (int)tickable.TimeSpeed;
        if (tickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f)
            speed = 0;

        Widgets.DrawRectFast(button, bgColor);
        if (Widgets.ButtonImage(button, TexButton.SpeedButtonTextures[speed]))
        {
            int dir = Event.current.button == 0 ? 1 : -1;
            SendTimeChange(tickable, (TimeSpeed)GenMath.PositiveMod(speed + dir, (int)TimeSpeed.Ultrafast));
            Event.current.Use();
        }
    }

    public static void SendTimeChange(ITickable tickable, TimeSpeed newSpeed)
    {
        if (tickable is MultiplayerWorldComp)
            Multiplayer.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)newSpeed);
        else if (tickable is AsyncTimeComp comp)
            Multiplayer.Client.SendCommand(CommandType.MapTimeSpeed, comp.map.uniqueID, (byte)newSpeed);
    }
}
