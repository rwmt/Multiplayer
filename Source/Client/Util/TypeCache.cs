using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Util;

public static class TypeCache
{
    internal static Dictionary<Type, List<Type>> subClasses = new();
    internal static Dictionary<Type, List<Type>> subClassesNonAbstract = new();
    internal static Dictionary<Type, List<Type>> implementations = new();

    internal static void CacheTypeHierarchy()
    {
        foreach (var type in GenTypes.AllTypes)
        {
            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                subClasses.GetOrAddNew(baseType).Add(type);
                if (!type.IsAbstract)
                    subClassesNonAbstract.GetOrAddNew(baseType).Add(type);
            }

            foreach (var i in type.GetInterfaces())
                implementations.GetOrAddNew(i).Add(type);
        }
    }

    internal static Dictionary<string, Type> typeByName = new();
    internal static Dictionary<string, Type> typeByFullName = new();

    internal static void CacheTypeByName()
    {
        foreach (var type in GenTypes.AllTypes)
        {
            if (!typeByName.ContainsKey(type.Name))
                typeByName[type.Name] = type;

            if (!typeByFullName.ContainsKey(type.Name))
                typeByFullName[type.FullName] = type;
        }
    }

    public static IEnumerable<Type> AllSubtypesAndSelf(this Type t)
    {
        return t.AllSubclasses().Concat(t);
    }

    public static IEnumerable<Type> AllImplementing(this Type type)
    {
        return implementations.GetValueSafe(type) ?? Enumerable.Empty<Type>();
    }

}
