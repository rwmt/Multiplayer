extern alias zip;

using HarmonyLib;
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
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;
using Random = System.Random;

namespace Multiplayer.Client
{
    public static class Extensions
    {
        private static Regex methodNameCleaner = new Regex(@"(\?[0-9\-]+)");

        public static IEnumerable<Type> AllSubtypesAndSelf(this Type t)
        {
            return t.AllSubclasses().Concat(t);
        }

        public static IEnumerable<Type> AllImplementing(this Type type)
        {
            return Multiplayer.implementations.GetValueSafe(type) is { } list ? list : Array.Empty<Type>();
        }

        // Sets the current Faction.OfPlayer
        // Applies faction's world components
        // Applies faction's map components if map not null
        public static void PushFaction(this Map map, Faction f)
        {
            var faction = FactionContext.Push(f);
            if (faction == null) return;

            Multiplayer.WorldComp?.SetFaction(faction);
            map?.MpComp().SetFaction(faction);
        }

        public static void PushFaction(this Map map, int factionId)
        {
            Faction faction = Find.FactionManager.GetById(factionId);
            map.PushFaction(faction);
        }

        public static Faction PopFaction()
        {
            return PopFaction(null);
        }

        public static Faction PopFaction(this Container<Map>? c)
        {
            if (!c.HasValue) return null;
            return PopFaction(c.Value.Inner);
        }

        public static Faction PopFaction(this Map map)
        {
            Faction faction = FactionContext.Pop();
            if (faction == null) return null;

            Multiplayer.WorldComp?.SetFaction(faction);
            map?.MpComp().SetFaction(faction);

            return faction;
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

        public static AsyncTimeComp AsyncTime(this Map map)
        {
            var list = Multiplayer.game?.asyncTimeComps;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].map == map)
                    return list[i];
            return null;
        }

        public static MultiplayerMapComp MpComp(this Map map)
        {
            var list = Multiplayer.game?.mapComps;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].map == map)
                    return list[i];
            return null;
        }

        public static T ThingReplacement<T>(this Map map, T thing) where T : Thing
        {
            foreach (Thing t in map.thingGrid.ThingsListAtFast(thing.positionInt))
                if (t.thingIDNumber == thing.thingIDNumber)
                    return (T)t;

            return null;
        }

        public static Faction GetById(this FactionManager manager, int factionId)
        {
            var list = manager.AllFactionsListForReading;
            for (int i = 0; i < list.Count; i++)
                if (list[i].loadID == factionId)
                    return list[i];
            return null;
        }

        public static Map GetById(this List<Map> maps, int mapId)
        {
            for (int i = 0; i < maps.Count; i++)
                if (maps[i].uniqueID == mapId)
                    return maps[i];
            return null;
        }

        public static void SendCommand(this ConnectionBase conn, CommandType type, int mapId, byte[] data)
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(Convert.ToInt32(type));
            writer.WriteInt32(mapId);
            writer.WritePrefixedBytes(data);

            conn.Send(Packets.Client_Command, writer.ToArray());
        }

        public static void SendCommand(this ConnectionBase conn, CommandType type, int mapId, params object[] data)
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
            var traceToHash = new StringBuilder();
            for (int i = 0; i < trace.FrameCount; i++) {
                var method = trace.GetFrame(i).GetMethod();
                traceToHash.Append(methodNameCleaner.Replace(method.ToString(), "") + "\n");
            }

            return traceToHash.ToString().GetHashCode();
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

        /// <summary>
        /// Like Harmony.GeneralExtensions.FullDescription but without method type (static, abstract...) and return type
        /// </summary>
        /// <returns>
        /// [namespace].[type]::[method name]([param namespace].[param type name]...)
        /// "null" for a null method
        /// </returns>
        public static string MethodDesc(this MethodBase method)
        {
            if (method is null) return "null";
            var paramStr = method.GetParameters().Join(p => $"{p.ParameterType.Namespace}.{p.ParameterType.Name}");
            return $"{method.DeclaringType.Namespace}.{method.DeclaringType.Name}::{method.Name}({paramStr})";
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

        public static int CRC32(this FileInfo file)
        {
            using var stream = file.OpenRead();
            return new CRC32().GetCrc32(stream);
        }

        public static int AggregateHash(this IEnumerable<int> e)
        {
            return e.Aggregate(0, (a, b) => Gen.HashCombineInt(a, b));
        }

        public static string IgnorePrefix(this string str, string prefix)
        {
            return str.Substring(prefix.Length);
        }

        public static string[] Names(this ParameterInfo[] pinfo)
        {
            return pinfo.Select(pi => pi.Name).ToArray();
        }

        public static string After(this string s, char c)
        {
            if (s.IndexOf(c) == -1)
                throw new ArgumentException($"Char {c} not found in string {s}");
            return s.Substring(s.IndexOf(c) + 1);
        }

        public static string Until(this string s, char c)
        {
            if (s.IndexOf(c) == -1)
                throw new ArgumentException($"Char {c} not found in string {s}");
            return s.Substring(0, s.IndexOf(c));
        }

        public static string CamelSpace(this string str)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                if (i != 0 && str[i] is > 'A' and < 'Z')
                    builder.Append(' ');
                builder.Append(str[i]);
            }

            return builder.ToString();
        }

        public static string NormalizePath(this string path)
        {
            return path.Replace('\\', '/');
        }

        public static MethodInfo PatchMeasure(this Harmony harmony, MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
        {
            var watch = Multiplayer.harmonyWatch;
            var prev = watch.ElapsedMillisDouble();
            watch.Start();
            var result = harmony.Patch(original, prefix, postfix, transpiler, finalizer);
            watch.Stop();
            var took = watch.ElapsedMillisDouble() - prev;
            // if (took > 5)
                // Log.Message($"{took} ms: Patching {original.MethodDesc()}");
            return result;
        }

        public static T Cycle<T>(this T e) where T : Enum
        {
            T[] vals = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
            return vals[(vals.FindIndex(e) + 1) % vals.Length];
        }

        /// <summary>
        /// Returns an enumerable as a string, joined by a separator string. By default null values appear as an empty string.
        /// </summary>
        /// <param name="list">A list of elements to string together</param>
        /// <param name="separator">A string to inset between elements</param>
        /// <param name="explicitNullValues">If true, null elements will appear as "[null]"</param>
        public static string Join(this IEnumerable list, string separator, bool explicitNullValues = false)
        {
            if (list == null) return "";
            var builder = new StringBuilder();
            var useSeparator = false;
            foreach (var elem in list)
            {
                if (useSeparator) builder.Append(separator);
                useSeparator = true;
                if (elem != null || explicitNullValues)
                {
                    builder.Append(elem != null ? elem.ToString() : "[null]");
                }
            }
            return builder.ToString();
        }

        public static void WriteVectorXZ(this ByteWriter data, Vector3 vec)
        {
            data.WriteShort((short)(vec.x * 10f));
            data.WriteShort((short)(vec.z * 10f));
        }

        public static float Range(this Random rand, float start, float end)
        {
            return (float)(start + rand.NextDouble() * (end - start));
        }
    }

}
