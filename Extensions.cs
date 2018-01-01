using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Verse;

namespace Multiplayer
{
    public static class Extensions
    {
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
            else if (obj is byte @byte)
            {
                stream.WriteByte(@byte);
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

        public static void SendAction(this Connection conn, ServerAction action, params object[] extra)
        {
            conn.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { action, Server.GetBytes(extra) });
        }

        public static IEnumerable<Type> AllSubtypesAndSelf(this Type t)
        {
            return t.AllSubclasses().Concat(t);
        }

        public static IEnumerable<Type> AllImplementing(this Type t)
        {
            return from x in GenTypes.AllTypes where t.IsAssignableFrom(x) select x;
        }

        public static void PushFaction(this Map map, Faction faction)
        {
            Faction f = FactionContext.Push(faction);
            if (map != null)
                map.GetComponent<MultiplayerMapComp>().SetFaction(f);
        }

        public static void PushFaction(this Map map, string factionId)
        {
            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId);
            map.PushFaction(faction);
        }

        public static void PopFaction(this Container<Map> c)
        {
            PopFaction(c.Value);
        }

        public static void PopFaction(this Map map)
        {
            Faction faction = FactionContext.Pop();
            if (map != null)
                map.GetComponent<MultiplayerMapComp>().SetFaction(faction);
        }
    }
}
