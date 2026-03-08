using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using JetBrains.Annotations;
using Verse;

namespace Multiplayer.Client.Desyncs;

public class JittedMethod
{
    [CanBeNull] public MethodBase method;
    public MethodBase from;
    [CanBeNull] public int[] mapTicks;
    public int worldTicks;
    public int timer;
    public bool inInterface;

    public string TimeString()
    {
        return $"m:{mapTicks.Join(delimiter: ",")} w:{worldTicks} t:{timer}";
    }

    public override string ToString()
    {
        return $"{method?.DeclaringType}.{method?.Name} from {from.DeclaringType}.{from.Name} {TimeString()} i:{inInterface}";
    }
}

public static class JittedMethods
{
    private static Queue<JittedMethod> methodQueue = new();
    private static IntPtr profiler;
    private static bool adding;

    public static void Init()
    {
        profiler = Native.mono_profiler_create(IntPtr.Zero);
        Native.mono_profiler_set_jit_done_callback(profiler, (_, method, _) =>
        {
            if (!adding && UnityData.IsInMainThread && Multiplayer.settings != null)
            {
                adding = true;
                try
                {
                    var methodBase = Native.GetMethodBaseFromRuntimePointer(method);
                    methodQueue.Enqueue(new JittedMethod
                    {
                        method = methodBase,
                        from = new StackTrace().GetFrame(1)?.GetMethod(),
                        mapTicks = Multiplayer.game?.asyncTimeComps.Select(c => c.mapTicks).ToArray(),
                        worldTicks = Multiplayer.game?.asyncWorldTimeComp?.worldTicks ?? -1,
                        timer = TickPatch.Timer,
                        inInterface = Multiplayer.InInterface
                    });

                    if (methodQueue.Count > Multiplayer.settings.jittedMethodsInDesync)
                        methodQueue.Dequeue();
                }
                finally
                {
                    adding = false;
                }
            }
        });
    }

    public static void OnApplicationQuit()
    {
        Native.mono_profiler_set_jit_done_callback(profiler, null);
    }

    public static string GetJittedMethodsString()
    {
        // Two ToArrays to prevent a compilation in between from causing a "Collection was modified" exception
        var jittedMethods = methodQueue.ToArray().Select((j, i) => (j, i)).ToArray();

        var builder = new StringBuilder();
        builder.Append("In simulation:"); // This will get a newline from the != comparison below
        var timeString = "";

        foreach (var j in jittedMethods.Where(j => !j.j.inInterface))
        {
            if (timeString != j.j.TimeString())
                builder.AppendLine();
            builder.AppendLine($"{j.i} {j.j}");
            timeString = j.j.TimeString();
        }

        builder.AppendLine();
        builder.AppendLine();

        timeString = "";

        builder.Append("In interface:");
        foreach (var j in jittedMethods.Where(j => j.j.inInterface))
        {
            if (timeString != j.j.TimeString())
                builder.AppendLine();
            builder.AppendLine($"{j.i} {j.j}");
            timeString = j.j.TimeString();
        }

        return builder.ToString();
    }
}
