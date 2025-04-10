using System;
using System.Collections.Concurrent;

namespace Multiplayer.Common.Util;

internal static class EnumCache
{
    private static readonly ConcurrentDictionary<Type, Array> Cache = new();

    public static Array GetValues(Type enumType) => Cache.GetOrAdd(enumType, Enum.GetValues);
}
