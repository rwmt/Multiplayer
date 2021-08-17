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

        public static object NewObjectNoCtor(Type type)
        {
            return FormatterServices.GetUninitializedObject(type);
        }

        public static T NewObjectNoCtor<T>()
        {
            return (T)NewObjectNoCtor(typeof(T));
        }

        // Copied from Harmony.PatchProcessor
        public static MethodBase GetMethod(Type type, string methodName, MethodType methodType, Type[] args)
        {
            if (type == null) return null;

            switch (methodType)
            {
                case MethodType.Normal:
                    if (methodName == null)
                        return null;
                    return AccessTools.DeclaredMethod(type, methodName, args);

                case MethodType.Getter:
                    if (methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(type, methodName).GetGetMethod(true);

                case MethodType.Setter:
                    if (methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(type, methodName).GetSetMethod(true);

                case MethodType.Constructor:
                    return AccessTools.DeclaredConstructor(type, args);

                case MethodType.StaticConstructor:
                    return AccessTools.GetDeclaredConstructors(type)
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

        public static To ShallowCopy<From, To>(From from, To to) where To : From
        {
            foreach (var f in AccessTools.GetDeclaredFields(typeof(From)))
                if (!f.IsStatic)
                    f.SetValue(to, f.GetValue(from));

            return to;
        }

        const string DisplayClassPrefix = "<>c__DisplayClass";
        const string SharedDisplayClass = "<>c";
        const string LambdaMethodInfix = "b__";
        const string LocalFunctionInfix = "g__";
        const string EnumerableStateMachineInfix = "d__";

        public static MethodInfo GetLambda(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, int lambdaOrdinal = 0)
        {
            var parent = GetMethod(parentType, parentMethod, parentMethodType, parentArgs);
            if (parent == null)
                throw new Exception($"Couldn't find parent method ({parentMethodType}) {parentType}::{parentMethod}");

            var parentId = GetMethodDebugId(parent);

            // Example: <>c__DisplayClass10_
            var displayClassPrefix = $"{DisplayClassPrefix}{parentId}_";

            // Example: <FillTab>b__0
            var lambdaNameShort = $"<{parent.Name}>{LambdaMethodInfix}{lambdaOrdinal}";

            // Capturing lambda
            var lambda = parentType.GetNestedTypes(AccessTools.all).
                Where(t => t.Name.StartsWith(displayClassPrefix)).
                SelectMany(t => t.GetDeclaredMethods()).
                FirstOrDefault(m => m.Name == lambdaNameShort);

            // Example: <FillTab>b__10_0
            var lambdaNameFull = $"<{parent.Name}>{LambdaMethodInfix}{parentId}_{lambdaOrdinal}";

            // Non-capturing lambda
            lambda ??= AccessTools.Method(parentType, lambdaNameFull);

            // Non-capturing cached lambda
            if (lambda == null && AccessTools.Inner(parentType, SharedDisplayClass) is Type sharedDisplayClass)
                lambda = AccessTools.Method(sharedDisplayClass, lambdaNameFull);

            if (lambda == null)
                throw new Exception($"Couldn't find lambda {lambdaOrdinal} in parent method {parentType}::{parent.Name} (parent method id: {parentId})");

            return lambda;
        }

        public static MethodInfo GetLocalFunc(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, string localFunc = null)
        {
            var parent = GetMethod(parentType, parentMethod, parentMethodType, parentArgs);
            if (parent == null)
                throw new Exception($"Couldn't find parent method ({parentMethodType}) {parentType}::{parentMethod}");

            var parentId = GetMethodDebugId(parent);

            // Example: <>c__DisplayClass10_
            var displayClassPrefix = $"{DisplayClassPrefix}{parentId}_";

            // Example: <DoWindowContents>g__Start|
            var localFuncPrefix = $"<{parentMethod}>{LocalFunctionInfix}{localFunc}|";

            // Example: <DoWindowContents>g__Start|10
            var localFuncPrefixWithId = $"<{parentMethod}>{LocalFunctionInfix}{localFunc}|{parentId}";

            var candidates = parentType.GetNestedTypes(AccessTools.all).
                Where(t => t.Name.StartsWith(displayClassPrefix)).
                SelectMany(t => t.GetDeclaredMethods()).
                Where(m => m.Name.StartsWith(localFuncPrefix)).
                Concat(parentType.GetDeclaredMethods().Where(m => m.Name.StartsWith(localFuncPrefixWithId))).
                ToArray();

            if (candidates.Length == 0)
                throw new Exception($"Couldn't find local function {localFunc} in parent method {parentType}::{parent.Name} (parent method id: {parentId})");

            if (candidates.Length > 1)
                throw new Exception($"Ambiguous local function {localFunc} in parent method {parentType}::{parent.Name} (parent method id: {parentId})");

            return candidates[0];
        }

        // Based on https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNameKind.cs
        // and https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNames.cs
        public static int GetMethodDebugId(MethodBase method)
        {
            string cur = null;

            try
            {
                // Try extract the debug id from the method body
                foreach (var inst in PatchProcessor.GetOriginalInstructions(method))
                {
                    // Example class names: <>c__DisplayClass10_0 or <CompGetGizmosExtra>d__7
                    if (inst.opcode == OpCodes.Newobj
                        && inst.operand is MethodBase m
                        && (cur = m.DeclaringType.Name) != null)
                    {
                        if (cur.StartsWith(DisplayClassPrefix))
                            return int.Parse(cur.Substring(DisplayClassPrefix.Length).Until('_'));
                        else if (cur.Contains(EnumerableStateMachineInfix))
                            return int.Parse(cur.After('>').Substring(EnumerableStateMachineInfix.Length));
                    }
                    // Example method names: <FillTab>b__10_0 or <DoWindowContents>g__Start|55_1
                    else if (
                        (inst.opcode == OpCodes.Ldftn || inst.opcode == OpCodes.Call)
                        && inst.operand is MethodBase f
                        && (cur = f.Name) != null
                        && cur.StartsWith("<")
                        && cur.After('>').CharacterCount('_') == 3)
                    {
                        if (cur.Contains(LambdaMethodInfix))
                            return int.Parse(cur.After('>').Substring(LambdaMethodInfix.Length).Until('_'));
                        else if (cur.Contains(LocalFunctionInfix))
                            return int.Parse(cur.After('|').Until('_'));
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Extracting debug id for {method.DeclaringType}::{method.Name} failed at {cur} with: {e.Message}");
            }

            throw new Exception($"Couldn't determine debug id for parent method {method.DeclaringType}::{method.Name}");
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
