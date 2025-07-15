using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.DebugUi;

/// <summary>
/// Records performance and networking diagnostics during multiplayer sessions.
/// Tracks averages, highs, and lows for various metrics and outputs results to file and console.
/// </summary>
public static class PerformanceRecorder
{
    private static bool isRecording = false;
    private static DateTime recordingStartTime;
    private static int recordingFrameCount = 0;

    // Performance metrics
    private static List<float> frameTimeSamples = new List<float>();
    private static List<float> tickTimeSamples = new List<float>();
    private static List<float> deltaTimeSamples = new List<float>();
    private static List<float> fpsSamples = new List<float>();
    private static List<float> tpsSamples = new List<float>();
    private static List<float> normalizedTpsSamples = new List<float>();
    private static List<float> serverTPTSamples = new List<float>();

    // Networking metrics
    private static List<int> timerLagSamples = new List<int>();
    private static List<int> receivedCmdsSamples = new List<int>();
    private static List<int> sentCmdsSamples = new List<int>();
    private static List<int> bufferedChangesSamples = new List<int>();
    private static List<int> mapCmdsSamples = new List<int>();
    private static List<int> worldCmdsSamples = new List<int>();

    // Memory/GC metrics
    private static List<int> clientOpinionsSamples = new List<int>();
    private static List<int> worldPawnsSamples = new List<int>();
    private static List<int> windowCountSamples = new List<int>();
    
    public static bool IsRecording => isRecording;
    public static int FrameCount => recordingFrameCount;
    public static TimeSpan RecordingDuration => isRecording ? DateTime.Now - recordingStartTime : TimeSpan.Zero;
    public static float AverageFPS => fpsSamples.Count > 0 ? fpsSamples.Average() : 0f;
    public static float AverageTPS => tpsSamples.Count > 0 ? tpsSamples.Average() : 0f;
    public static float AverageNormalizedTPS => normalizedTpsSamples.Count > 0 ? normalizedTpsSamples.Average() : 0f;

    /// <summary>
    /// Get recording status for debug panel display
    /// </summary>
    public static StatusBadge GetRecordingStatus()
    {
        if (!isRecording)
            return new StatusBadge("⏸", Color.gray, "REC", "Performance recording is stopped");
        
        var duration = DateTime.Now - recordingStartTime;
        return new StatusBadge("⏺", Color.red, $"REC {duration.TotalSeconds:F0}s", $"Recording performance for {duration.TotalSeconds:F1} seconds");
    }

    public static void StartRecording()
    {
        if (isRecording) return;

        isRecording = true;
        recordingStartTime = DateTime.Now;
        recordingFrameCount = 0;

        // Clear previous samples
        frameTimeSamples.Clear();
        tickTimeSamples.Clear();
        deltaTimeSamples.Clear();
        fpsSamples.Clear();
        tpsSamples.Clear();
        normalizedTpsSamples.Clear();
        serverTPTSamples.Clear();
        timerLagSamples.Clear();
        receivedCmdsSamples.Clear();
        sentCmdsSamples.Clear();
        bufferedChangesSamples.Clear();
        mapCmdsSamples.Clear();
        worldCmdsSamples.Clear();
        clientOpinionsSamples.Clear();
        worldPawnsSamples.Clear();
        windowCountSamples.Clear();

        Verse.Log.Message("[PerformanceRecorder] Recording started");
    }

    public static void StopRecording()
    {
        if (!isRecording) return;

        isRecording = false;
        var recordingDuration = DateTime.Now - recordingStartTime;

        Verse.Log.Message("[PerformanceRecorder] Recording stopped");

        // Generate and output results
        var results = GenerateResults(recordingDuration);
        OutputToConsole(results);
        OutputToFile(results);
    }

