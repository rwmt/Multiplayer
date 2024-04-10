using System;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client;

public static class CompSerialization
{
    public static Type[] gameCompTypes;
    public static Type[] worldCompTypes;
    public static Type[] mapCompTypes;

    public static Type[] thingCompTypes;
    public static Type[] hediffCompTypes;
    public static Type[] abilityCompTypes;
    public static Type[] worldObjectCompTypes;

    public static void Init()
    {
        thingCompTypes = TypeCache.AllSubclassesNonAbstractOrdered(typeof(ThingComp));
        hediffCompTypes = TypeCache.AllSubclassesNonAbstractOrdered(typeof(HediffComp));
        abilityCompTypes = TypeCache.AllSubclassesNonAbstractOrdered(typeof(AbilityComp));
        worldObjectCompTypes = TypeCache.AllSubclassesNonAbstractOrdered(typeof(WorldObjectComp));

        gameCompTypes = TypeCache.AllSubclassesNonAbstractOrdered(typeof(GameComponent));
        worldCompTypes = TypeCache.AllSubclassesNonAbstractOrdered(typeof(WorldComponent));
        mapCompTypes = TypeCache.AllSubclassesNonAbstractOrdered(typeof(MapComponent));
    }
}
