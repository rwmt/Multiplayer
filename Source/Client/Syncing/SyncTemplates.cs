using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Multiplayer.Client
{
    public static class SyncTemplates
    {
        public static HarmonyMethod CreateTranspiler() => new HarmonyMethod(m_Transpiler) {
            priority = Priority.First
        };

        static bool General(int internalId, object instance, object[] args)
        {
            if (Multiplayer.ShouldSync) {
                Sync.internalIdToSyncMethod[internalId].DoSync(instance, args);
                return false;
            }

            return true;
        }

        static readonly MethodInfo m_General = SymbolExtensions.GetMethodInfo(() => General(0, null, Array.Empty<object>()));
        static readonly MethodInfo m_Transpiler = SymbolExtensions.GetMethodInfo(() => Transpiler(null, null, null));

        static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions, ILGenerator gen)
        {
            int idx;
            var label = gen.DefineLabel();
            var parameters = original.GetParameters();

            idx = 0;
            foreach (var pInfo in parameters) {
                var argIndex = idx++ + (original.IsStatic ? 0 : 1);
                var pType = pInfo.ParameterType;
                if (pInfo.IsOut) {
                    pType = pType.GetElementType();
                    yield return new CodeInstruction(OpCodes.Ldarg, argIndex);
                    yield return new CodeInstruction(OpCodes.Initobj, pType);
                }
            }

            yield return new CodeInstruction(OpCodes.Ldc_I4, Sync.methodBaseToInternalId[original]);
            yield return new CodeInstruction(original.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);

            yield return new CodeInstruction(OpCodes.Ldc_I4, parameters.Length);
            yield return new CodeInstruction(OpCodes.Newarr, typeof(object));

            idx = 0;
            var arrayIdx = 0;
            foreach (var pInfo in parameters) {
                var argIndex = idx++ + (original.IsStatic ? 0 : 1);
                var pType = pInfo.ParameterType;
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Ldc_I4, arrayIdx++);
                yield return new CodeInstruction(OpCodes.Ldarg, argIndex);
                if (pType.IsByRef) {
                    pType = pType.GetElementType();
                    if (pType.IsValueType) {
                        yield return new CodeInstruction(OpCodes.Ldobj, pType);
                        yield return new CodeInstruction(OpCodes.Box, pType);
                    } else
                        yield return new CodeInstruction(OpCodes.Ldind_Ref, pType);
                } else if (pType.IsValueType)
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
            if (type == typeof(void)) yield break;
            if (type.IsByRef)
                type = type.GetElementType();

            if (!type.IsValueType) {
                yield return new CodeInstruction(OpCodes.Ldnull);
                yield break;
            }
            if (type.IsValueType) {
                var v = generator.DeclareLocal(type);
                yield return new CodeInstruction(OpCodes.Ldloca, v);
                yield return new CodeInstruction(OpCodes.Initobj, type);
                yield return new CodeInstruction(OpCodes.Ldloc, v);
                yield break;
            }
        }

    }
}