    public static void RecordFrame()
    {
        if (!isRecording) return;

        recordingFrameCount++;

        // Performance metrics
        frameTimeSamples.Add(Time.deltaTime * 1000f);
        tickTimeSamples.Add(TickPatch.tickTimer?.ElapsedMilliseconds ?? 0);
        deltaTimeSamples.Add(Time.deltaTime * 60f);
        fpsSamples.Add(1f / Time.deltaTime);

        // Networking metrics
        if (Multiplayer.Client != null)
        {
            timerLagSamples.Add(TickPatch.tickUntil - TickPatch.Timer);
            receivedCmdsSamples.Add(Multiplayer.session?.receivedCmds ?? 0);
            sentCmdsSamples.Add(Multiplayer.session?.remoteSentCmds ?? 0);
            bufferedChangesSamples.Add(SyncFieldUtil.bufferedChanges?.Sum(kv => kv.Value?.Count ?? 0) ?? 0);

            if (Find.CurrentMap?.AsyncTime() != null)
            {
                var async = Find.CurrentMap.AsyncTime();
                float currentTps = IngameUIPatch.tps;
                tpsSamples.Add(currentTps);
                
                // Only record normalized TPS if we're not in stabilization period
                if (PerformanceCalculator.GetStableNormalizedTPS(currentTps) is float stableNormalizedTps)
                {
                    normalizedTpsSamples.Add(stableNormalizedTps);
                }
                
                serverTPTSamples.Add(TickPatch.serverTimePerTick);
                mapCmdsSamples.Add(async.cmds?.Count ?? 0);
                worldCmdsSamples.Add(Multiplayer.AsyncWorldTime?.cmds?.Count ?? 0);
            }

            clientOpinionsSamples.Add(Multiplayer.game?.sync?.knownClientOpinions?.Count ?? 0);
        }

        // Memory/system metrics
        worldPawnsSamples.Add(Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0);
        windowCountSamples.Add(Find.WindowStack?.windows?.Count ?? 0);
        }
        
    private static PerformanceResults GenerateResults(TimeSpan duration)
    {
        return new PerformanceResults
        {
            Duration = duration,
            FrameCount = recordingFrameCount,
            StartTime = recordingStartTime,

            // Performance stats
            FrameTime = CalculateStats(frameTimeSamples),
            TickTime = CalculateStats(tickTimeSamples),
            DeltaTime = CalculateStats(deltaTimeSamples),
            FPS = CalculateStats(fpsSamples),
            TPS = CalculateStats(tpsSamples),
            NormalizedTPS = CalculateStats(normalizedTpsSamples),
            ServerTPT = CalculateStats(serverTPTSamples),

            // Networking stats
            TimerLag = CalculateStats(timerLagSamples.Select(x => (float)x)),
            ReceivedCmds = CalculateStats(receivedCmdsSamples.Select(x => (float)x)),
            SentCmds = CalculateStats(sentCmdsSamples.Select(x => (float)x)),
            BufferedChanges = CalculateStats(bufferedChangesSamples.Select(x => (float)x)),
            MapCmds = CalculateStats(mapCmdsSamples.Select(x => (float)x)),
            WorldCmds = CalculateStats(worldCmdsSamples.Select(x => (float)x)),

            // Memory stats
            ClientOpinions = CalculateStats(clientOpinionsSamples.Select(x => (float)x)),
            WorldPawns = CalculateStats(worldPawnsSamples.Select(x => (float)x)),
            WindowCount = CalculateStats(windowCountSamples.Select(x => (float)x))
        };
    }

    private static StatResult CalculateStats(IEnumerable<float> samples)
    {
        var list = samples.ToList();
        if (list.Count == 0)
            return new StatResult { Average = 0, Min = 0, Max = 0, Count = 0 };

        return new StatResult
        {
            Average = list.Average(),
            Min = list.Min(),
            Max = list.Max(),
            Count = list.Count
        };
    }

