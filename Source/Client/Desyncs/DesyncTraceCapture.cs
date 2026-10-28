using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Multiplayer.Client.Desyncs;

internal readonly record struct DesyncTrace(string Text, int Depth, int Hash);

internal static class DesyncTraceCapture
{
    internal const int MaxDepth = 32;
    internal const string FastDescription = "Detailed stack not captured (Fast tracing mode).";

    // This decision must precede stack walking, formatting, and any pooled-item allocation.
    internal static bool TryCapture(DesyncTracingMode mode, out DesyncTrace trace)
    {
        trace = default;
        if (mode == DesyncTracingMode.Fast)
        {
            trace = new DesyncTrace(FastDescription, 0, 0);
            return true;
        }
        if (mode != DesyncTracingMode.Slow)
            return false;

        trace = CaptureSlow();
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DesyncTrace CaptureSlow()
    {
        // Skip this helper. Retain caller frames even when the JIT inlines TryCapture.
        var frames = new StackTrace(1, false).GetFrames();
        var text = new StringBuilder();
        int hash = 17;
        int depth = Math.Min(frames?.Length ?? 0, MaxDepth);
        for (int i = 0; i < depth; i++)
        {
            var method = frames[i].GetMethod();
            var name = $"{method?.DeclaringType?.FullName}.{method}";
            text.AppendLine(name);
            foreach (char c in name)
                hash = unchecked(hash * 31 + c);
        }
        return new DesyncTrace(text.ToString(), depth, hash);
    }
}
