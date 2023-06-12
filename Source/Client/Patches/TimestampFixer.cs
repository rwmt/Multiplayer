using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches;

public static class TimestampFixer
{
    delegate ref int FieldGetter<in T>(T obj) where T : IExposable;

    private static Dictionary<Type, List<FieldGetter<IExposable>>> timestampFields = new();

    private static void Add<T>(FieldGetter<T> t) where T : IExposable
    {
        timestampFields.GetOrAddNew(typeof(T)).Add(obj => ref t((T)obj));
    }

    static TimestampFixer()
    {
        Add((Pawn_MindState mind) => ref mind.canSleepTick);
        Add((Pawn_MindState mind) => ref mind.canLovinTick);
        Add((Pawn_GuestTracker guest) => ref guest.ticksWhenAllowedToEscapeAgain);
        Add((Pawn_GuestTracker guest) => ref guest.lastPrisonBreakTicks);
    }

    public static int? currentOffset;

    public static void FixPawn(Pawn p, Map oldMap, Map newMap)
    {
        var oldTime = oldMap?.AsyncTime().mapTicks ?? Multiplayer.AsyncWorldTime.worldTicks;
        var newTime = newMap?.AsyncTime().mapTicks ?? Multiplayer.AsyncWorldTime.worldTicks;
        currentOffset = newTime - oldTime;

        MpLog.Debug($"Fixing pawn timestamps for {p} moving from {oldMap?.ToString() ?? "World"}:{oldTime} to {newMap?.ToString() ?? "World"}:{newTime}");

        try
        {
            // Auxiliary save which is used to visit the pawn's data
            Scribe.saver.DebugOutputFor(p);
        }
        finally
        {
            currentOffset = null;
        }
    }

    public static void ProcessExposable(IExposable exposable)
    {
        if (timestampFields.ContainsKey(exposable.GetType()))
            foreach (var del in timestampFields[exposable.GetType()])
                del(exposable) += currentOffset!.Value;
    }
}

[HarmonyPatch(typeof(DebugLoadIDsSavingErrorsChecker), nameof(DebugLoadIDsSavingErrorsChecker.RegisterDeepSaved))]
static class RegisterDeepSaved_ProcessExposable
{
    static void Prefix(object obj)
    {
        if (TimestampFixer.currentOffset != null && obj is IExposable exposable)
            TimestampFixer.ProcessExposable(exposable);
    }
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
static class PawnDespawn_RememberMap
{
    static void Prefix(Pawn __instance)
    {
        if (Multiplayer.Client == null) return;
        __instance.GetComp<MultiplayerPawnComp>().lastMap = __instance.Map.uniqueID;
    }
}

[HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.RemovePawn))]
static class WorldPawnsRemovePawn_RememberTick
{
    static void Prefix(Pawn p)
    {
        if (Multiplayer.Client == null) return;
        p.GetComp<MultiplayerPawnComp>().worldPawnRemoveTick = Multiplayer.AsyncWorldTime.worldTicks;
    }
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
static class PawnSpawn_FixTimestamp
{
    static void Postfix(Pawn __instance)
    {
        if (Multiplayer.Client == null) return;
        if (__instance.Map == null) return;

        if (__instance.GetComp<MultiplayerPawnComp>().worldPawnRemoveTick == Multiplayer.AsyncWorldTime.worldTicks)
            TimestampFixer.FixPawn(__instance, null, __instance.Map);
    }
}

[HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.AddPawn))]
static class WorldPawnsAddPawn_FixTimestamp
{
    static void Prefix(Pawn p)
    {
        if (Multiplayer.Client == null) return;

        var lastMap = p.GetComp<MultiplayerPawnComp>().lastMap;
        if (lastMap != -1)
            TimestampFixer.FixPawn(p, Find.Maps.FirstOrDefault(m => m.uniqueID == lastMap), null);
    }
}
