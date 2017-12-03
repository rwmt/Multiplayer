using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace ServerMod
{
    public static class Extensions
    {
        public static int Combine(this int i1, int i2)
        {
            return i1 ^ (i2 << 16 | (i2 >> 16));
        }

        public static IEnumerable<T> Add<T>(this IEnumerable<T> input, params T[] add)
        {
            foreach (T t in input)
                yield return t;

            foreach (T t in add)
                yield return t;
        }

        public static T[] Append<T>(this T[] arr1, T[] arr2)
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

        public static void WriteObject(this MemoryStream stream, object obj)
        {
            if (obj is int @int)
            {
                stream.Write(BitConverter.GetBytes(@int));
            }
            else if (obj is bool @bool)
            {
                stream.Write(BitConverter.GetBytes(@bool));
            }
            else if (obj is byte[] bytearr)
            {
                stream.WritePrefixed(bytearr);
            }
            else if (obj is Enum)
            {
                stream.WriteObject((int)obj);
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
    }
}
