using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client;

internal static class RwTypeHelper
{
    private static Dictionary<Type, Type[]> cache = new();

    public static void Init()
    {
        cache[typeof(IStoreSettingsParent)] = RwImplSerialization.storageParents;
        cache[typeof(IPlantToGrowSettable)] = RwImplSerialization.plantToGrowSettables;
        cache[typeof(Designator)] = RwImplSerialization.designatorTypes;

        cache[typeof(ThingComp)] = CompSerialization.thingCompTypes;
        cache[typeof(AbilityComp)] = CompSerialization.abilityCompTypes;
        cache[typeof(WorldObjectComp)] = CompSerialization.worldObjectCompTypes;
        cache[typeof(HediffComp)] = CompSerialization.hediffCompTypes;

        cache[typeof(GameComponent)] = CompSerialization.gameCompTypes;
        cache[typeof(WorldComponent)] = CompSerialization.worldCompTypes;
        cache[typeof(MapComponent)] = CompSerialization.mapCompTypes;
    }

    internal static void FlushCache()
    {
        cache.Clear();
    }

    internal static Type[] GenTypeCache(Type type)
    {
        var types = GenTypes.AllTypes
            .Where(t => t != type && type.IsAssignableFrom(t))
            .OrderBy(t => t.IsInterface)
            .ToArray();

        cache[type] = types;
        return types;
    }

    internal static Type GetType(ushort index, Type baseType)
    {
        if (!cache.TryGetValue(baseType, out Type[] types))
            types = GenTypeCache(baseType);

        return types[index];
    }

    internal static ushort GetTypeIndex(Type type, Type baseType)
    {
        if (!cache.TryGetValue(baseType, out Type[] types))
            types = GenTypeCache(baseType);

        return (ushort) types.FindIndex(type);
    }
}
