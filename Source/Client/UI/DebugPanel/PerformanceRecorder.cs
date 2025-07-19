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
    private static bool hasLoggedLimitWarning = false;
    private static DateTime recordingStartTime;
    private static int recordingFrameCount = 0;
    private static int lastNonPerfMetricsFrameCount = 0;

    public static int MaxSampleCountSetting = 72000; // Default: 20 min at 60fps
    public static int NonPerfFrameIntervalSetting = 60;
    private static int cachedMaxSampleCount;
    private static int cachedNonPerfMetricsFrameInterval;

    // Performance metrics
    private static CircularBuffer<float> frameTimeSamples;
    private static CircularBuffer<float> tickTimeSamples;
    private static CircularBuffer<float> deltaTimeSamples;
    private static CircularBuffer<float> fpsSamples;
    private static CircularBuffer<float> tpsSamples;
    private static CircularBuffer<float> normalizedTpsSamples;
    private static CircularBuffer<float> serverTPTSamples;

    // Networking metrics
    private static CircularBuffer<int> timerLagSamples;
    private static CircularBuffer<int> receivedCmdsSamples;
    private static CircularBuffer<int> sentCmdsSamples;
    private static CircularBuffer<int> bufferedChangesSamples;
    private static CircularBuffer<int> mapCmdsSamples;
    private static CircularBuffer<int> worldCmdsSamples;

    // Memory/GC metrics
    private static CircularBuffer<int> clientOpinionsSamples;
    private static CircularBuffer<int> worldPawnsSamples;
    private static CircularBuffer<int> windowCountSamples;
    
    private static int MaxSampleCount => cachedMaxSampleCount;
    private static int NonPerfMetricsFrameInterval => cachedNonPerfMetricsFrameInterval;

    public static bool IsRecording => isRecording;
    public static int FrameCount => recordingFrameCount;
    public static TimeSpan RecordingDuration => isRecording ? DateTime.Now - recordingStartTime : TimeSpan.Zero;
    public static float AverageFPS => fpsSamples.Count > 0 ? fpsSamples.GetValues().Average() : 0f;
    public static float AverageTPS => tpsSamples.Count > 0 ? tpsSamples.GetValues().Average() : 0f;
    public static float AverageNormalizedTPS => normalizedTpsSamples.Count > 0 ? normalizedTpsSamples.GetValues().Average() : 0f;

    /// <summary>
    /// Draws the performance recorder Start/Stop control buttons on the <see cref="SyncDebugPanel"/> interface.
    /// </summary>
    /// <param name="x">The x-coordinate of the starting position for the buttons.</param>
    /// <param name="y">The y-coordinate of the starting position for the buttons.</param>
    /// <param name="width">The total width available for the buttons.</param>
    /// <returns>The total height occupied by the buttons, including spacing.</returns>
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

    /// <summary>
    /// Draws the performance recorder section on the <see cref="SyncDebugPanel"/> interface.
    /// </summary>
    /// <remarks>This method displays the current status of the performance recorder, including the frame count,
    /// recording duration, and average frames per second (FPS) and ticks per second (TPS) if recording is active.</remarks>
    /// <param name="x">The x-coordinate of the section's top-left corner.</param>
    /// <param name="y">The y-coordinate of the section's top-left corner.</param>
    /// <param name="width">The width of the section to be drawn.</param>
    /// <returns>The height of the drawn section.</returns>
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
            lines.Add(new DebugLine("Avg FPS:", fpsSamples.Count > 0 ? $"{AverageFPS:F1}" : "N/A", Color.white));
            lines.Add(new DebugLine("Avg TPS:", tpsSamples.Count > 0 ? $"{AverageTPS:F1}" : "N/A", Color.white));
        }

        var section = new DebugSection("PERFORMANCE RECORDER", lines.ToArray());
        return DrawSection(x, y, width, section);
    }

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

    /// <summary>
    /// Records the current frame's performance metrics, including frame time, FPS, and other relevant data.
    /// </summary>
    /// <remarks>This method collects and stores various performance metrics for the current frame if
    /// recording is active. It logs frame time, FPS, and other metrics related to multiplayer and asynchronous
    /// operations. The method also checks and records non-performance metrics at specified intervals.</remarks>
    public static void RecordFrame()
    {
        if (!isRecording) return;

        recordingFrameCount++;

        LogTrimWarningIfNeeded();

        float deltaTime = Time.deltaTime;
        float frameTimeMs = deltaTime * 1000f;
        float fps = deltaTime > 0 ? 1f / deltaTime : 0f;

        frameTimeSamples.Add(frameTimeMs);
        tickTimeSamples.Add(TickPatch.tickTimer?.ElapsedMilliseconds ?? 0);
        deltaTimeSamples.Add(deltaTime * 60f);
        fpsSamples.Add(fps);

        if (Multiplayer.Client != null)
        {
            timerLagSamples.Add(TickPatch.tickUntil - TickPatch.Timer);

            if (Find.CurrentMap?.AsyncTime() is AsyncTimeComp asyncComp)
            {
                float currentTps = IngameUIPatch.tps;
                tpsSamples.Add(currentTps);

                // Only record normalized TPS if we're not in stabilization period
                if (PerformanceCalculator.GetStableNormalizedTPS(currentTps) is float stableNormalizedTps)
                {
                    normalizedTpsSamples.Add(stableNormalizedTps);
                }

                serverTPTSamples.Add(TickPatch.serverTimePerTick);
                mapCmdsSamples.Add(asyncComp.cmds?.Count ?? 0);
            }
        }
        // Check if enough frames have passed since last non-performance metrics update
        if (recordingFrameCount >= lastNonPerfMetricsFrameCount + NonPerfMetricsFrameInterval)
        {
            RecordNonPerformanceMetrics();
        }
    }

    /// <summary>
    /// Initiates the recording process for performance metrics.
    /// </summary>
    /// <remarks>This method starts recording performance data if it is not already in progress.  It resets
    /// the recording state and logs a message indicating the start of the recording.</remarks>
    public static void StartRecording()
    {
        if (isRecording) return;

        cachedMaxSampleCount = MaxSampleCountSetting;
        cachedNonPerfMetricsFrameInterval = NonPerfFrameIntervalSetting;

        InitializeBuffers();
        isRecording = true;
        recordingStartTime = DateTime.Now;
        recordingFrameCount = 0;
        ClearSamples();

        Verse.Log.Message("[PerformanceRecorder] Recording started");     
    }

    /// <summary>
    /// Stops the current recording session and processes the recorded data.
    /// </summary>
    /// <remarks>This method finalizes the recording session by calculating the duration of the recording,
    /// generating results, and outputting them to the console and a file. It also clears any collected samples and
    /// resets the logging state.</remarks>
    public static void StopRecording()
    {
        if (!isRecording) return;

        isRecording = false;
        var recordingDuration = DateTime.Now - recordingStartTime;

        // Generate and output results
        var results = GenerateResults(recordingDuration);
        Verse.Log.Message("[PerformanceRecorder] Recording stopped");

        OutputToConsole(results);
        OutputToFile(results);
        ClearSamples();
        hasLoggedLimitWarning = false;
    }

    private static void AppendCountStats(StringBuilder sb, string name, int count)
    {
        sb.AppendLine($"{name,-25} Count: {count,6:N0}");
    }

    private static void AppendDetailedStats(StringBuilder sb, string name, StatResult stats)
    {
        sb.AppendLine($"{name,-25} Avg: {stats.Average,8:F2}  Min: {stats.Min,8:F2}  Max: {stats.Max,8:F2}  Samples: {stats.Count,6:N0}");
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

    private static StatResult CalculateStats(IEnumerable<int> samples)
        => CalculateStats(samples.Select(s => (float)s));

    private static void InitializeBuffers()
    {
        var maxSamples = MaxSampleCount;
        var frameInterval = NonPerfMetricsFrameInterval;
        var nonPerfSamples = Math.Max(1, maxSamples / frameInterval);
        
        frameTimeSamples = new CircularBuffer<float>(maxSamples);
        tickTimeSamples = new CircularBuffer<float>(maxSamples);
        deltaTimeSamples = new CircularBuffer<float>(maxSamples);
        fpsSamples = new CircularBuffer<float>(maxSamples);
        tpsSamples = new CircularBuffer<float>(maxSamples);
        normalizedTpsSamples = new CircularBuffer<float>(maxSamples);
        serverTPTSamples = new CircularBuffer<float>(maxSamples);
        timerLagSamples = new CircularBuffer<int>(maxSamples);
        mapCmdsSamples = new CircularBuffer<int>(maxSamples);
        
        receivedCmdsSamples = new CircularBuffer<int>(nonPerfSamples);
        sentCmdsSamples = new CircularBuffer<int>(nonPerfSamples);
        bufferedChangesSamples = new CircularBuffer<int>(nonPerfSamples);
        worldCmdsSamples = new CircularBuffer<int>(nonPerfSamples);
        clientOpinionsSamples = new CircularBuffer<int>(nonPerfSamples);
        worldPawnsSamples = new CircularBuffer<int>(nonPerfSamples);
        windowCountSamples = new CircularBuffer<int>(nonPerfSamples);
    }
    
    private static void ClearSamples()
    {
        frameTimeSamples?.Clear();
        tickTimeSamples?.Clear();
        deltaTimeSamples?.Clear();
        fpsSamples?.Clear();
        tpsSamples?.Clear();
        normalizedTpsSamples?.Clear();
        serverTPTSamples?.Clear();
        timerLagSamples?.Clear();
        receivedCmdsSamples?.Clear();
        sentCmdsSamples?.Clear();
        bufferedChangesSamples?.Clear();
        mapCmdsSamples?.Clear();
        worldCmdsSamples?.Clear();
        clientOpinionsSamples?.Clear();
        worldPawnsSamples?.Clear();
        windowCountSamples?.Clear();
        lastNonPerfMetricsFrameCount = 0;
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

    private static PerformanceResults GenerateResults(TimeSpan duration)
    {
        return new PerformanceResults
        {
            Duration = duration,
            FrameCount = recordingFrameCount,
            StartTime = recordingStartTime,

            // Performance stats
            FrameTime = CalculateStats(frameTimeSamples.GetValues()),
            TickTime = CalculateStats(tickTimeSamples.GetValues()),
            DeltaTime = CalculateStats(deltaTimeSamples.GetValues()),
            FPS = CalculateStats(fpsSamples.GetValues()),
            TPS = CalculateStats(tpsSamples.GetValues()),
            NormalizedTPS = CalculateStats(normalizedTpsSamples.GetValues()),
            ServerTPT = CalculateStats(serverTPTSamples.GetValues()),

            // Networking stats
            TimerLag = CalculateStats(timerLagSamples.GetValues()),
            ReceivedCmds = CalculateStats(receivedCmdsSamples.GetValues()),
            SentCmds = CalculateStats(sentCmdsSamples.GetValues()),
            BufferedChanges = CalculateStats(bufferedChangesSamples.GetValues()),
            MapCmds = CalculateStats(mapCmdsSamples.GetValues()),
            WorldCmds = CalculateStats(worldCmdsSamples.GetValues()),

            // Memory stats
            ClientOpinions = CalculateStats(clientOpinionsSamples.GetValues()),
            WorldPawns = CalculateStats(worldPawnsSamples.GetValues()),
            WindowCount = CalculateStats(windowCountSamples.GetValues())
        };
    }

    private static void LogTrimWarningIfNeeded()
    {
        if (!hasLoggedLimitWarning && frameTimeSamples?.IsFull == true)
        {
            Verse.Log.Warning($"[PerformanceRecorder] Sample buffer full - oldest data will be overwritten to maintain rolling {MaxSampleCount} max sample count");
            hasLoggedLimitWarning = true;
        }
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
        sb.AppendLine($"  Client Opinions: Avg {results.ClientOpinions.Average:F0}   Min {results.ClientOpinions.Min:F0}     Max {results.ClientOpinions.Max:F0}   ({results.ClientOpinions.Count} samples)");
        sb.AppendLine($"  World Pawns:     Avg {results.WorldPawns.Average:F0}   Min {results.WorldPawns.Min:F0}     Max {results.WorldPawns.Max:F0}   ({results.WorldPawns.Count} samples)");
        sb.AppendLine($"  Window Count:    Avg {results.WindowCount.Average:F0}   Min {results.WindowCount.Min:F0}     Max {results.WindowCount.Max:F0}   ({results.WindowCount.Count} samples)");
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

    private static void RecordNonPerformanceMetrics()
    {
        lastNonPerfMetricsFrameCount = recordingFrameCount;

        if (Multiplayer.Client != null)
        {
            bufferedChangesSamples.Add(SyncFieldUtil.bufferedChanges?.Sum(kv => kv.Value?.Count ?? 0) ?? 0);
            receivedCmdsSamples.Add(Multiplayer.session?.receivedCmds ?? 0);
            sentCmdsSamples.Add(Multiplayer.session?.remoteSentCmds ?? 0);
            worldCmdsSamples.Add(Multiplayer.AsyncWorldTime?.cmds?.Count ?? 0);
            clientOpinionsSamples.Add(Multiplayer.game?.sync?.knownClientOpinions?.Count ?? 0);
        }

        worldPawnsSamples.Add(Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0);
        windowCountSamples.Add(Find.WindowStack?.windows?.Count ?? 0);
    }
}

#region PerformanceResults and StatResult

internal struct StatResult
{
    public float Average;
    public int Count;
    public float Max;
    public float Min;
}

internal class PerformanceResults
{
    public StatResult BufferedChanges;
    // System
    public StatResult ClientOpinions;

    public StatResult DeltaTime;
    public TimeSpan Duration;
    public StatResult FPS;
    public int FrameCount;
    // Performance
    public StatResult FrameTime;

    public StatResult MapCmds;
    public StatResult NormalizedTPS;
    public StatResult ReceivedCmds;
    public StatResult SentCmds;
    public StatResult ServerTPT;
    public DateTime StartTime;
    public StatResult TickTime;
    // Networking
    public StatResult TimerLag;

    public StatResult TPS;
    public StatResult WindowCount;
    public StatResult WorldCmds;
    public StatResult WorldPawns;
}
#endregion PerformanceResults and StatResult

#region CircularBuffer

internal class CircularBuffer<T>
{
    private readonly T[] buffer;
    private readonly int capacity;
    private int count = 0;
    private int head = 0;
    public CircularBuffer(int capacity)
    {
        this.capacity = capacity;
        this.buffer = new T[capacity];
    }

    public int Count => count;

    public bool IsFull => count == capacity;

    public void Add(T item)
    {
        buffer[head] = item;
        head = (head + 1) % capacity;
        if (count < capacity)
            count++;
    }

    public void Clear()
    {
        head = 0;
        count = 0;
        Array.Clear(buffer, 0, capacity);
    }

    public IEnumerable<T> GetValues()
    {
        if (count == 0) yield break;

        int start = count < capacity ? 0 : head;
        for (int i = 0; i < count; i++)
        {
            yield return buffer[(start + i) % capacity];
        }
    }
}
#endregion CircularBuffer
