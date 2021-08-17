using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using Verse;
using Multiplayer.Client.Desyncs;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class MainButtonsPatch
    {
        const float btnMargin = 8f;
        const float btnHeight = 27f;
        const float btnWidth = 80f;

        static bool Prefix()
        {
            Text.Font = GameFont.Small;

            if (MpVersion.IsDebug) {
                DoDebugInfo();
            }

            if (Multiplayer.IsReplay || TickPatch.Simulating)
            {
                DrawTimeline();
                DrawSimulatingWindow();
            }

            DoButtons();

            if (Multiplayer.Client != null
                && !Multiplayer.IsReplay
                && MultiplayerStatic.ToggleChatDef.KeyDownEvent)
            {
                Event.current.Use();

                if (ChatWindow.Opened != null)
                    ChatWindow.Opened.Close();
                else
                    ChatWindow.OpenChat();
            }

            return Find.Maps.Count > 0;
        }

        static void DoDebugInfo()
        {
            if (Multiplayer.ShowDevInfo)
            {
                int timerLag = (TickPatch.tickUntil - TickPatch.Timer);
                string text = $"{Faction.OfPlayer.loadID} {Multiplayer.RealPlayerFaction?.loadID} {Find.UniqueIDsManager.nextThingID} {Find.UniqueIDsManager.nextJobID} {Find.TickManager.TicksGame} {TickPatch.Timer} {TickPatch.tickUntil} {timerLag} {TickPatch.maxBehind} {Time.deltaTime * 60f} {Multiplayer.session?.localCmdId} {Multiplayer.session?.remoteCmdId} {Multiplayer.session?.remoteTickUntil}";
                Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
                Widgets.Label(rect, text);
            }

            if (Multiplayer.ShowDevInfo && Multiplayer.Client != null && Find.CurrentMap != null)
            {
                var async = Find.CurrentMap.AsyncTime();
                StringBuilder text = new StringBuilder();
                text.Append($"{Multiplayer.game.sync.knownClientOpinions.FirstOrDefault()?.isLocalClientsOpinion} {async.mapTicks}");

                text.Append($"z: {Find.CurrentMap.haulDestinationManager.AllHaulDestinationsListForReading.ToStringSafeEnumerable()} d: {Find.CurrentMap.designationManager.allDesignations.Count} hc: {Find.CurrentMap.listerHaulables.ThingsPotentiallyNeedingHauling().Count}");

                if (Find.CurrentMap.ParentFaction != null)
                {
                    int faction = Find.CurrentMap.ParentFaction.loadID;
                    MultiplayerMapComp comp = Find.CurrentMap.MpComp();
                    FactionMapData data = comp.factionData.GetValueSafe(faction);

                    if (data != null)
                    {
                        text.Append($" h: {data.listerHaulables.ThingsPotentiallyNeedingHauling().Count}");
                        text.Append($" sg: {data.haulDestinationManager.AllGroupsListForReading.Count}");
                    }
                }

                text.Append($" {Multiplayer.GlobalIdBlock.Current}");

                text.Append($"\n{SyncUtil.bufferedChanges.Sum(kv => kv.Value.Count)} {Find.UniqueIDsManager.nextThingID}");
                text.Append($"\n{DeferredStackTracing.acc}");
                text.Append($"\n{(uint)async.randState} {(uint)(async.randState >> 32)}");
                text.Append($"\n{(uint)Multiplayer.WorldComp.randState} {(uint)(Multiplayer.WorldComp.randState >> 32)}");
                text.Append($"\n{async.cmds.Count} {Multiplayer.WorldComp.cmds.Count} {async.slower.forceNormalSpeedUntil} {MultiplayerWorldComp.asyncTime}");
                text.Append($"\nt{DeferredStackTracing.maxTraceDepth} p{SimplePool<StackTraceLogItemRaw>.FreeItemsCount} {DeferredStackTracingImpl.hashtableEntries}/{DeferredStackTracingImpl.hashtableSize} {DeferredStackTracingImpl.collisions}");

                Rect rect1 = new Rect(80f, 110f, 330f, Text.CalcHeight(text.ToString(), 330f));
                Widgets.Label(rect1, text.ToString());
            }

            //if (Event.current.type == EventType.Repaint)
            //    RandGetValuePatch.tracesThistick = 0;
        }

        static void DoButtons()
        {
            float x = UI.screenWidth - btnWidth - btnMargin;
            float y = btnMargin;

            var session = Multiplayer.session;

            if (session != null && !Multiplayer.IsReplay)
            {
                var btnRect = new Rect(x, y, btnWidth, btnHeight);
                var chatColor = session.players.Any(p => p.status == PlayerStatus.Desynced) ? "#ff5555" : "#dddddd";
                var hasUnread = session.hasUnread ? "*" : "";
                var chatLabel = $"{"MpChatButton".Translate()} <color={chatColor}>({session.players.Count})</color>{hasUnread}";

                if (Widgets.ButtonText(btnRect, chatLabel))
                {
                    ChatWindow.OpenChat();
                }

                if (!TickPatch.Simulating)
                {
                    IndicatorInfo(out Color color, out string text, out bool slow);

                    var indRect = new Rect(btnRect.x - 25f - 5f + 6f / 2f, btnRect.y + 6f / 2f, 19f, 19f);
                    var biggerRect = new Rect(btnRect.x - 25f - 5f + 2f / 2f, btnRect.y + 2f / 2f, 23f, 23f);

                    if (slow && Widgets.ButtonInvisible(biggerRect))
                        TickPatch.SimulateTo(toTickUntil: true, canESC: true);

                    Widgets.DrawRectFast(biggerRect, new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f));
                    Widgets.DrawRectFast(indRect, color);
                    TooltipHandler.TipRegion(indRect, new TipSignal(text, 31641624));
                }

                y += btnHeight;
            }

            if (Multiplayer.ShowDevInfo && Multiplayer.WriterLog != null)
            {
                if (Widgets.ButtonText(new Rect(x, y, btnWidth, btnHeight), $"Write ({Multiplayer.WriterLog.NodeCount})"))
                    Find.WindowStack.Add(Multiplayer.WriterLog);

                y += btnHeight;
                if (Widgets.ButtonText(new Rect(x, y, btnWidth, btnHeight), $"Read ({Multiplayer.ReaderLog.NodeCount})"))
                    Find.WindowStack.Add(Multiplayer.ReaderLog);
                
                y += btnHeight;
            }

            if (Multiplayer.Client != null && Multiplayer.WorldComp.debugMode)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, y, btnWidth, 30f), $"Debug mode");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }

        static void IndicatorInfo(out Color color, out string text, out bool slow)
        {
            int behind = TickPatch.tickUntil - TickPatch.Timer;
            text = "MpTicksBehind".Translate(behind);
            slow = false;

            if (behind > 30)
            {
                color = new Color(0.9f, 0, 0);
                text += $"\n\n{"MpLowerGameSpeed".Translate()}\n{"MpForceCatchUp".Translate()}";
                slow = true;
            }
            else if (behind > 15)
            {
                color = Color.yellow;
            }
            else
            {
                color = new Color(0.0f, 0.8f, 0.0f);
            }
        }

        const float TimelineMargin = 50f;
        const float TimelineHeight = 35f;

        static void DrawTimeline()
        {
            Rect rect = new Rect(TimelineMargin, UI.screenHeight - 35f - TimelineHeight - 10f - 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight + 30f);
            Find.WindowStack.ImmediateWindow(TimelineWindowId, rect, WindowLayer.SubSuper, DrawTimelineWindow, doBackground: false, shadowAlpha: 0);
        }

        static void DrawTimelineWindow()
        {
            if (Multiplayer.Client == null) return;

            Rect rect = new Rect(0, 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight);

            Widgets.DrawBoxSolid(rect, new Color(0.6f, 0.6f, 0.6f, 0.8f));

            int timerStart = Multiplayer.session.replayTimerStart >= 0 ?
                Multiplayer.session.replayTimerStart : Multiplayer.session.cache.cachedAtTime;

            int timerEnd = Multiplayer.session.replayTimerEnd >= 0 ?
                Multiplayer.session.replayTimerEnd : TickPatch.tickUntil;

            int timeLen = timerEnd - timerStart;

            MpUtil.DrawRotatedLine(new Vector2(rect.xMin + 2f, rect.center.y), TimelineHeight, 20f, 90f, Color.white);
            MpUtil.DrawRotatedLine(new Vector2(rect.xMax - 2f, rect.center.y), TimelineHeight, 20f, 90f, Color.white);

            float progress = (TickPatch.Timer - timerStart) / (float)timeLen;
            float progressX = rect.xMin + progress * rect.width;
            MpUtil.DrawRotatedLine(new Vector2((int)progressX, rect.center.y), TimelineHeight, 20f, 90f, Color.green);

            float mouseX = Event.current.mousePosition.x;
            ReplayEvent mouseEvent = null;

            foreach (var ev in Multiplayer.session.events)
            {
                if (ev.time < timerStart || ev.time > timerEnd)
                    continue;

                var pointX = rect.xMin + (ev.time - timerStart) / (float)timeLen * rect.width;

                //GUI.DrawTexture(new Rect(pointX - 12f, rect.yMin - 24f, 24f, 24f), texture);
                MpUtil.DrawRotatedLine(new Vector2(pointX, rect.center.y), TimelineHeight, 20f, 90f, ev.color);

                if (Mouse.IsOver(rect) && Math.Abs(mouseX - pointX) < 10)
                {
                    mouseX = pointX;
                    mouseEvent = ev;
                }
            }

            if (Mouse.IsOver(rect))
            {
                float mouseProgress = (mouseX - rect.xMin) / rect.width;
                int mouseTimer = timerStart + (int)(timeLen * mouseProgress);

                MpUtil.DrawRotatedLine(new Vector2(mouseX, rect.center.y), TimelineHeight, 15f, 90f, Color.blue);

                if (Event.current.type == EventType.MouseUp)
                {
                    TickPatch.SimulateTo(mouseTimer, canESC: true);

                    if (mouseTimer < TickPatch.Timer)
                    {
                        ClientJoiningState.ReloadGame(Multiplayer.session.cache.mapData.Keys.ToList(), false);
                    }
                }

                if (Event.current.isMouse)
                    Event.current.Use();

                string tooltip = $"Tick {mouseTimer}";
                if (mouseEvent != null)
                    tooltip = $"{mouseEvent.name}\n{tooltip}";

                const int TickTipId = 215462143;

                TooltipHandler.TipRegion(rect, new TipSignal(tooltip, TickTipId));
                // Remove delay between the mouseover and showing
                if (TooltipHandler.activeTips.TryGetValue(TickTipId, out ActiveTip tip))
                    tip.firstTriggerTime = 0;
            }

            if (TickPatch.Simulating)
            {
                float pct = (TickPatch.simulating.target.Value - timerStart) / (float)timeLen;
                float simulateToX = rect.xMin + rect.width * pct;
                MpUtil.DrawRotatedLine(new Vector2(simulateToX, rect.center.y), TimelineHeight, 15f, 90f, Color.yellow);
            }
        }

        public const int SimulatingWindowId = 26461263;
        public const int TimelineWindowId = 5723681;

        static void DrawSimulatingWindow()
        {
            if (Multiplayer.Client == null || !TickPatch.Simulating) return;

            string text = $"{TickPatch.simulating.simTextKey.Translate()}{MpUtil.FixedEllipsis()}";
            float textWidth = Text.CalcSize(text).x;
            float windowWidth = Math.Max(240f, textWidth + 40f);
            float windowHeight = TickPatch.simulating.onCancel != null ? 100f : 75f;
            Rect rect = new Rect(0, 0, windowWidth, windowHeight).CenterOn(new Rect(0, 0, UI.screenWidth, UI.screenHeight));

            if (TickPatch.simulating.canESC && Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Escape)
            {
                TickPatch.ClearSimulating();
                Event.current.Use();
            }

            Find.WindowStack.ImmediateWindow(SimulatingWindowId, rect, WindowLayer.Super, () =>
            {
                if (!TickPatch.Simulating) return;

                var textRect = rect.AtZero();
                if (TickPatch.simulating.onCancel != null)
                {
                    textRect.yMin += 5f;
                    textRect.height -= 50f;
                }

                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                Widgets.Label(textRect, text);
                Text.Anchor = TextAnchor.UpperLeft;

                if (TickPatch.simulating.onCancel != null && Widgets.ButtonText(new Rect(0, textRect.yMax, 100f, 35f).CenteredOnXIn(textRect), TickPatch.simulating.cancelButtonKey.Translate()))
                    TickPatch.simulating.onCancel();
            }, absorbInputAroundWindow: true);
        }
    }

    [HarmonyPatch]
    static class MakeSpaceForReplayTimeline
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI));
            yield return AccessTools.Method(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI));
            yield return AccessTools.Method(typeof(WorldGlobalControls), nameof(WorldGlobalControls.WorldGlobalControlsOnGUI));
        }

        static void Prefix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight -= 60;
        }

        static void Postfix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight += 60;
        }
    }
}
