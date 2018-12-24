using Harmony;
using Multiplayer.Common;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class MpUtil
    {
        public static string FixedEllipsis()
        {
            int num = Mathf.FloorToInt(Time.realtimeSinceStartup) % 3;
            if (num == 0)
                return ".  ";
            if (num == 1)
                return ".. ";
            return "...";
        }

        public static IEnumerable<Type> AllModTypes()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
                for (int i = 0; i < mod.assemblies.loadedAssemblies.Count; i++)
                    foreach (Type t in mod.assemblies.loadedAssemblies[i].GetTypes())
                        yield return t;
        }

        public unsafe static void MarkNoInlining(MethodBase method)
        {
            ushort* iflags = (ushort*)(method.MethodHandle.Value) + 1;
            *iflags |= (ushort)MethodImplOptions.NoInlining;
        }

        public static T UninitializedObject<T>()
        {
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
        }

        // Copied from Harmony
        public static MethodBase GetOriginalMethod(HarmonyMethod attr)
        {
            if (attr.declaringType == null) return null;

            switch (attr.methodType)
            {
                case MethodType.Normal:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredMethod(attr.declaringType, attr.methodName, attr.argumentTypes);

                case MethodType.Getter:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName).GetGetMethod(true);

                case MethodType.Setter:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName).GetSetMethod(true);

                case MethodType.Constructor:
                    return AccessTools.DeclaredConstructor(attr.declaringType, attr.argumentTypes);

                case MethodType.StaticConstructor:
                    return AccessTools.GetDeclaredConstructors(attr.declaringType)
                        .Where(c => c.IsStatic)
                        .FirstOrDefault();
            }

            return null;
        }

        // https://stackoverflow.com/a/27376368
        public static string GetLocalIpAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint.Address.ToString();
                }
            }
            catch
            {
                return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork).ToString();
            }
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class HotSwappableAttribute : Attribute
    {
    }

    public struct Container<T>
    {
        private readonly T _value;
        public T Inner => _value;

        public Container(T value)
        {
            _value = value;
        }

        public static implicit operator Container<T>(T value)
        {
            return new Container<T>(value);
        }
    }

    public class OrderedDict<K, V> : IEnumerable
    {
        private List<K> list = new List<K>();
        private Dictionary<K, V> dict = new Dictionary<K, V>();

        public K this[int index]
        {
            get => list[index];
        }

        public V this[K key]
        {
            get => dict[key];
        }

        public void Add(K key, V value)
        {
            dict.Add(key, value);
            list.Add(key);
        }

        public void Insert(int index, K key, V value)
        {
            dict.Add(key, value);
            list.Insert(index, key);
        }

        public bool TryGetValue(K key, out V value)
        {
            value = default(V);
            return dict.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.GetEnumerator();
        }
    }

    public class UniqueList<T> : IEnumerable<T>
    {
        private List<T> list = new List<T>();
        private HashSet<T> set = new HashSet<T>();

        public int Count => list.Count;
        public T this[int index] => list[index];

        public bool Add(T t)
        {
            if (set.Add(t))
            {
                list.Add(t);
                return true;
            }

            return false;
        }

        public T[] ToArray()
        {
            return list.ToArray();
        }

        public bool Contains(T t)
        {
            return set.Contains(t);
        }

        public int IndexOf(T t)
        {
            return list.IndexOf(t);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return list.GetEnumerator();
        }
    }

}
