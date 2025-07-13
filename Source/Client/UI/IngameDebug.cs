using System.Linq;
using System.Text;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class IngameDebug
{
    private static double avgDelta;
    private static double avgTickTime;

    private const float BtnMargin = 8f;
    private const float BtnHeight = 27f;
    private const float BtnWidth = 80f;
    private const string separator = "     ";

    internal static void DoDebugPrintout()
    {
        if (Multiplayer.ShowDevInfo)
        {
            int timerLag = (TickPatch.tickUntil - TickPatch.Timer);
            StringBuilder text = new StringBuilder();

            // Core game state and timing info
            text.AppendLine($"Timer: {TickPatch.Timer}");
            text.AppendLine($"Timer Lag: {timerLag}");
            text.AppendLine($"Current FPS: {1f / Time.deltaTime:0.0}");
            text.AppendLine($"Tick Time: {TickPatch.tickTimer.ElapsedMilliseconds}ms{separator}Avg: {avgTickTime = (avgTickTime * 59.0 + TickPatch.tickTimer.ElapsedMilliseconds) / 60.0:0.0000}ms");
            text.AppendLine($"Avg Delta: {avgDelta = (avgDelta * 59.0 + Time.deltaTime * 60.0) / 60.0:0.0000}");
            text.AppendLine($"Game Ticks: {Find.TickManager.TicksGame}");
            text.AppendLine($"Time Speed: {Find.TickManager.CurTimeSpeed}");
            text.AppendLine($"Tick Until: {TickPatch.tickUntil}{separator}Remote: {Multiplayer.session?.remoteTickUntil ?? 0}");
            text.AppendLine($"Received Commands: {Multiplayer.session?.receivedCmds ?? 0}");
            text.AppendLine($"Sent Commands: {Multiplayer.session?.remoteSentCmds ?? 0}");

            text.AppendLine($"\nFaction ID: {Faction.OfPlayer.loadID} ({FactionContext.stack.Count}){separator}Real Faction ID: {Multiplayer.RealPlayerFaction?.loadID ?? -1}");
            text.AppendLine($"Next Thing ID: {Find.UniqueIDsManager.nextThingID}{separator}Next Job ID: {Find.UniqueIDsManager.nextJobID}");
            text.AppendLine($"Faction Stack: {FactionContext.stack.Count}{separator}World Settlements: {Find.World.worldObjects.settlements.Count}");

            // Add map-specific info if available
            if (Multiplayer.Client != null && Find.CurrentMap != null)
            {
                var async = Find.CurrentMap.AsyncTime();

                text.AppendLine($"\nMap TPS: {IngameUIPatch.tps:0.00}");
                text.AppendLine($"Frame Time: {Time.deltaTime * 1000f:0.0}ms{separator}Avg: {TickPatch.avgFrameTime:0.0}ms");
                text.AppendLine($"Server TPT: {TickPatch.serverTimePerTick:0.0}ms");
                text.AppendLine($"Calculated TPT: {(TickPatch.tickUntil - TickPatch.Timer <= 3 ? TickPatch.serverTimePerTick * 1.2f : TickPatch.tickUntil - TickPatch.Timer >= 7 ? TickPatch.serverTimePerTick * 0.8f : TickPatch.serverTimePerTick):0.0}ms");
                text.AppendLine($"Map Ticks: {async.mapTicks}{separator}Frozen: {TickPatch.serverFrozen} @ {TickPatch.frozenAt}");
                text.AppendLine($"Client Opinions: {Multiplayer.game.sync.knownClientOpinions.Count}{separator}Opinion Start Tick: {Multiplayer.game.sync.knownClientOpinions.FirstOrDefault()?.startTick ?? 0}");
                text.AppendLine($"Opinion Start Tick: {Multiplayer.game.sync.knownClientOpinions.FirstOrDefault()?.startTick ?? 0}");
                text.AppendLine($"Force Normal Speed Until: {async.slower.forceNormalSpeedUntil}");
                text.AppendLine($"Async Time Enabled: {Multiplayer.GameComp.asyncTime}");

                text.AppendLine($"\nBuffered Changes: {SyncFieldUtil.bufferedChanges.Sum(kv => kv.Value.Count)}");
                text.AppendLine($"Map Cmds: {async.cmds.Count}{separator} World Cmds: {Multiplayer.AsyncWorldTime.cmds.Count}");
                text.AppendLine($"Stack Trace: {DeferredStackTracing.acc}{separator} Max Depth: {DeferredStackTracing.maxTraceDepth}");
                text.AppendLine($"Hash: {DeferredStackTracingImpl.hashtableEntries}/{DeferredStackTracingImpl.hashtableSize} ({DeferredStackTracingImpl.collisions} collisions)");

                text.AppendLine($"\nIdeology: {Find.IdeoManager.classicMode}{separator} Ideo ID: {Find.IdeoManager.IdeosInViewOrder.FirstOrDefault()?.id ?? 0}");
                text.AppendLine($"Haul Dest: {Find.CurrentMap.haulDestinationManager.AllHaulDestinationsListForReading.Count}{separator} Designations: {Find.CurrentMap.designationManager.designationsByDef.Count}");
                text.AppendLine($"Haulables: {Find.CurrentMap.listerHaulables.ThingsPotentiallyNeedingHauling().Count}{separator} Mines: {Find.CurrentMap.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine).Count()}");

                if (Find.CurrentMap.ParentFaction != null)
                {
                    var comp = Find.CurrentMap.MpComp();
                    var data = comp.factionData.TryGetValue(Find.CurrentMap.ParentFaction.loadID);
                    if (data != null)
                        text.AppendLine($"Faction Haul: {data.listerHaulables.ThingsPotentiallyNeedingHauling().Count}{separator}Groups: {data.haulDestinationManager.AllGroupsListForReading.Count}");
                }
                text.AppendLine($"World Pawns: {Find.WorldPawns.AllPawnsAliveOrDead.Count}{separator}Pool: {SimplePool<StackTraceLogItemRaw>.FreeItemsCount}");

                text.AppendLine($"\nMap RNG: {(uint)async.randState:X8} {(uint)(async.randState >> 32):X8}");
                text.AppendLine($"World RNG: {(uint)Multiplayer.AsyncWorldTime.randState:X8} {(uint)(Multiplayer.AsyncWorldTime.randState >> 32):X8}");
                text.AppendLine($"Mouse2: {MpInput.Mouse2UpWithoutDrag}   Up: {Input.GetKeyUp(KeyCode.Mouse2)}   Held: {Input.GetKey(KeyCode.Mouse2)}");
                text.AppendLine($"Cell Size: {UI.CurUICellSize()}{separator} Windows: {Find.WindowStack.windows.Count}");

                string focusedWin = Find.WindowStack.focusedWindow is ImmediateWindow win ?
                    $"Immediate: {MpUtil.DelegateMethodInfo(win.doWindowFunc?.Method)}" :
                    Find.WindowStack.focusedWindow?.ToString() ?? "None";
                text.AppendLine($"Focus: {focusedWin}");
            }

            text.AppendLine("\n[Hold END to reset averages]");

            // Calculate appropriate width based on content
            float contentWidth = Text.CalcSize(text.ToString()).x + 20f; // Add some padding
            float maxWidth = UI.screenWidth * 0.4f; // Don't take more than 40% of screen width
            float finalWidth = Mathf.Min(contentWidth, maxWidth);

            Rect rect = new Rect(80f, 60f, finalWidth, Text.CalcHeight(text.ToString(), finalWidth));

            // Apply transparency to text
            if (!Mouse.IsOver(rect))
            {
                GUI.contentColor = new Color(1, 1, 1, 0.8f);
            }

            Widgets.Label(rect, text.ToString());

            // Reset GUI color
            GUI.contentColor = Color.white;

            if (Input.GetKey(KeyCode.End))
            {
                avgDelta = 0;
                avgTickTime = 0;
            }
        }
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

    internal static float DoTimeDiffLabel(float y)
    {
        float x = UI.screenWidth - BtnWidth - BtnMargin;

        if (MpVersion.IsDebug &&
            Multiplayer.Client != null &&
            !Multiplayer.GameComp.asyncTime &&
            Find.CurrentMap.AsyncTime() != null &&
            Find.CurrentMap.AsyncTime().mapTicks != Multiplayer.AsyncWorldTime.worldTicks)
        {
            using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(x, y, BtnWidth, 30f), $"{Find.CurrentMap.AsyncTime().mapTicks - Multiplayer.AsyncWorldTime.worldTicks}");

            return BtnHeight;
        }

        return 0;
    }
}
