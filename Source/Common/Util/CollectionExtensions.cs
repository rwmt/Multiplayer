using System.Collections.Generic;

namespace Multiplayer.Common.Util;

public static class CollectionExtensions
{
    public static void AddRange<TKey, TValue>(this IDictionary<TKey, TValue> dest, IDictionary<TKey, TValue> source)
    {
        foreach (KeyValuePair<TKey, TValue> item in source)
        {
            dest.Add(item.Key, item.Value);
        }
    }
}
