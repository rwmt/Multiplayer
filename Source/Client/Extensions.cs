extern alias zip;

using Harmony;
using Ionic.Crc;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client
{
    public static class Extensions
    {
        public static IEnumerable<T> NotNull<T>(this IEnumerable<T> e)
        {
            return e.Where(t => t != null);
        }

        public static IEnumerable<Type> AllSubtypesAndSelf(this Type t)
        {
            return t.AllSubclasses().Concat(t);
        }

        public static IEnumerable<Type> AllImplementing(this Type t)
        {
            return from x in GenTypes.AllTypes where t.IsAssignableFrom(x) select x;
        }

        // Sets the current Faction.OfPlayer
        // Applies faction's world components
        // Applies faction's map components if map not null
        public static void PushFaction(this Map map, Faction faction)
        {
            faction = FactionContext.Push(faction);
            if (faction == null) return;

            Multiplayer.WorldComp?.SetFaction(faction);
            map?.MpComp().SetFaction(faction);
        }

        public static void PushFaction(this Map map, int factionId)
        {
            Faction faction = Find.FactionManager.GetById(factionId);
            map.PushFaction(faction);
        }

        public static void PopFaction()
        {
            PopFaction(null);
        }

        public static void PopFaction(this Container<Map>? c)
        {
            if (!c.HasValue) return;
            PopFaction(c.Value.Inner);
        }

        public static void PopFaction(this Map map)
        {
            Faction faction = FactionContext.Pop();
            if (faction == null) return;

            Multiplayer.WorldComp?.SetFaction(faction);
            map?.MpComp().SetFaction(faction);
        }

        public static Map GetMap(this ScheduledCommand cmd)
        {
            if (cmd.mapId == ScheduledCommand.Global) return null;
            return Find.Maps.Find(map => map.uniqueID == cmd.mapId);
        }

        public static Faction GetFaction(this ScheduledCommand cmd)
        {
            if (cmd.factionId == ScheduledCommand.NoFaction) return null;
            return Find.FactionManager.GetById(cmd.factionId);
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

        public static MapAsyncTimeComp AsyncTime(this Map map)
        {
            return Multiplayer.game?.asyncTimeComps.Find(c => c.map == map);
        }

        public static MultiplayerMapComp MpComp(this Map map)
        {
            return Multiplayer.game?.mapComps.Find(c => c.map == map);
        }

        public static T ThingReplacement<T>(this Map map, T thing) where T : Thing
        {
            foreach (Thing t in map.thingGrid.ThingsListAtFast(thing.positionInt))
                if (t.thingIDNumber == thing.thingIDNumber)
                    return (T)t;

            return null;
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

        public static Rect Down(this Rect rect, float y)
        {
            rect.y += y;
            return rect;
        }

        public static Rect Up(this Rect rect, float y)
        {
            rect.y -= y;
            return rect;
        }

        public static Rect Right(this Rect rect, float x)
        {
            rect.x += x;
            return rect;
        }

        public static Rect Left(this Rect rect, float x)
        {
            rect.x -= x;
            return rect;
        }

        public static Rect Width(this Rect rect, float width)
        {
            rect.width = width;
            return rect;
        }

        public static Rect CenterOn(this Rect rect, Rect on)
        {
            rect.x = on.x + (on.width - rect.width) / 2f;
            rect.y = on.y + (on.height - rect.height) / 2f;
            return rect;
        }

        public static Rect CenterOn(this Rect rect, Vector2 on)
        {
            rect.x = on.x - rect.width / 2f;
            rect.y = on.y - rect.height / 2f;
            return rect;
        }

        public static Vector2 BottomLeftCorner(this Rect rect)
        {
            return new Vector2(rect.xMin, rect.yMax);
        }

        public static Vector2 TopRightCorner(this Rect rect)
        {
            return new Vector2(rect.xMax, rect.yMin);
        }

        public static Faction GetById(this FactionManager manager, int factionId)
        {
            return manager.AllFactionsListForReading.Find(f => f.loadID == factionId);
        }

        public static void SendCommand(this IConnection conn, CommandType type, int mapId, byte[] data)
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(Convert.ToInt32(type));
            writer.WriteInt32(mapId);
            writer.WritePrefixedBytes(data);

            conn.Send(Packets.Client_Command, writer.ToArray());
        }

        public static void SendCommand(this IConnection conn, CommandType type, int mapId, params object[] data)
        {
            SendCommand(conn, type, mapId, ByteWriter.GetBytes(data));
        }

        public static MpContext MpContext(this ByteReader data)
        {
            if (data.context == null)
                data.context = new MpContext();
            return data.context as MpContext;
        }

        public static MpContext MpContext(this ByteWriter data)
        {
            if (data.context == null)
                data.context = new MpContext();
            return data.context as MpContext;
        }

        public static int Hash(this StackTrace trace)
        {
            int hash = 0;
            for (int i = 0; i < trace.GetFrames().Length; i++)
                hash = Gen.HashCombineInt(hash, trace.GetFrames()[i].GetMethod().MetadataToken);

            return hash;
        }

        public static int Hash(this MethodBase[] methods)
        {
            int hash = 0;
            for (int i = 0; i < methods.Length; i++)
                hash = Gen.HashCombineInt(hash, methods[i].MetadataToken);

            return hash;
        }

        public static byte[] GetBytes(this ZipEntry entry)
        {
            MemoryStream stream = new MemoryStream();
            entry.Extract(stream);
            return stream.ToArray();
        }

        public static string GetString(this ZipEntry entry)
        {
            return Encoding.UTF8.GetString(entry.GetBytes());
        }

        public static bool IsCompilerGenerated(this Type type)
        {
            while (type != null)
            {
                if (type.HasAttribute<CompilerGeneratedAttribute>()) return true;
                type = type.DeclaringType;
            }

            return false;
        }

        public static bool HasAttribute<T>(this ICustomAttributeProvider provider) where T : Attribute
        {
            var attrs = provider.GetCustomAttributes(false);
            if (attrs.Length == 0) return false;
            for (int i = 0; i < attrs.Length; i++)
                if (attrs[i] is T)
                    return true;
            return false;
        }

        public static void RemoveNulls(this IList list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }
        }

        public static MethodInfo[] GetDeclaredMethods(this Type type)
        {
            return type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }

        public static FieldInfo[] GetDeclaredInstanceFields(this Type type)
        {
            return type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        public static XmlTextReader ReadToNextElement(this XmlTextReader reader, string name = null)
        {
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && (name == null || reader.Name == name))
                    return reader;
            return null;
        }

        public static XmlTextReader SkipContents(this XmlTextReader reader)
        {
            reader.Skip();
            return reader;
        }

        public static string ReadFirstText(this XmlTextReader textReader)
        {
            while (textReader.Read())
                if (textReader.NodeType == XmlNodeType.Text)
                    return textReader.Value;
            return null;
        }

        public static IEnumerable<DiaNode> TraverseNodes(this DiaNode root, HashSet<DiaNode> processed = null)
        {
            if (processed == null)
                processed = new HashSet<DiaNode>();

            if (!processed.Add(root)) yield break;

            yield return root;

            foreach (var opt in root.options)
                if (opt.link != null)
                    foreach (var node in TraverseNodes(opt.link, processed))
                        yield return node;
        }

        public static void TryKill(this Process process)
        {
            try
            {
                process.Kill();
            }
            catch { }
        }

        public static string MethodDesc(this MethodBase method)
        {
            return $"{method.DeclaringType.Namespace}.{method.DeclaringType.Name}::{method.Name}({method.GetParameters().Join(p => $"{p.ParameterType.Namespace}.{p.ParameterType.Name}")})";
        }

        public static void Add_KeepRect(this WindowStack windows, Window window)
        {
            var rect = window.windowRect;
            windows.Add(window);
            window.windowRect = rect;
        }

        public static string[] ReadStrings(this XmlReader reader)
        {
            var sub = reader.ReadSubtree();

            var names = new List<string>();
            while (sub.ReadToFollowing("li"))
                names.Add(reader.ReadString());

            sub.Close();

            return names.ToArray();
        }

        public static FileInfo[] ModAssemblies(this ModMetaData mod) => ModAssemblies(mod.RootDir.FullName);
        public static FileInfo[] ModAssemblies(this ModContentPack mod) => ModAssemblies(mod.RootDir);

        private static FileInfo[] ModAssemblies(string modRoot)
        {
            var assemblies = new DirectoryInfo(Path.Combine(modRoot, "Assemblies"));
            if (!assemblies.Exists)
                return new FileInfo[0];
            return assemblies.GetFiles("*.*", SearchOption.AllDirectories).Where(f => f.Extension.ToLower() == ".dll").ToArray();
        }

        public static int CRC32(this FileInfo[] files)
        {
            return files.Select(f => new CRC32().GetCrc32(f.OpenRead())).Aggregate(0, (a, b) => Gen.HashCombineInt(a, b));
        }
    }

    public static class ClientDataExtensions
    {
        public static void WriteRect(this ByteWriter data, Rect rect)
        {
            data.WriteFloat(rect.x);
            data.WriteFloat(rect.y);
            data.WriteFloat(rect.width);
            data.WriteFloat(rect.height);
        }

        public static Rect ReadRect(this ByteReader data)
        {
            return new Rect(data.ReadFloat(), data.ReadFloat(), data.ReadFloat(), data.ReadFloat());
        }

        public static void WriteVectorXZ(this ByteWriter data, Vector3 vec)
        {
            data.WriteShort((short)(vec.x * 10f));
            data.WriteShort((short)(vec.z * 10f));
        }
    }

}
