using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public static class SimpleProfiler
    {
        // Inits (or clears) the profiler
        [DllImport("simple_profiler.dll", CharSet = CharSet.Ansi)]
        private static extern void init_profiler(string id);

        // Starts collecting profiler data
        [DllImport("simple_profiler.dll")]
        private static extern void start_profiler();

        // Pauses data collection
        [DllImport("simple_profiler.dll")]
        private static extern void pause_profiler();

        // Prints collected data to file
        [DllImport("simple_profiler.dll")]
        private static extern void print_profiler(string filename);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public static bool available;
        public static bool running;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void CheckAvailable()
        {
            available = GetModuleHandle("simple_profiler.dll").ToInt32() != 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Init(string id)
        {
            if (!available) return;
            init_profiler(id);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Start()
        {
            if (!available) return;
            start_profiler();
            running = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Pause()
        {
            if (!available) return;
            pause_profiler();
            running = false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Print(string file)
        {
            if (!available) return;
            print_profiler(file);
        }

        private static HashSet<int> printed = new HashSet<int>();

        public static void DumpMemory(object obj, StringBuilder builder)
        {
            printed.Clear();
            DumpMemory(obj, builder, 0);
        }

        private static IEnumerable<FieldInfo> AllFields(Type t)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in t.GetFields(flags))
            {
                if (field.DeclaringType == t)
                    yield return field;
            }

            var baseType = t.BaseType;
            if (baseType != null)
                foreach (FieldInfo f in AllFields(baseType))
                    yield return f;
        }

        private static void DumpMemory(object obj, StringBuilder builder, int depth)
        {
            if (obj == null)
            {
                builder.AppendLine(" null");
                return;
            }

            Type fType = obj.GetType();
            if (fType.IsPrimitive ||
                fType.IsEnum ||
                obj is string ||
                obj is Def ||
                obj is Type ||
                obj is RegionLink ||
                obj is Color32 ||
                obj is Delegate ||
                obj is IntVec3 ||
                obj is Rot4
            )
            {
                if (obj is string str && str.Contains("\n"))
                    obj = obj.GetHashCode();
                builder.Append(" ").Append(obj).AppendLine();
                return;
            }

            if (!fType.IsValueType && !printed.Add(obj.GetHashCode()))
            {
                builder.Append(" ").Append(obj).Append(" [r]").AppendLine();
                return;
            }

            if (IsCollection(obj) && obj is IEnumerable e)
            {
                builder.AppendLine();
                int i = 0;
                bool shouldPrintElements = fType != typeof(int[]) && fType != typeof(byte[]) && fType != typeof(bool[]);
                foreach (object elem in e)
                {
                    if (shouldPrintElements && elem != null)
                    {
                        builder.Append(' ', depth + 1).Append('[').Append(i).Append("]:");
                        DumpMemory(elem, builder, depth + 2);
                    }
                    i++;
                }
                builder.Append(' ', depth + 1).Append("[Size: ").Append(i).Append("]").AppendLine();
                return;
            }

            builder.AppendLine();

            foreach (FieldInfo f in AllFields(fType))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                if (f.IsStatic) continue;

                if (f.Name == "holdingOwner" || f.Name == "cachedLabelMouseover") continue;

                if (f.Name == "calcGrid" &&
                    (f.DeclaringType == typeof(PathFinder) ||
                    f.DeclaringType == typeof(WorldPathFinder)
                )) continue;

                builder.Append(' ', depth);
                builder.Append(f.Name).Append(":");
                object val = f.GetValue(obj);
                //Type fType = f.FieldType;

                if (f.FieldType == typeof(Map) ||
                    f.FieldType == typeof(Pawn_DrawTracker) ||
                    f.FieldType == typeof(WorldGrid) ||
                    f.FieldType == typeof(ThingGrid) ||
                    f.FieldType == typeof(MapDrawer) ||
                    f.FieldType == typeof(PathGrid) ||
                    f.FieldType == typeof(CellGrid) ||
                    f.FieldType == typeof(FloodFiller) ||
                    f.FieldType == typeof(FogGrid) ||
                    f.FieldType == typeof(ListerThings) ||
                    f.FieldType == typeof(LinkGrid) ||
                    f.FieldType == typeof(GlowFlooder) ||
                    f.FieldType == typeof(MapCellsInRandomOrder) ||
                    f.FieldType == typeof(GlowGrid) ||
                    f.FieldType == typeof(DeepResourceGrid) ||
                    f.FieldType == typeof(SnowGrid) ||
                    f.FieldType == typeof(RoofGrid)
                )
                {
                    builder.AppendLine();
                    continue;
                }

                DumpMemory(val, builder, depth + 1);
            }
        }

        public static bool IsCollection(object obj)
        {
            return (
                obj is ICollection ||
                obj is IList ||
                obj is IDictionary ||
                (
                    obj.GetType().IsGenericType &&
                    typeof(HashSet<>).IsAssignableFrom(obj.GetType().GetGenericTypeDefinition())
                )
            );
        }

        public static bool IsOfGenericType(this Type typeToCheck, Type genericType)
        {
            Type concreteType;
            return typeToCheck.IsOfGenericType(genericType, out concreteType);
        }

        public static bool IsOfGenericType(this Type typeToCheck, Type genericType, out Type concreteGenericType)
        {
            while (true)
            {
                concreteGenericType = null;

                if (genericType == null)
                    throw new ArgumentNullException(nameof(genericType));

                if (typeToCheck == null || typeToCheck == typeof(object))
                    return false;

                if (typeToCheck == genericType)
                {
                    concreteGenericType = typeToCheck;
                    return true;
                }

                if ((typeToCheck.IsGenericType ? typeToCheck.GetGenericTypeDefinition() : typeToCheck) == genericType)
                {
                    concreteGenericType = typeToCheck;
                    return true;
                }

                if (genericType.IsInterface)
                    foreach (var i in typeToCheck.GetInterfaces())
                        if (i.IsOfGenericType(genericType, out concreteGenericType))
                            return true;

                typeToCheck = typeToCheck.BaseType;
            }
        }
    }
}