    private static void OutputToConsole(PerformanceResults results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- MULTIPLAYER PERFORMANCE RECORDING RESULTS --");
        sb.AppendLine($"Recording Duration: {results.Duration.TotalSeconds:F2} seconds");
        sb.AppendLine($"Total Frames: {results.FrameCount:N0}");
        sb.AppendLine($"Recording Start: {results.StartTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("PERFORMANCE METRICS:");
        sb.AppendLine($"  Frame Time:     Avg {results.FrameTime.Average:F2}ms  Min {results.FrameTime.Min:F2}ms  Max {results.FrameTime.Max:F2}ms");
        sb.AppendLine($"  Tick Time:      Avg {results.TickTime.Average:F2}ms  Min {results.TickTime.Min:F2}ms  Max {results.TickTime.Max:F2}ms");
        sb.AppendLine($"  FPS:            Avg {results.FPS.Average:F1}     Min {results.FPS.Min:F1}     Max {results.FPS.Max:F1}");
        sb.AppendLine($"  TPS (Raw):      Avg {results.TPS.Average:F2}     Min {results.TPS.Min:F2}     Max {results.TPS.Max:F2}");
        sb.AppendLine($"  TPS (Norm %):   Avg {results.NormalizedTPS.Average:F1}%    Min {results.NormalizedTPS.Min:F1}%    Max {results.NormalizedTPS.Max:F1}%");
        sb.AppendLine($"  Server TPT:     Avg {results.ServerTPT.Average:F2}ms  Min {results.ServerTPT.Min:F2}ms  Max {results.ServerTPT.Max:F2}ms");
        sb.AppendLine();

        sb.AppendLine("NETWORKING METRICS:");
        sb.AppendLine($"  Timer Lag:      Avg {results.TimerLag.Average:F1}     Min {results.TimerLag.Min:F0}     Max {results.TimerLag.Max:F0}");
        sb.AppendLine($"  Received Cmds:  Avg {results.ReceivedCmds.Average:F0}     Min {results.ReceivedCmds.Min:F0}     Max {results.ReceivedCmds.Max:F0}");
        sb.AppendLine($"  Sent Cmds:      Avg {results.SentCmds.Average:F0}     Min {results.SentCmds.Min:F0}     Max {results.SentCmds.Max:F0}");
        sb.AppendLine($"  Buffered Changes: Avg {results.BufferedChanges.Average:F0}   Min {results.BufferedChanges.Min:F0}     Max {results.BufferedChanges.Max:F0}");
        sb.AppendLine($"  Map Commands:   Avg {results.MapCmds.Average:F0}     Min {results.MapCmds.Min:F0}     Max {results.MapCmds.Max:F0}");
        sb.AppendLine($"  World Commands: Avg {results.WorldCmds.Average:F0}     Min {results.WorldCmds.Min:F0}     Max {results.WorldCmds.Max:F0}");
        sb.AppendLine();

        sb.AppendLine("SYSTEM METRICS:");
        sb.AppendLine($"  Client Opinions: Avg {results.ClientOpinions.Average:F0}   Min {results.ClientOpinions.Min:F0}     Max {results.ClientOpinions.Max:F0}");
        sb.AppendLine($"  World Pawns:     Avg {results.WorldPawns.Average:F0}   Min {results.WorldPawns.Min:F0}     Max {results.WorldPawns.Max:F0}");
        sb.AppendLine($"  Window Count:    Avg {results.WindowCount.Average:F0}   Min {results.WindowCount.Min:F0}     Max {results.WindowCount.Max:F0}");
        sb.AppendLine();
        sb.AppendLine("-- END RECORDING RESULTS --");

        Verse.Log.Message(sb.ToString());
    }

    private static void OutputToFile(PerformanceResults results)
    {
        try
        {
            var fileName = $"MpLogs/MpPerf-{results.StartTime:MMddHHmmss}.txt";
            var filePath = Path.Combine(GenFilePaths.SaveDataFolderPath, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("MULTIPLAYER PERFORMANCE RECORDING RESULTS");
            sb.AppendLine("-----------------------------------------");
            sb.AppendLine($"Recording Start: {results.StartTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Recording Duration: {results.Duration.TotalSeconds:F2} seconds");
            sb.AppendLine($"Total Frames Recorded: {results.FrameCount:N0}");
            sb.AppendLine($"Average Frame Rate: {results.FrameCount / results.Duration.TotalSeconds:F1} FPS");
            sb.AppendLine();

            sb.AppendLine("PERFORMANCE METRICS");
            sb.AppendLine("-------------------");
            AppendDetailedStats(sb, "Frame Time (ms)", results.FrameTime);
            AppendDetailedStats(sb, "Tick Time (ms)", results.TickTime);
            AppendDetailedStats(sb, "Delta Time", results.DeltaTime);
            AppendDetailedStats(sb, "Frames Per Second", results.FPS);
            AppendDetailedStats(sb, "Ticks Per Second (Raw)", results.TPS);
            AppendDetailedStats(sb, "TPS Performance (%)", results.NormalizedTPS);
            AppendDetailedStats(sb, "Server Time Per Tick (ms)", results.ServerTPT);
            AppendDetailedStats(sb, "Timer Lag", results.TimerLag);

            sb.AppendLine();



            sb.AppendLine("MISC METRICS");
            sb.AppendLine("------------");
            AppendDetailedStats(sb, "Client Opinions", results.ClientOpinions);
            AppendDetailedStats(sb, "World Pawns", results.WorldPawns);
            AppendDetailedStats(sb, "Window Count", results.WindowCount);
            sb.AppendLine();

            sb.AppendLine("COMMAND METRICS");
            sb.AppendLine("---------------");
            AppendCountStats(sb, "Received Commands", (int)results.ReceivedCmds.Max);
            AppendCountStats(sb, "Sent Commands", (int)results.SentCmds.Max);
            AppendCountStats(sb, "Buffered Changes", (int)results.BufferedChanges.Max);
            AppendCountStats(sb, "Map Commands", (int)results.MapCmds.Max);
            AppendCountStats(sb, "World Commands", (int)results.WorldCmds.Max);
            sb.AppendLine();

            sb.AppendLine("SYSTEM INFORMATION");
            sb.AppendLine("------------------");
            sb.AppendLine($"RimWorld Version: {VersionControl.CurrentVersionStringWithRev}");
            sb.AppendLine($"Multiplayer Version: {MpVersion.Version}");
            sb.AppendLine($"Unity Version: {Application.unityVersion}");
            sb.AppendLine($"Platform: {Application.platform}");
            sb.AppendLine($"System Memory: {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"Graphics Memory: {SystemInfo.graphicsMemorySize} MB");
            sb.AppendLine($"Processor: {SystemInfo.processorType} ({SystemInfo.processorCount} cores)");

            File.WriteAllText(filePath, sb.ToString());
            Verse.Log.Message($"[PerformanceRecorder] Results saved to: {filePath}");
        }
        catch (Exception ex)
        {
            Verse.Log.Error($"[PerformanceRecorder] Failed to save results to file: {ex.Message}");
        }
    }

    private static void AppendDetailedStats(StringBuilder sb, string name, StatResult stats)
    {
        sb.AppendLine($"{name,-25} Avg: {stats.Average,8:F2}  Min: {stats.Min,8:F2}  Max: {stats.Max,8:F2}  Samples: {stats.Count,6:N0}");
    }

    private static void AppendCountStats(StringBuilder sb, string name, int count)
    {
        sb.AppendLine($"{name,-25} Count: {count,6:N0}");
    }
    /// <summary>
    /// Draw performance recorder section for debug panel
    /// </summary>
    public static float DrawPerformanceRecorderSection(float x, float y, float width)
    {
        StatusBadge recordingStatus = GetRecordingStatus();
        
        var lines = new List<DebugLine>
        {
            new DebugLine("Status:", recordingStatus.text, recordingStatus.color),
            new DebugLine("Frame Count:", recordingFrameCount.ToString(), Color.white)
        };

        if (isRecording)
        {
            var duration = DateTime.Now - recordingStartTime;
            lines.Add(new DebugLine("Duration:", $"{duration.TotalSeconds:F1}s", Color.white));
            lines.Add(new DebugLine("Avg FPS:", frameTimeSamples.Count > 0 ? $"{fpsSamples.Average():F1}" : "N/A", Color.white));
            lines.Add(new DebugLine("Avg TPS:", tpsSamples.Count > 0 ? $"{tpsSamples.Average():F1}" : "N/A", Color.white));
        }

        var section = new DebugSection("PERFORMANCE RECORDER", lines.ToArray());
        return DrawSection(x, y, width, section);
    }

    /// <summary>
    /// Draw performance recorder controls for debug panel
    /// </summary>
    public static float DrawPerformanceRecorderControls(float x, float y, float width)
    {
        var buttonWidth = 60f;
        var buttonHeight = 20f;
        var spacing = 4f;
        
        var startRect = new Rect(x, y, buttonWidth, buttonHeight);
        var stopRect = new Rect(x + buttonWidth + spacing, y, buttonWidth, buttonHeight);

        GUI.color = isRecording ? Color.gray : Color.white;
        
        if (Widgets.ButtonText(startRect, "Start") && !isRecording)
        {
            StartRecording();
        }


        GUI.color = !isRecording ? Color.gray : Color.white;
        
        if (Widgets.ButtonText(stopRect, "Stop") && isRecording)
        {
            StopRecording();
        }
        
        
        UnityEngine.GUI.color = Color.white;
        return buttonHeight + spacing;
    }

    // Helper method to draw sections (copied from SyncDebugPanel pattern)
    private static float DrawSection(float x, float y, float width, DebugSection section)
    {
        float startY = y;
        const float LineHeight = 18f;
        const float SectionSpacing = 10f;
        const float LabelColumnWidth = 0.5f;

        // Draw section header
        var headerRect = new Rect(x, y, width, LineHeight + 2f);
        using (MpStyle.Set(GameFont.Small).Set(Color.cyan).Set(TextAnchor.MiddleLeft))
        {
            Widgets.Label(headerRect, $"── {section.Title} ──");
        }
        y += LineHeight + 6f;

        // Draw section lines
        foreach (var line in section.Lines)
        {
            var labelWidth = width * LabelColumnWidth;
            var valueWidth = width * (1f - LabelColumnWidth);

            // Label
            using (MpStyle.Set(GameFont.Tiny).Set(Color.white).Set(TextAnchor.MiddleLeft))
            {
                var labelRect = new Rect(x, y, labelWidth - 4f, LineHeight);
                Widgets.Label(labelRect, line.Label);
            }

            // Value
            using (MpStyle.Set(GameFont.Tiny).Set(line.Color).Set(TextAnchor.MiddleLeft))
            {
                var valueRect = new Rect(x + labelWidth + 4f, y, valueWidth - 4f, LineHeight);
                Widgets.Label(valueRect, line.Value);
            }

            y += LineHeight + 1f;
        }

        return y - startY + SectionSpacing;
    }
}

public class PerformanceResults
{
    public TimeSpan Duration;
    public int FrameCount;
    public DateTime StartTime;

    // Performance
    public StatResult FrameTime;
    public StatResult TickTime;
    public StatResult DeltaTime;
    public StatResult FPS;
    public StatResult TPS;
    public StatResult NormalizedTPS;
    public StatResult ServerTPT;

    // Networking
    public StatResult TimerLag;
    public StatResult ReceivedCmds;
    public StatResult SentCmds;
    public StatResult BufferedChanges;
    public StatResult MapCmds;
    public StatResult WorldCmds;

    // System
    public StatResult ClientOpinions;
    public StatResult WorldPawns;
    public StatResult WindowCount;
}

public struct StatResult
{
    public float Average;
    public float Min;
    public float Max;
    public int Count;
}
