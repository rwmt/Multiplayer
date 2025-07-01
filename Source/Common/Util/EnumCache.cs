using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Multiplayer.Common.Util;

internal static class EnumCache<T> where T: Enum
{
    public static readonly T[] Values = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
}

internal static class EnumCache
{
    private static readonly ConcurrentDictionary<Type, Array> Cache = new();

    public static Array Values(Type enumType) => Cache.GetOrAdd(enumType, Enum.GetValues);
}