using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Multiplayer.Common
{
    public static class Extensions
    {
        public static V AddOrGet<K, V>(this Dictionary<K, V> dict, K obj, V defaultValue)
        {
            if (!dict.TryGetValue(obj, out V value))
            {
                value = defaultValue;
                dict[obj] = value;
            }

            return value;
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

        public static T[] SubArray<T>(this T[] data, int index, int length)
        {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        public static IConnection GetConnection(this NetPeer peer)
        {
            return (IConnection)peer.Tag;
        }

        public static void SendCommand(this IConnection conn, CommandType action, int mapId, byte[] extra)
        {
            conn.Send(Packets.CLIENT_COMMAND, new object[] { action, mapId, extra });
        }

        public static void SendCommand(this IConnection conn, CommandType action, int mapId, params object[] extra)
        {
            conn.Send(Packets.CLIENT_COMMAND, new object[] { action, mapId, ByteWriter.GetBytes(extra) });
        }

        public static void Write(this MemoryStream stream, byte[] arr)
        {
            if (arr.Length > 0)
                stream.Write(arr, 0, arr.Length);
        }

        public static IEnumerable<T> AllAttributes<T>(this MemberInfo member) where T : Attribute
        {
            return Attribute.GetCustomAttributes(member, typeof(T)).Cast<T>();
        }
    }

    public static class EnumerableHelper
    {
        public static void CombineAndProcess<T, U>(IEnumerable<T> first, IEnumerable<U> second, Action<T, U> action)
        {
            using (var firstEnumerator = first.GetEnumerator())
            using (var secondEnumerator = second.GetEnumerator())
            {
                bool hasFirst = true;
                bool hasSecond = true;

                while ((hasFirst && (hasFirst = firstEnumerator.MoveNext())) | (hasSecond && (hasSecond = secondEnumerator.MoveNext())))
                {
                    action(hasFirst ? firstEnumerator.Current : default(T), hasSecond ? secondEnumerator.Current : default(U));
                }
            }
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
    }
}
