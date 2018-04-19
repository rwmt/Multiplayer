using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

        public static void SendCommand(this IConnection conn, CommandType action, int mapId, params object[] extra)
        {
            conn.Send(Packets.CLIENT_COMMAND, new object[] { action, mapId, ByteWriter.GetBytes(extra) });
        }

        public static bool IsList(this object o)
        {
            return o is IList &&
               o.GetType().IsGenericType &&
               o.GetType().GetGenericTypeDefinition().IsAssignableFrom(typeof(List<>));
        }

        public static bool IsDictionary(this object o)
        {
            return o is IDictionary &&
               o.GetType().IsGenericType &&
               o.GetType().GetGenericTypeDefinition().IsAssignableFrom(typeof(Dictionary<,>));
        }

        public static bool ListsEqual(this IList list1, IList list2)
        {
            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
                if (!Equals(list1[i], list2[i]))
                    return false;

            return true;
        }
    }

    public static class ByteWriter
    {
        public static void WriteObject(this MemoryStream stream, object obj)
        {
            if (obj is int @int)
            {
                stream.Write(BitConverter.GetBytes(@int));
            }
            else if (obj is ushort @ushort)
            {
                stream.Write(BitConverter.GetBytes(@ushort));
            }
            else if (obj is bool @bool)
            {
                stream.WriteByte(@bool ? (byte)1 : (byte)0);
            }
            else if (obj is byte @byte)
            {
                stream.WriteByte(@byte);
            }
            else if (obj is float @float)
            {
                stream.Write(BitConverter.GetBytes(@float));
            }
            else if (obj is double @double)
            {
                stream.Write(BitConverter.GetBytes(@double));
            }
            else if (obj is byte[] bytearr)
            {
                stream.WritePrefixed(bytearr);
            }
            else if (obj is Enum)
            {
                stream.WriteObject(Convert.ToInt32(obj));
            }
            else if (obj is string @string)
            {
                stream.WriteObject(Encoding.UTF8.GetBytes(@string));
            }
            else if (obj is Array arr)
            {
                stream.WriteObject(arr.Length);
                foreach (object o in arr)
                    stream.WriteObject(o);
            }
        }

        public static void Write(this MemoryStream stream, byte[] arr)
        {
            if (arr.Length > 0)
                stream.Write(arr, 0, arr.Length);
        }

        public static void WritePrefixed(this MemoryStream stream, byte[] arr)
        {
            stream.Write(BitConverter.GetBytes(arr.Length), 0, 4);
            if (arr.Length > 0)
                stream.Write(arr, 0, arr.Length);
        }

        public static byte[] GetBytes(params object[] data)
        {
            var stream = new MemoryStream();
            foreach (object o in data)
                stream.WriteObject(o);
            return stream.ToArray();
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
