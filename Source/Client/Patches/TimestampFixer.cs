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

    // Shifts a timestamp field by the given offset
    delegate void FieldFixer(IExposable obj, int offset);

    private static Dictionary<Type, List<FieldFixer>> timestampFields = new();

    // timestampFields including the fields of base types, by the exact type
    private static Dictionary<Type, List<FieldFixer>> resolvedFields = new();

    // Values below minValid are "never" markers: kept as they are, and shifted values are clamped to stay valid
    private static void Add<T>(FieldGetter<T> t, int minValid = int.MinValue) where T : IExposable
    {
        timestampFields.GetOrAddNew(typeof(T)).Add((obj, offset) =>
        {
            ref int field = ref t((T)obj);
            if (field >= minValid)
                field = Math.Max(minValid, field + offset);
        });
    }

    static TimestampFixer()
    {
        Add((Pawn_MindState mind) => ref mind.canSleepTick);
        Add((Pawn_MindState mind) => ref mind.canLovinTick);
        Add((Pawn_GuestTracker guest) => ref guest.ticksWhenAllowedToEscapeAgain);
        Add((Pawn_GuestTracker guest) => ref guest.lastPrisonBreakTicks);
        Add((Need_Food food) => ref food.lastNonStarvingTick);
        Add((Ability ability) => ref ability.cooldownEndTick);
        Add((Ability ability) => ref ability.lastCastTick, minValid: 0);
        Add((Pawn_SkillTracker skills) => ref skills.lastXpSinceMidnightResetTimestamp, minValid: 0);
        Add((FactionPermit permit) => ref permit.lastUsedTick, minValid: 1);
        Add((Pawn_RoyaltyTracker royalty) => ref royalty.lastDecreeTicks);
    }

    public static int? currentOffset;

    public static void FixPawn(Pawn p, Map oldMap, Map newMap)
    {
        var oldTime = oldMap?.AsyncTime().mapTicks ?? Multiplayer.AsyncWorldTime.worldTicks;
        var newTime = newMap?.AsyncTime().mapTicks ?? Multiplayer.AsyncWorldTime.worldTicks;
        if (newTime == oldTime) return;

        currentOffset = newTime - oldTime;

        MpLog.Debug($"Fixing pawn timestamps for {p} moving from {oldMap?.ToString() ?? "World"}:{oldTime} to {newMap?.ToString() ?? "World"}:{newTime}");

        try
        {
            // Saving a destroyed pawn logs a warning, and every pawn killed on a map is passed to the world pawns. Its
            // timestamps still have to move to the world's time: they're shifted back if it's resurrected.
            if (p.Destroyed)
                FixDirectly(p);
            else
                // Auxiliary save which is used to visit the pawn's data
                Scribe.saver.DebugOutputFor(p);
        }
        finally
        {
            currentOffset = null;
        }
    }

    // The holders of the fields above, without the save pass's walk through the pawn's data
    private static void FixDirectly(Pawn p)
    {
        ProcessExposable(p.mindState);
        ProcessExposable(p.guest);
        ProcessExposable(p.needs?.food);
        ProcessExposable(p.skills);
        ProcessExposable(p.royalty);

        if (p.abilities != null)
            foreach (var ability in p.abilities.abilities)
                ProcessExposable(ability);

        if (p.royalty != null)
            foreach (var permit in p.royalty.AllFactionPermits)
                ProcessExposable(permit);
    }

    public static void ProcessExposable(IExposable exposable)
    {
        if (exposable == null) return;

        foreach (var fixer in FieldsFor(exposable.GetType()))
            fixer(exposable, currentOffset!.Value);
    }

    private static List<FieldFixer> FieldsFor(Type type)
    {
        if (resolvedFields.TryGetValue(type, out var fields))
            return fields;

        fields = new List<FieldFixer>();
        for (var t = type; t != null; t = t.BaseType)
            if (timestampFields.TryGetValue(t, out var own))
                fields.AddRange(own);

        return resolvedFields[type] = fields;
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

        var comp = __instance.GetComp<MultiplayerPawnComp>();
        if (comp.worldPawnRemoveTick != -1)
        {
            TimestampFixer.FixPawn(__instance, null, __instance.Map);
            comp.worldPawnRemoveTick = -1;
        }
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
        {
            TimestampFixer.FixPawn(p, Find.Maps.FirstOrDefault(m => m.uniqueID == lastMap), null);
            p.GetComp<MultiplayerPawnComp>().lastMap = -1;
        }
    }
}
