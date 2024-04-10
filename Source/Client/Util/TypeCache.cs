using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Util;

public static class TypeCache
{
    internal static Dictionary<Type, List<Type>> subClasses = new(); // Type to all subtypes
    internal static Dictionary<Type, List<Type>> subClassesOrdered = new(); // Type to all subtypes, ordered by abstractness then name
    internal static Dictionary<Type, List<Type>> subClassesNonAbstract = new(); // Type to non-abstract subtypes
    internal static Dictionary<Type, List<Type>> interfaceImplementations = new(); // Interface to implementations
    internal static Dictionary<Type, List<Type>> interfaceImplementationsOrdered = new(); // Interface to implementations, ordered by name

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
                interfaceImplementations.GetOrAddNew(i).Add(type);
        }

        subClassesOrdered = subClasses.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.OrderBy(t => t.IsAbstract).ThenBy(t => t.Name).ToList()
        );

        interfaceImplementationsOrdered = interfaceImplementations.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.OrderBy(t => t.IsInterface).ThenBy(t => t.Name).ToList()
        );
    }

    internal static Dictionary<string, Type> typeByName = new();
    internal static Dictionary<string, Type> typeByFullName = new();

    internal static void CacheTypeByName()
    {
        foreach (var type in GenTypes.AllTypes)
        {
            typeByName.TryAdd(type.Name, type);
            typeByFullName.TryAdd(type.FullName, type);
        }
    }

    public static IEnumerable<Type> AllSubtypesAndSelf(this Type t)
    {
        return t.AllSubclasses().Concat(t);
    }

    public static IEnumerable<Type> AllImplementing(this Type type)
    {
        return interfaceImplementations.GetValueSafe(type) ?? Enumerable.Empty<Type>();
    }

    public static Type[] AllImplementationsOrdered(Type type)
    {
        return type.AllImplementing()
            .OrderBy(t => t.IsInterface)
            .ThenBy(t => t.Name)
            .ToArray();
    }

    public static Type[] AllSubclassesNonAbstractOrdered(Type type)
    {
        return type
            .AllSubclassesNonAbstract()
            .OrderBy(t => t.Name)
            .ToArray();
    }
}
