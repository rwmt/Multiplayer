using System;
using System.Linq;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Util;
using Multiplayer.Client.Patches;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic; // Added for List

namespace Multiplayer.Client.DebugUi
{
    /// <summary>
    /// Enhanced expandable debug panel that organizes existing comprehensive debug information
    /// with modern UI/UX while preserving all developer functionality
    /// </summary>
    public static partial class SyncDebugPanel
    {
        // Panel state
        private static bool isExpanded = false;
        private static Vector2 scrollPosition = Vector2.zero;

        // Panel dimensions
        private const float HeaderHeight = 40f;
        private const float VisibleExpandedHeight = 400f;
        private const float ContentHeight = 1500f;
        private const float PanelWidth = 275f;

        // Visual constants
        private const float Margin = 8f;
        private const float StatusBadgePadding = 4f;
        private const float SectionSpacing = 10f;
        private const float LineHeight = 18f;
        private const float LabelColumnWidth = 0.5f;

        /// <summary>
        /// Main entry point for the enhanced debug panel
        /// </summary>
        public static float DoSyncDebugPanel(float y)
        {
            // Safety checks
            if (Multiplayer.session == null || !MpVersion.IsDebug || !Multiplayer.ShowDevInfo || Multiplayer.WriterLog == null) return 0;

            try
            {
                float x = 100f;
                float panelHeight = isExpanded ? VisibleExpandedHeight : HeaderHeight;

                Rect panelRect = new(x, y, PanelWidth, panelHeight);

                // Draw panel background
                Widgets.DrawBoxSolid(panelRect, new Color(0f, 0f, 0f, 0.7f));
                Widgets.DrawBox(panelRect);

                if (isExpanded)
                    DrawExpandedPanel(panelRect);
                else
                    DrawHeader(panelRect);

                return panelHeight + Margin;
            }
            catch (Exception ex)
            {
                // Fallback in case of any errors - don't crash the game
                Log.Error($"SyncDebugPanel error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Draw the compact status summary view
        /// </summary>
        private static void DrawHeader(Rect rect)
        {
            // Draw border around entire header for expanded mode only
            if (isExpanded)
                Widgets.DrawBox(rect);

            Rect contentRect = rect.ContractedBy(Margin);

            try
            {
                // Draw compact status indicators with text values
                float currentX = contentRect.x + 2f;
                float centerY = contentRect.y + (contentRect.height / 2f);

                // Draw all status badges
                StatusBadge[] statusBadges =
                [
                    StatusBadge.GetSyncStatus(),
                    StatusBadge.GetPerformanceStatus(),
                    StatusBadge.GetVtrStatus(),
                    StatusBadge.GetNumOfPlayersStatus(),
                    StatusBadge.GetTickStatus()
                ];

                foreach (StatusBadge status in statusBadges)
                {
                    currentX = DrawCompactStatusBadge(currentX, centerY, status);
                    currentX += StatusBadgePadding;
                }

                // Button (expand/collapse)
                float buttonWidth = 20f;
                Rect buttonRect = new(contentRect.xMax - buttonWidth - StatusBadgePadding, contentRect.y + (contentRect.height - 16f) / 2f, buttonWidth, 16f);

                // Draw button as a badge (no border for consistency)
                Widgets.DrawBoxSolid(buttonRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));

                using (MpStyle.Set(GameFont.Tiny).Set(Color.white).Set(TextAnchor.MiddleCenter))
                {
                    if (Widgets.ButtonText(buttonRect, isExpanded ? "^" : "v"))
                    {
                        isExpanded = !isExpanded;
                    }
                }

                // Click anywhere else to expand (only in compact mode)
                if (!isExpanded && Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 &&
                    contentRect.Contains(Event.current.mousePosition) &&
                    !buttonRect.Contains(Event.current.mousePosition))
                {
                    isExpanded = !isExpanded;
                    Event.current.Use();
                }
            }
            catch (Exception ex)
            {
                // Fallback display
                using (MpStyle.Set(GameFont.Tiny).Set(Color.red).Set(TextAnchor.MiddleLeft))
                {
                    Widgets.Label(contentRect, $"Debug Panel Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Draw a compact status badge with icon and text [🟢 SYNC]
        /// </summary>
        private static float DrawCompactStatusBadge(float x, float centerY, StatusBadge status)
        {
            // Calculate badge dimensions
            string badgeText = $"{status.icon} {status.text}";
            float textWidth = Text.CalcSize(badgeText).x + 8f; // padding
            float badgeHeight = 16f;

            Rect badgeRect = new(x, centerY - badgeHeight / 2f, textWidth, badgeHeight);

            // Draw badge background (no border for cleaner look)
            Widgets.DrawBoxSolid(badgeRect, new Color(0.1f, 0.1f, 0.1f, 0.7f));

            // Draw badge text
            using (MpStyle.Set(GameFont.Tiny).Set(status.color).Set(TextAnchor.MiddleCenter))
            {
                Widgets.Label(badgeRect, badgeText);
            }

            // Add tooltip
            if (!string.IsNullOrEmpty(status.tooltip))
            {
                TooltipHandler.TipRegion(badgeRect, status.tooltip);
            }

            return x + textWidth;
        }

        /// <summary>
        /// Draw the expanded comprehensive debug view
        /// </summary>
        private static void DrawExpandedPanel(Rect rect)
        {
            Rect headerRect = new(rect.x, rect.y, rect.width, 30f);
            Rect contentRect = new(rect.x, rect.y + 30f, rect.width, rect.height - 30f);

            DrawHeader(headerRect);
            Rect viewRect = new(0f, 0f, contentRect.width - 16f, ContentHeight);
            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);

            float currentY = 0f;

            // Draw the content
            currentY += DrawStatusSummarySection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawRngStatesSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawPerformanceSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawPerformanceRecorderSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawNetworkSyncSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawCoreSystemSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawTimingSyncSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawGameStateSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawRngDebugSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawCommandSyncSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawMemoryPerformanceSection(viewRect.x + Margin, currentY, viewRect.width - Margin);
            currentY += DrawMapManagementSection(viewRect.x + Margin, currentY, viewRect.width - Margin);

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Generic section drawing method
        /// </summary>
        private static float DrawSection(float x, float y, float width, DebugSection section)
        {
            float startY = y;

            y = DrawSectionHeader(x, y, width, section.Title);

            foreach (DebugLine line in section.Lines)
            {
                y = DrawStatusLine(x, y, width, line.Label, line.Value, line.Color);
            }

            return y - startY + SectionSpacing;
        }

        /// <summary>
        /// Draw status summary section with key indicators
        /// </summary>
        private static float DrawStatusSummarySection(float x, float y, float width)
        {
            StatusBadge syncStatus = StatusBadge.GetSyncStatus();
            StatusBadge performanceStatus = StatusBadge.GetPerformanceStatus();
            StatusBadge vtrStatus = StatusBadge.GetVtrStatus();
            StatusBadge tickStatus = StatusBadge.GetTickStatus();

            DebugLine[] lines = [
                new("Sync Status:", syncStatus.text, syncStatus.color),
                new("Performance:", performanceStatus.text, performanceStatus.color),
                new("VTR Status:", vtrStatus.text, vtrStatus.color),
                new("Tick Status:", tickStatus.text, tickStatus.color)
            ];

            DebugSection section = new("STATUS SUMMARY", lines);
            return DrawSection(x, y, width, section);
        }

        /// <summary>
        /// Draw performance recorder section with controls
        /// </summary>
        private static float DrawPerformanceRecorderSection(float x, float y, float width)
        {
            StatusBadge recordingStatus = PerformanceRecorder.GetRecordingStatus();
            
            var lines = new List<DebugLine>
            {
                new("Status:", recordingStatus.text, recordingStatus.color),
                new("Frame Count:", PerformanceRecorder.FrameCount.ToString(), Color.white)
            };

            if (PerformanceRecorder.IsRecording)
            {
                var duration = PerformanceRecorder.RecordingDuration;
                lines.Add(new("Duration:", $"{duration.TotalSeconds:F1}s", Color.white));
                lines.Add(new("Avg FPS:", PerformanceRecorder.AverageFPS > 0 ? $"{PerformanceRecorder.AverageFPS:F1}" : "N/A", Color.white));
                lines.Add(new("Avg TPS:", PerformanceRecorder.AverageTPS > 0 ? $"{PerformanceRecorder.AverageTPS:F1}" : "N/A", Color.white));
                
                if (PerformanceCalculator.IsInStabilizationPeriod())
                {
                    lines.Add(new("TPS Perf:", "STABILIZING", Color.yellow));
                }
                else
                {
                    lines.Add(new("TPS Perf:", PerformanceRecorder.AverageNormalizedTPS > 0 ? $"{PerformanceRecorder.AverageNormalizedTPS:F1}%" : "N/A", Color.white));
                }
            }

            var section = new DebugSection("PERFORMANCE RECORDER", lines.ToArray());
            float sectionHeight = DrawSection(x, y, width, section);
            
            // Add control buttons below the section
            float buttonY = y + sectionHeight - SectionSpacing + 4f;
            float buttonHeight = DrawRecorderControls(x, buttonY, width);
            
            return sectionHeight + buttonHeight;
        }

        /// <summary>
        /// Draw performance recorder control buttons and settings
        /// </summary>
        private static float DrawRecorderControls(float x, float y, float width)
        {
            var buttonWidth = 60f;
            var buttonHeight = 20f;
            var spacing = 4f;
            var lineHeight = 18f;
            float currentY = y;

            if (!PerformanceRecorder.IsRecording)
            {
                using (MpStyle.Set(GameFont.Tiny).Set(Color.white).Set(TextAnchor.MiddleLeft))
                {
                    float sampleCount = PerformanceRecorder.MaxSampleCountSetting;
                    var sampleCountLabelRect = new Rect(x, currentY, width, lineHeight);
                    Widgets.Label(sampleCountLabelRect, $"Max samples: {sampleCount}");
                    currentY+= lineHeight + 1;

                    var sampleCountSliderRect = new Rect(x, currentY, width, lineHeight);
                    sampleCount = Widgets.HorizontalSlider(sampleCountSliderRect, sampleCount, 1000f, 500000f, roundTo: 0);
                    PerformanceRecorder.MaxSampleCountSetting = (int)sampleCount;
                    currentY += lineHeight + 1;

                    float nonPerfInterval = PerformanceRecorder.NonPerfFrameIntervalSetting;
                    var nonPerfLabelRect = new Rect(x, currentY, width, lineHeight);
                    Widgets.Label(nonPerfLabelRect, $"Non-performance metric sample interval: {nonPerfInterval}");
                    currentY += lineHeight + 1;

                    var nonPerfSliderRect = new Rect(x, currentY, width, lineHeight);
                    nonPerfInterval = Widgets.HorizontalSlider(nonPerfSliderRect, nonPerfInterval, 1f, 300f, roundTo: 0);
                    PerformanceRecorder.NonPerfFrameIntervalSetting = (int)nonPerfInterval;
                    currentY += lineHeight + 1;
                }
            }

            var startRect = new Rect(x, currentY, buttonWidth, buttonHeight);
            var stopRect = new Rect(x + buttonWidth + spacing, currentY, buttonWidth, buttonHeight);
            
            GUI.color = PerformanceRecorder.IsRecording ? Color.gray : Color.white;
            
            if (Widgets.ButtonText(startRect, "Start") && !PerformanceRecorder.IsRecording)
            {
                PerformanceRecorder.StartRecording();
            }

            GUI.color = !PerformanceRecorder.IsRecording ? Color.gray : Color.white;
            
            if (Widgets.ButtonText(stopRect, "Stop") && PerformanceRecorder.IsRecording)
            {
                PerformanceRecorder.StopRecording();
            }
            
            GUI.color = Color.white;
            currentY += buttonHeight + spacing;
            return currentY - y;
        }

        /// <summary>
        /// Draw RNG states comparison section
        /// </summary>
        private static float DrawRngStatesSection(float x, float y, float width)
        {
            List<DebugLine> lines;

            try
            {
                AsyncTimeComp async = Find.CurrentMap?.AsyncTime();
                AsyncTime.AsyncWorldTimeComp worldAsync = Multiplayer.AsyncWorldTime;

                if (async != null && worldAsync != null)
                {
                    lines = [
                        new("Current Map:", GetRngStatesString(async.randState), Color.white),
                        new("World RNG:", GetRngStatesString(worldAsync.randState), Color.white),
                        new("Round Mode:", $"{RoundMode.GetCurrentRoundMode()}", Color.white)
                    ];
                }
                else
                {
                    lines = [new("RNG States:", "No async time available", Color.gray)];
                }
            }
            catch (Exception ex)
            {
                lines = [new("RNG States:", $"Error: {ex.Message}", Color.red)];
            }

            return DrawSection(x, y, width, new("RNG STATES", lines.ToArray()));
        }

        private static string GetRngStatesString(ulong randState) => $"{(uint)(randState >> 32):X8} | {(uint)randState:X8}";

        /// <summary>
        /// Draw performance metrics section
        /// </summary>
        private static float DrawPerformanceSection(float x, float y, float width)
        {
            // TPS
            float tps = IngameUIPatch.tps;
            float targetTps = PerformanceCalculator.GetTargetTPS();
            string tpsPerformanceText;
            Color tpsColor;
            
            if (PerformanceCalculator.IsInStabilizationPeriod())
            {
                tpsPerformanceText = "STABILIZING";
                tpsColor = Color.yellow;
            }
            else
            {
                float normalizedTps = PerformanceCalculator.GetNormalizedTPS(tps);
                tpsPerformanceText = $"{normalizedTps:F1}%";
                tpsColor = PerformanceCalculator.GetPerformanceColor(normalizedTps, 90f, 70f);
            }

            // Frame time
            float frameTime = Time.deltaTime * 1000f;
            Color frameColor = PerformanceCalculator.GetPerformanceColor(frameTime, 20f, 35f, true);

            DebugLine[] lines = [
                new("Map TPS:", $"{tps:F1}", Color.white),
                new("Target TPS:", $"{targetTps:F0}", Color.gray),
                new("TPS Perf:", tpsPerformanceText, tpsColor),
                new("Frame Time:", $"{frameTime:F1}ms", frameColor),
                new("Server TPT:", $"{TickPatch.serverTimePerTick:F1}ms", Color.white),
                new("Avg Frame:", $"{TickPatch.avgFrameTime:F1}ms", Color.white)
            ];

            return DrawSection(x, y, width, new("PERFORMANCE", lines));
        }

        /// <summary>
        /// Draw network and sync status section
        /// </summary>
        private static float DrawNetworkSyncSection(float x, float y, float width)
        {
            DebugLine[] lines;

            if (Multiplayer.session != null)
            {
                // Player count
                int playerCount = Multiplayer.session.players.Count;
                bool hasDesynced = Multiplayer.session.players.Any(p => p.status == PlayerStatus.Desynced);
                Color playerColor = hasDesynced ? Color.red : Color.green;

                // Server status
                string serverStatus = TickPatch.serverFrozen ? "Frozen" : "Running";
                Color serverColor = TickPatch.serverFrozen ? Color.yellow : Color.green;

                lines = [
                    new("Players:", $"{playerCount}", playerColor),
                    new("Received Cmds:", $"{Multiplayer.session.receivedCmds}", Color.white),
                    new("Remote Sent:", $"{Multiplayer.session.remoteSentCmds}", Color.white),
                    new("Remote Tick:", $"{Multiplayer.session.remoteTickUntil}", Color.white),
                    new("Server:", serverStatus, serverColor),
                ];
            }
            else
            {
                lines = [(new("Network:", "No active session", Color.gray))];
            }

            return DrawSection(x, y, width, new("NETWORK & SYNC", lines));
        }

        /// <summary>
        /// Draw core system data section
        /// </summary>
        private static float DrawCoreSystemSection(float x, float y, float width)
        {
            try
            {
                DebugLine[] coreLines = [
                    new("Faction Stack:", $"{FactionContext.stack?.Count ?? 0}", Color.white),
                    new("Player Faction:", $"{Faction.OfPlayer?.loadID ?? -1}", Color.white),
                    new("Real Player:", $"{Multiplayer.RealPlayerFaction?.loadID ?? -1}", Color.white),
                    new("Next Thing ID:", $"{Find.UniqueIDsManager?.nextThingID ?? -1}", Color.white),
                    new("Next Job ID:", $"{Find.UniqueIDsManager?.nextJobID ?? -1}", Color.white),
                    new("Game Ticks:", $"{Find.TickManager?.TicksGame ?? -1}", Color.white),
                    new("Time Speed:", $"{Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Paused}", Color.white)
                ];

                return DrawSection(x, y, width, new("CORE SYSTEM", coreLines));
            }
            catch (Exception ex)
            {
                DebugLine[] errorLines = [new("Core System:", $"Error: {ex.Message}", Color.red)];
                return DrawSection(x, y, width, new("CORE SYSTEM", errorLines));
            }
        }

        /// <summary>
        /// Draw timing and sync data section
        /// </summary>
        private static float DrawTimingSyncSection(float x, float y, float width)
        {
            try
            {
                int timerLag = TickPatch.tickUntil - TickPatch.Timer;
                Color lagColor = PerformanceCalculator.GetPerformanceColor(timerLag, 15, 30);
                
                DebugLine[] timingLines = [
                    new("Timer Lag:", $"{timerLag}", lagColor),
                    new("Timer:", $"{TickPatch.Timer}", Color.white),
                    new("Tick Until:", $"{TickPatch.tickUntil}", Color.white),
                    new("Raw Tick Timer:", $"{TickPatch.tickTimer?.ElapsedMilliseconds ?? 0}ms", Color.white),
                    new("World Settlements:", $"{Find.World?.worldObjects?.settlements?.Count ?? 0}", Color.white)
                ];

                return DrawSection(x, y, width, new("TIMING & SYNC", timingLines));
            }
            catch (Exception ex)
            {
                DebugLine[] errorLines = [new("Timing Sync:", $"Error: {ex.Message}", Color.red)];
                return DrawSection(x, y, width, new("TIMING & SYNC", errorLines));
            }
        }

        /// <summary>
        /// Draw game state data section
        /// </summary>
        private static float DrawGameStateSection(float x, float y, float width)
        {
            try
            {
                AsyncTimeComp async = Find.CurrentMap?.AsyncTime();
                
                DebugLine[] gameStateLines = [
                    new("Classic Mode:", $"{Find.IdeoManager?.classicMode ?? false}", Color.white),
                    new("Client Opinions:", $"{Multiplayer.game?.sync?.knownClientOpinions?.Count ?? 0}", Color.white),
                    new("First Opinion Tick:", $"{Multiplayer.game?.sync?.knownClientOpinions?.FirstOrDefault()?.startTick ?? -1}", Color.white),
                    new("Map Ticks:", $"{async?.mapTicks ?? -1}", Color.white),
                    new("Frozen At:", $"{TickPatch.frozenAt}", Color.white)
                ];

                return DrawSection(x, y, width, new("GAME STATE", gameStateLines));
            }
            catch (Exception ex)
            {
                DebugLine[] errorLines = [new("Game State:", $"Error: {ex.Message}", Color.red)];
                return DrawSection(x, y, width, new("GAME STATE", errorLines));
            }
        }

        /// <summary>
        /// Draw RNG and debug data section
        /// </summary>
        private static float DrawRngDebugSection(float x, float y, float width)
        {
            DebugLine[] rngLines = [
                new("Rand Calls:", $"{DeferredStackTracing.acc}", Color.white),
                new("Max Trace Depth:", $"{DeferredStackTracing.maxTraceDepth}", Color.white),
                new("Hash Entries:", $"{DeferredStackTracingImpl.hashtableEntries}/{DeferredStackTracingImpl.hashtableSize}", Color.white),
                new("Hash Collisions:", $"{DeferredStackTracingImpl.collisions}", Color.white)
            ];

            return DrawSection(x, y, width, new("RNG & DEBUG", rngLines));
        }

        /// <summary>
        /// Draw command and sync data section
        /// </summary>
        private static float DrawCommandSyncSection(float x, float y, float width)
        {
            try
            {
                AsyncTimeComp async = Find.CurrentMap?.AsyncTime();
                
                DebugLine[] commandLines = [
                    new("Async Commands:", $"{async?.cmds?.Count ?? 0}", Color.white),
                    new("World Commands:", $"{Multiplayer.AsyncWorldTime?.cmds?.Count ?? 0}", Color.white),
                    new("Force Normal Speed:", $"{async?.slower?.forceNormalSpeedUntil ?? -1}", Color.white),
                    new("Async Time Status:", $"{Multiplayer.GameComp?.asyncTime ?? false}", Color.white),
                    new("Buffered Changes:", $"{SyncFieldUtil.bufferedChanges?.Sum(kv => kv.Value?.Count ?? 0) ?? 0}", Color.white)
                ];

                return DrawSection(x, y, width, new("COMMAND & SYNC", commandLines));
            }
            catch (Exception ex)
            {
                DebugLine[] errorLines = [new("Command Sync:", $"Error: {ex.Message}", Color.red)];
                return DrawSection(x, y, width, new("COMMAND & SYNC", errorLines));
            }
        }

        /// <summary>
        /// Draw memory and performance data section
        /// </summary>
        private static float DrawMemoryPerformanceSection(float x, float y, float width)
        {
            float serverTpt = TickPatch.tickUntil - TickPatch.Timer <= 3 ? TickPatch.serverTimePerTick * 1.2f :
                TickPatch.tickUntil - TickPatch.Timer >= 7 ? TickPatch.serverTimePerTick * 0.8f :
                TickPatch.serverTimePerTick;
                
            DebugLine[] memoryLines = [
                new("World Pawns:", $"{Find.WorldPawns.AllPawnsAliveOrDead.Count}", Color.white),
                new("Pool Free Items:", $"{SimplePool<StackTraceLogItemRaw>.FreeItemsCount}", Color.white),
                new("Calc Server TPT:", $"{serverTpt:F1}ms", Color.white)
            ];

            return DrawSection(x, y, width, new("MEMORY & PERFORMANCE", memoryLines));
        }

        /// <summary>
        /// Draw map management data section
        /// </summary>
        private static float DrawMapManagementSection(float x, float y, float width)
        {
            try
            {
                DebugLine[] mapLines = [
                    new("Haul Destinations:", $"{Find.CurrentMap?.haulDestinationManager?.AllHaulDestinationsListForReading?.Count ?? 0}", Color.white),
                    new("Designations:", $"{Find.CurrentMap?.designationManager?.designationsByDef?.Count ?? 0}", Color.white),
                    new("Haulable Items:", $"{Find.CurrentMap?.listerHaulables?.ThingsPotentiallyNeedingHauling()?.Count ?? 0}", Color.white),
                    new("Mining Designations:", $"{Find.CurrentMap?.designationManager?.SpawnedDesignationsOfDef(DesignationDefOf.Mine)?.Count() ?? 0}", Color.white),
                    new("First Ideology ID:", $"{Find.IdeoManager?.IdeosInViewOrder?.FirstOrDefault()?.id ?? -1}", Color.white)
                ];

                return DrawSection(x, y, width, new("MAP MANAGEMENT", mapLines));
            }
            catch (Exception ex)
            {
                DebugLine[] errorLines = [new("Map Management:", $"Error: {ex.Message}", Color.red)];
                return DrawSection(x, y, width, new("MAP MANAGEMENT", errorLines));
            }
        }

        // Helper methods for UI drawing

        private static float DrawSectionHeader(float x, float y, float width, string title)
        {
            Rect headerRect = new(x, y, width, LineHeight + 2f);
            using (MpStyle.Set(GameFont.Small).Set(Color.cyan).Set(TextAnchor.MiddleLeft))
            {
                Widgets.Label(headerRect, $"── {title} ──");
            }
            return y + LineHeight + 6f; // More spacing after headers
        }

        private static float DrawStatusLine(float x, float y, float width, string label, string value, Color valueColor)
        {
            // Use consistent column widths and add padding
            float labelWidth = width * LabelColumnWidth;
            float valueWidth = width * (1f - LabelColumnWidth);

            // Label with right padding
            using (MpStyle.Set(GameFont.Tiny).Set(Color.white).Set(TextAnchor.MiddleLeft))
            {
                Rect labelRect = new(x, y, labelWidth - 4f, LineHeight);
                Widgets.Label(labelRect, label);
            }

            // Value with left padding
            using (MpStyle.Set(GameFont.Tiny).Set(valueColor).Set(TextAnchor.MiddleLeft))
            {
                Rect valueRect = new(x + labelWidth + 4f, y, valueWidth - 4f, LineHeight);
                Widgets.Label(valueRect, value);
            }

            return y + LineHeight + 1f; // Small padding between lines
        }
    }
} 
