using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Patches;

// todo wip
//[HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.AddPawn))]
static class WorldPawnsAdd
{
    public static List<Action> toProcess = new();

    public static void PostProcess()
    {
        foreach (var action in toProcess)
            action();

        toProcess.Clear();
    }

    static bool Prefix(WorldPawns __instance, Pawn p)
    {
        Log.Message($"Add world pawn from {Multiplayer.MapContext}: {p} {new StackTrace()}");

        if (Multiplayer.MapContext != null)
        {
            toProcess.Insert(0, () => Add(p));
            OnMainThread.Enqueue(PostProcess);
        }

        return Multiplayer.MapContext == null;
    }

    [SyncMethod(exposeParameters = new[]{0})]
    static void Add(Pawn p)
    {
        Find.WorldPawns.AddPawn(p);
    }
}

//[HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Add))]
static class WorldObjectAdd
{
    static bool Prefix(WorldObjectsHolder __instance, WorldObject o)
    {
        Log.Message($"Add world object from {Multiplayer.MapContext}: {o}");

        if (Multiplayer.MapContext != null)
        {
            WorldPawnsAdd.toProcess.Add(() => Add(o));
            OnMainThread.Enqueue(WorldPawnsAdd.PostProcess);
        }

        return Multiplayer.MapContext == null;
    }

    [SyncMethod(exposeParameters = new[]{0})]
    static void Add(WorldObject o)
    {
        Find.WorldObjects.Add(o);
    }
}
