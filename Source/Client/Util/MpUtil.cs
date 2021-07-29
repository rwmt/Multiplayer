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

using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class MpUtil
    {
        public static Vector2 Resolution => new Vector2(UI.screenWidth, UI.screenHeight);

        static Func<ICustomAttributeProvider, Type, bool> IsDefinedInternal;

        // Doesn't load the type
        public static bool HasAttr(ICustomAttributeProvider provider, Type attrType)
        {
            if (IsDefinedInternal == null)
                IsDefinedInternal = (Func<ICustomAttributeProvider, Type, bool>)Delegate.CreateDelegate(typeof(Func<ICustomAttributeProvider, Type, bool>), AccessTools.Method(Type.GetType("System.MonoCustomAttrs"), "IsDefinedInternal"));

            return IsDefinedInternal(provider, attrType);
        }

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
            foreach (var asm in LoadedModManager.RunningMods.SelectMany(m => m.assemblies.loadedAssemblies))
            {
                Type[] types = null;

                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    Log.Error($"Exception getting types in assembly {asm}");
                }

                if (types != null)
                    foreach (Type t in types)
                        yield return t;
            }
        }

        public unsafe static void MarkNoInlining(MethodBase method)
        {
            ushort* iflags = (ushort*)(method.MethodHandle.Value) + 1;
            *iflags |= (ushort)MethodImplOptions.NoInlining;
        }

        public static T NewObjectNoCtor<T>()
        {
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
        }

        // Copied from Harmony.PatchProcessor
        public static MethodBase GetOriginalMethod(HarmonyMethod attr)
        {
            if (attr.declaringType == null) return null;

            if (attr.methodType == null)
                attr.methodType = MethodType.Normal;

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

        public static void DrawRotatedLine(Vector2 center, float length, float width, float angle, Color color)
        {
            var size = new Vector2(length, width);
            var start = center - size / 2f;
            Rect screenRect = new Rect(start.x, start.y, length, width);
            Matrix4x4 m = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.TRS(-center, Quaternion.identity, Vector3.one);

            GL.PushMatrix();
            GL.MultMatrix(m);
            GUI.DrawTexture(screenRect, Widgets.LineTexAA, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
            GL.PopMatrix();
        }

        public static void Label(Rect rect, string label, GameFont? font = null, TextAnchor? anchor = null, Color? color = null)
        {
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            if (font != null)
                Text.Font = font.Value;

            if (anchor != null)
                Text.Anchor = anchor.Value;

            if (color != null)
                GUI.color = color.Value;

            Widgets.Label(rect, label);

            GUI.color = prevColor;
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
        }

        public static void ClearWindowStack()
        {
            Find.WindowStack.windows.Clear();
        }

        public static string RwDataFile(string filename)
        {
            return Path.Combine(GenFilePaths.SaveDataFolderPath, filename);
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

    [AttributeUsage(AttributeTargets.Class)]
    public class HotSwappableAttribute : Attribute
    {
    }

}
