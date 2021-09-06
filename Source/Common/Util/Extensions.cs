using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Multiplayer.Common
{
    public static class Extensions
    {
        public static object GetDefaultValue(this Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        public static V AddOrGet<K, V>(this Dictionary<K, V> dict, K key, Func<K, V> defaultValueGetter)
        {
            if (!dict.TryGetValue(key, out V value))
            {
                value = defaultValueGetter(key);
                dict[key] = value;
            }

            return value;
        }

        public static V GetOrAddNew<K, V>(this Dictionary<K, V> dict, K obj) where V : new()
        {
            return AddOrGet(dict, obj, k => new V());
        }

        public static IEnumerable<T> ToEnumerable<T>(this T input)
        {
            yield return input;
        }

        public static int Combine(this int i1, int i2)
        {
            return i1 ^ (i2 << 16 | (i2 >> 16));
        }

        public static T[] Append<T>(this T[] arr1, params T[] arr2)
        {
            T[] result = new T[arr1.Length + arr2.Length];
            Array.Copy(arr1, 0, result, 0, arr1.Length);
            Array.Copy(arr2, 0, result, arr1.Length, arr2.Length);
            return result;
        }

        public static T[] SubArray<T>(this T[] data, int index)
        {
            return SubArray(data, index, data.Length - index);
        }

        public static T[] SubArray<T>(this T[] data, int index, int length)
        {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        public static LiteNetConnection GetConnection(this NetPeer peer)
        {
            return (LiteNetConnection)peer.Tag;
        }

        public static void WriteBytes(this MemoryStream stream, byte[] arr)
        {
            stream.Write(arr, 0, arr.Length);
        }

        public static IEnumerable<T> AllAttributes<T>(this MemberInfo member) where T : Attribute
        {
            return Attribute.GetCustomAttributes(member).OfType<T>();
        }

        public static T GetAttribute<T>(this MemberInfo member) where T : Attribute
        {
            return AllAttributes<T>(member).FirstOrDefault();
        }

        public static void Restart(this Stopwatch watch)
        {
            watch.Reset();
            watch.Start();
        }

        public static bool HasFlag(this Enum on, Enum flag)
        {
            ulong num = Convert.ToUInt64(flag);
            return (Convert.ToUInt64(on) & num) == num;
        }

        public static int FindIndex<T>(this T[] arr, T t)
        {
            return Array.IndexOf(arr, t);
        }

        public static double ElapsedMillisDouble(this Stopwatch watch)
        {
            return (double)watch.ElapsedTicks / Stopwatch.Frequency * 1000;
        }

        public static string ToHexString(this byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }
    }

    public static class XmlExtensions
    {
        public static void SelectAndRemove(this XmlNode node, string xpath)
        {
            XmlNodeList nodes = node.SelectNodes(xpath);
            foreach (XmlNode selected in nodes)
                selected.RemoveFromParent();
        }

        public static void RemoveChildIfPresent(this XmlNode node, string child)
        {
            XmlNode childNode = node[child];
            if (childNode != null)
                node.RemoveChild(childNode);
        }

        public static void RemoveFromParent(this XmlNode node)
        {
            if (node == null) return;
            node.ParentNode.RemoveChild(node);
        }

        public static void AddNode(this XmlNode parent, string name, string value)
        {
            XmlNode node = parent.OwnerDocument.CreateElement(name);
            node.InnerText = value;
            parent.AppendChild(node);
        }
    }

    public static class Utils
    {
        public static byte[] GetMD5(IEnumerable<string> strings)
        {
            using (var hash = MD5.Create())
            {
                foreach (string s in strings)
                {
                    byte[] data = Encoding.UTF8.GetBytes(s);
                    hash.TransformBlock(data, 0, data.Length, null, 0);
                }

                hash.TransformFinalBlock(new byte[0], 0, 0);

                return hash.Hash;
            }
        }

        public static long MillisNow => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
    }

}
