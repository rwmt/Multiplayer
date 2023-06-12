using System.Linq;
using System.Text;
using HarmonyLib;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class IngameDebug
{
    private static double avgDelta;
    private static double avgTickTime;

    public static float tps;
    private static float lastTicksAt;
    private static int lastTicks;

    private const float BtnMargin = 8f;
    private const float BtnHeight = 27f;
    private const float BtnWidth = 80f;

    internal static void DoDebugPrintout()
    {
        if (Multiplayer.ShowDevInfo)
        {
            int timerLag = (TickPatch.tickUntil - TickPatch.Timer);
            StringBuilder text = new StringBuilder();
            text.Append(
                $"{Faction.OfPlayer.loadID} {Multiplayer.RealPlayerFaction?.loadID} {Find.UniqueIDsManager.nextThingID} j:{Find.UniqueIDsManager.nextJobID} {Find.TickManager.TicksGame} {Find.TickManager.CurTimeSpeed} {TickPatch.Timer} {TickPatch.tickUntil} {timerLag}");
            text.Append($"\n{Time.deltaTime * 60f:0.0000} {TickPatch.tickTimer.ElapsedMilliseconds}");
            text.Append($"\n{avgDelta = (avgDelta * 59.0 + Time.deltaTime * 60.0) / 60.0:0.0000}");
            text.Append(
                $"\n{avgTickTime = (avgTickTime * 59.0 + TickPatch.tickTimer.ElapsedMilliseconds) / 60.0:0.0000} {Find.World.worldObjects.settlements.Count}");
            text.Append(
                $"\n{Multiplayer.session?.receivedCmds} {Multiplayer.session?.remoteSentCmds} {Multiplayer.session?.remoteTickUntil}");
            Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text.ToString(), 330f));
            Widgets.Label(rect, text.ToString());

            if (Input.GetKey(KeyCode.End))
            {
                avgDelta = 0;
                avgTickTime = 0;
            }
        }

        if (Multiplayer.ShowDevInfo && Multiplayer.Client != null && Find.CurrentMap != null)
        {
            var async = Find.CurrentMap.AsyncTime();
            StringBuilder text = new StringBuilder();
            text.Append(
                $"{Multiplayer.game.sync.knownClientOpinions.Count} {Multiplayer.game.sync.knownClientOpinions.FirstOrDefault()?.startTick} {async.mapTicks} {TickPatch.serverFrozen} {TickPatch.frozenAt} ");

            text.Append(
                $"z: {Find.CurrentMap.haulDestinationManager.AllHaulDestinationsListForReading.Count()} d: {Find.CurrentMap.designationManager.designationsByDef.Count} hc: {Find.CurrentMap.listerHaulables.ThingsPotentiallyNeedingHauling().Count}");

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

            text.Append(
                $" {Find.CurrentMap.Parent.IncidentTargetTags().ToStringSafeEnumerable()} {Find.IdeoManager.IdeosInViewOrder.FirstOrDefault()?.id}");

            text.Append(
                $"\n{SyncFieldUtil.bufferedChanges.Sum(kv => kv.Value.Count)} {Find.UniqueIDsManager.nextThingID}");
            text.Append(
                $"\n{DeferredStackTracing.acc} {MpInput.Mouse2UpWithoutDrag} {Input.GetKeyUp(KeyCode.Mouse2)} {Input.GetKey(KeyCode.Mouse2)}");
            text.Append($"\n{(uint)async.randState} {(uint)(async.randState >> 32)}");
            text.Append($"\n{(uint)Multiplayer.WorldTime.randState} {(uint)(Multiplayer.WorldTime.randState >> 32)}");
            text.Append(
                $"\n{async.cmds.Count} {Multiplayer.WorldTime.cmds.Count} {async.slower.forceNormalSpeedUntil} {Multiplayer.GameComp.asyncTime}");
            text.Append(
                $"\nt{DeferredStackTracing.maxTraceDepth} p{SimplePool<StackTraceLogItemRaw>.FreeItemsCount} {DeferredStackTracingImpl.hashtableEntries}/{DeferredStackTracingImpl.hashtableSize} {DeferredStackTracingImpl.collisions}");

            text.Append(Find.WindowStack.focusedWindow is ImmediateWindow win
                ? $"\nImmediateWindow: {MpUtil.DelegateMethodInfo(win.doWindowFunc?.Method)}"
                : $"\n{Find.WindowStack.focusedWindow}");

            text.Append($"\n{UI.CurUICellSize()} {Find.WindowStack.windows.ToStringSafeEnumerable()}");
            text.Append($"\n\nMap TPS: {tps:0.00}");
            text.Append($"\nDelta: {Time.deltaTime * 1000f}");
            text.Append($"\nAverage ft: {TickPatch.avgFrameTime}");
            text.Append($"\nServer tpt: {TickPatch.serverTimePerTick}");

            var calcStpt = TickPatch.tickUntil - TickPatch.Timer <= 3 ? TickPatch.serverTimePerTick * 1.2f :
                TickPatch.tickUntil - TickPatch.Timer >= 7 ? TickPatch.serverTimePerTick * 0.8f :
                TickPatch.serverTimePerTick;
            text.Append($"\nServer tpt: {calcStpt}");

            Rect rect1 = new Rect(80f, 170f, 330f, Text.CalcHeight(text.ToString(), 330f));
            Widgets.Label(rect1, text.ToString());

            if (Time.time - lastTicksAt > 0.5f)
            {
                tps = (tps + (async.mapTicks - lastTicks) * 2f) / 2f;
                lastTicks = async.mapTicks;
                lastTicksAt = Time.time;
            }
        }

        //if (Event.current.type == EventType.Repaint)
        //    RandGetValuePatch.tracesThistick = 0;
    }

    internal static float DoDevInfo(float y)
    {
        float x = UI.screenWidth - BtnWidth - BtnMargin;

        if (Multiplayer.ShowDevInfo && Multiplayer.WriterLog != null)
        {
            if (Widgets.ButtonText(new Rect(x, y, BtnWidth, BtnHeight), $"Write ({Multiplayer.WriterLog.NodeCount})"))
                Find.WindowStack.Add(Multiplayer.WriterLog);

            y += BtnHeight;
            if (Widgets.ButtonText(new Rect(x, y, BtnWidth, BtnHeight), $"Read ({Multiplayer.ReaderLog.NodeCount})"))
                Find.WindowStack.Add(Multiplayer.ReaderLog);

            y += BtnHeight;
            var oldGhostMode = Multiplayer.session.ghostModeCheckbox;
            Widgets.CheckboxLabeled(new Rect(x, y, BtnWidth, 30f), "Ghost", ref Multiplayer.session.ghostModeCheckbox);
            if (oldGhostMode != Multiplayer.session.ghostModeCheckbox)
            {
                SyncFieldUtil.ClearAllBufferedChanges();
            }

            return BtnHeight * 3;
        }

        return 0;
    }

    internal static float DoDebugModeLabel(float y)
    {
        float x = UI.screenWidth - BtnWidth - BtnMargin;

        if (Multiplayer.Client != null && Multiplayer.GameComp.debugMode)
        {
            using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(x, y, BtnWidth, 30f), $"Debug mode");

            return BtnHeight;
        }

        return 0;
    }
}
