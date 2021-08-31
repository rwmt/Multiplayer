using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static class CollectionExtensions
    {
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

        public static void RemoveAll<K, V>(this Dictionary<K, V> dict, Func<K, V, bool> predicate)
        {
            dict.RemoveAll(p => predicate(p.Key, p.Value));
        }

        public static void RemoveAll<T>(this List<T> list, Func<T, int, bool> predicate)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (predicate(list[i], i))
                    list.RemoveAt(i);
        }

        public static bool EqualAsSets<T>(this IEnumerable<T> enum1, IEnumerable<T> enum2)
        {
            return enum1.ToHashSet().SetEquals(enum2);
        }

        public static IEnumerable<(A a, B b)> Zip<A, B>(this IEnumerable<A> enumA, IEnumerable<B> enumB)
        {
            return Enumerable.Zip(enumA, enumB, (a, b) => (a, b));
        }
    }
}
