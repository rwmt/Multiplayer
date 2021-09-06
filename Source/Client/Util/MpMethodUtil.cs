using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Util
{
    public static class MpMethodUtil
    {
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
            if (lambda == null && AccessTools.Inner(parentType, SharedDisplayClass) is { } sharedDisplayClass)
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
                    return AccessTools
                        .GetDeclaredConstructors(type)
                        .FirstOrDefault(c => c.IsStatic);
            }

            return null;
        }
    }
}
