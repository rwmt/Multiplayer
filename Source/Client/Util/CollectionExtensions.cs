using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static class CollectionExtensions
    {
        public static int? IndexNullable<T>(this IEnumerable<T> e, Func<T, bool> p)
        {
            int i = 0;
            foreach (T obj in e)
            {
                if (p(obj)) return i;
                ++i;
            }
            return null;
        }

        public static void RemoveNulls(this IList list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }
        }

        public static IEnumerable<T> AllNotNull<T>(this IEnumerable<T> e)
        {
            return e.Where(t => t != null);
        }

        public static void Insert<T>(this List<T> list, int index, params T[] items)
        {
            list.InsertRange(index, items);
        }

        public static void Add<T>(this List<T> list, params T[] items)
        {
            list.AddRange(items);
        }

        public static T RemoveFirst<T>(this List<T> list)
        {
            T elem = list[0];
            list.RemoveAt(0);
            return elem;
        }

        static bool ArraysEqual<T>(T[] a1, T[] a2)
        {
            if (ReferenceEquals(a1, a2))
                return true;

            if (a1 == null || a2 == null)
                return false;

            if (a1.Length != a2.Length)
                return false;

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a1.Length; i++)
                if (!comparer.Equals(a1[i], a2[i]))
                    return false;

            return true;
        }

        public static int RemoveAll<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, Func<TKey, TValue, bool> predicate)
        {
            List<TKey> list = null;
            int result;

            try
            {
                foreach (var (key, value) in dictionary)
                {
                    if (predicate(key, value))
                    {
                        list ??= SimplePool<List<TKey>>.Get();
                        list.Add(key);
                    }
                }

                if (list != null)
                {
                    int i = 0;
                    int count = list.Count;
                    while (i < count)
                    {
                        dictionary.Remove(list[i]);
                        i++;
                    }
                    result = list.Count;
                }
                else
                {
                    result = 0;
                }
            }
            finally
            {
                if (list != null)
                {
                    list.Clear();
                    SimplePool<List<TKey>>.Return(list);
                }
            }

            return result;
        }

        public static bool EqualAsSets<T>(this IEnumerable<T> enum1, IEnumerable<T> enum2)
        {
            return enum1.ToHashSet().SetEquals(enum2);
        }

        public static IEnumerable<(A a, B b)> Zip<A, B>(this IEnumerable<A> enumA, IEnumerable<B> enumB)
        {
            return Enumerable.Zip(enumA, enumB, (a, b) => (a, b));
        }

        /// <summary>
        /// Like ToDictionary but overrides duplicate keys
        /// </summary>
        public static Dictionary<K, V> ToDictionaryPermissive<T, K, V>(this IEnumerable<T> e, Func<T, K> keys, Func<T, V> values)
        {
            var dict = new Dictionary<K, V>();
            foreach (var item in e)
                dict[keys(item)] = values(item);
            return dict;
        }

        public static IEnumerable<V> GetOrEmpty<K, V>(this Dictionary<K, V> dict, K key)
        {
            if (dict.TryGetValue(key, out var value))
                yield return value;
        }
    }
}
