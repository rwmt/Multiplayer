using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Multiplayer.Client
{
    public static class SyncTemplates
    {
        public static HarmonyMethod CreateTranspiler() => new HarmonyMethod(m_Transpiler) {
            priority = Priority.First
        };

        static bool General(string typeName, int token, object instance, object[] args)
        {
            if (Multiplayer.ShouldSync) {
                var method = AccessTools.TypeByName(typeName).Module.ResolveMethod(token);
                Sync.syncMethods[method].DoSync(instance, args);
                return false;
            }

            return true;
        }

        static readonly MethodInfo m_General = SymbolExtensions.GetMethodInfo(() => General("", 0, null, new object[0]));
        static readonly MethodInfo m_Transpiler = SymbolExtensions.GetMethodInfo(() => Transpiler(null, null, null));
        static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions, ILGenerator gen)
        {
            int idx;
            var label = gen.DefineLabel();
            var parameter = original.GetParameters();

            idx = 0;
            foreach (var pInfo in parameter) {
                var argIndex = idx++ + (original.IsStatic ? 0 : 1);
                var pType = pInfo.ParameterType;
                if (pInfo.IsOut || pInfo.IsRetval) {
                    yield return new CodeInstruction(OpCodes.Ldarg, argIndex);
                    yield return CreateDefaultCodes(gen, pType).Last();
                    if (AccessTools.IsClass(pType))
                        yield return new CodeInstruction(OpCodes.Stind_Ref);
                    if (AccessTools.IsValue(pType)) {
                        if (pType == typeof(float))
                            yield return new CodeInstruction(OpCodes.Stind_R4, (float) 0);
                        else if (pType == typeof(double))
                            yield return new CodeInstruction(OpCodes.Stind_R8, (double) 0);
                        else if (pType == typeof(long))
                            yield return new CodeInstruction(OpCodes.Stind_I8, (long) 0);
                        else
                            yield return new CodeInstruction(OpCodes.Stind_I4, 0);
                    }
                }
            }

            yield return new CodeInstruction(OpCodes.Ldstr, original.DeclaringType.FullName);
            yield return new CodeInstruction(OpCodes.Ldc_I4, original.MetadataToken);
            yield return new CodeInstruction(original.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);

            yield return new CodeInstruction(OpCodes.Ldc_I4, parameter.Length);
            yield return new CodeInstruction(OpCodes.Newarr, typeof(object));

            idx = 0;
            var arrayIdx = 0;
            foreach (var pInfo in parameter) {
                var argIndex = idx++ + (original.IsStatic ? 0 : 1);
                var pType = pInfo.ParameterType;
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Ldc_I4, arrayIdx++);
                yield return new CodeInstruction(OpCodes.Ldarg, argIndex);
                if (pInfo.IsOut || pInfo.IsRetval) {
                    if (pType.IsValueType)
                        yield return new CodeInstruction(OpCodes.Ldobj, pType);
                    else
                        yield return new CodeInstruction(OpCodes.Ldind_Ref);
                }
                if (pType.IsValueType)
                    yield return new CodeInstruction(OpCodes.Box, pType);
                yield return new CodeInstruction(OpCodes.Stelem_Ref);
            }
            yield return new CodeInstruction(OpCodes.Call, m_General);
            yield return new CodeInstruction(OpCodes.Brtrue, label);
            foreach (var code in CreateDefaultCodes(gen, AccessTools.GetReturnedType(original)))
                yield return code;
            yield return new CodeInstruction(OpCodes.Ret);

            var list = instructions.ToList();
            list.First().labels.Add(label);
            foreach (var instruction in list)
                yield return instruction;
        }

        static IEnumerable<CodeInstruction> CreateDefaultCodes(ILGenerator generator, Type type)
        {
            if (type.IsByRef) type = type.GetElementType();

            if (AccessTools.IsClass(type)) {
                yield return new CodeInstruction(OpCodes.Ldnull);
                yield break;
            }
            if (AccessTools.IsStruct(type)) {
                var v = generator.DeclareLocal(type);
                yield return new CodeInstruction(OpCodes.Ldloca, v);
                yield return new CodeInstruction(OpCodes.Initobj, type);
                yield break;
            }
            if (AccessTools.IsValue(type)) {
                if (type == typeof(float))
                    yield return new CodeInstruction(OpCodes.Ldc_R4, (float) 0);
                else if (type == typeof(double))
                    yield return new CodeInstruction(OpCodes.Ldc_R8, (double) 0);
                else if (type == typeof(long))
                    yield return new CodeInstruction(OpCodes.Ldc_I8, (long) 0);
                else
                    yield return new CodeInstruction(OpCodes.Ldc_I4, 0);
                yield break;
            }
        }

    }
}
