using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class MpUtil
    {
        public static unsafe void MarkNoInlining(MethodBase method)
        {
            ushort* iflags = (ushort*)(method.MethodHandle.Value) + 1;
            *iflags |= (ushort)MethodImplOptions.NoInlining;
        }

        public static object NewObjectNoCtor(Type type)
        {
            return FormatterServices.GetUninitializedObject(type);
        }

        public static T NewObjectNoCtor<T>()
        {
            return (T)NewObjectNoCtor(typeof(T));
        }

        private static bool ipv6;
        private static bool ipv6Checked;

        // Socket.OSSupportsIPv6 doesn't seem to work
        public static bool SupportsIPv6()
        {
            if (!ipv6Checked)
            {
                try
                {
                    new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                    ipv6 = true;
                }
                catch
                {
                    ipv6 = false;
                }

                ipv6Checked = true;
            }

            return ipv6;
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

        public static string RwDataFile(string filename)
        {
            return Path.Combine(GenFilePaths.SaveDataFolderPath, filename);
        }

        public static To ShallowCopy<From, To>(From from, To to) where To : From
        {
            foreach (var f in AccessTools.GetDeclaredFields(typeof(From)))
                if (!f.IsStatic)
                    f.SetValue(to, f.GetValue(from));

            return to;
        }

        public static string TranslateWithDoubleNewLines(string keyBase, int count)
        {
            return Enumerable.Range(1, count).Select(n => (keyBase + n).Translate()).Join(delimiter: "\n\n");
        }

        public static string DelegateMethodInfo(MethodBase m)
        {
            return
                m == null
                    ? "No method"
                    : $"{m.DeclaringType.DeclaringType?.FullDescription()} {m.DeclaringType.FullDescription()} {m.Name}"
                        .Replace("<", "[").Replace(">", "]");
        }

        public static MethodBase GetOriginalFromHarmonyReplacement(long replacementAddr)
        {
            return HarmonySharedState.WithState(() =>
            {
                return HarmonySharedState.originals
                    .FirstOrDefault(kv => kv.Key.GetNativeStart().ToInt64() == replacementAddr).Value;
            });
        }
    }

    public struct Container<T>
    {
        public T Inner { get; }

        public Container(T value)
        {
            Inner = value;
        }

        public static implicit operator Container<T>(T value)
        {
            return new Container<T>(value);
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

    public class DefaultComparer<T> : IEqualityComparer<T>
    {
        public static DefaultComparer<T> Instance = new DefaultComparer<T>();

        public bool Equals(T x, T y)
        {
            return object.Equals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    public sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    public class FixedSizeQueue<T> : IEnumerable<T>
    {
        private Queue<T> q = new Queue<T>();

        public int Limit { get; set; }

        public void Enqueue(T obj)
        {
            q.Enqueue(obj);
            while (q.Count > Limit) q.Dequeue();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return q.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return q.GetEnumerator();
        }
    }

    public class ConcurrentSet<T> : IEnumerable<T>
    {
        private ConcurrentDictionary<T, object> dict = new ConcurrentDictionary<T, object>();

        public void Add(T t)
        {
            dict[t] = null;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return dict.Keys.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.Keys.GetEnumerator();
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class HotSwappableAttribute : Attribute
    {
        public HotSwappableAttribute()
        {

        }
    }

}
