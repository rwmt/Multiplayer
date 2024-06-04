using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Multiplayer.Common;
using Prepatcher;
using RimWorld;

namespace MultiplayerLoader;

public static class Prepatches
{
    [FreePatch]
    static void DontSetResolution(ModuleDefinition module)
    {
        if (MpVersion.IsDebug)
        {
            var resolutionUtilityType = module.ImportReference(typeof(ResolutionUtility)).Resolve();
            resolutionUtilityType.FindMethod(nameof(ResolutionUtility.SetResolutionRaw))!.Body.Instructions.Insert(
                0,
                Instruction.Create(OpCodes.Ret)
            );
        }
    }

    [FreePatch]
    static void ApplySyncPrepatches(ModuleDefinition module)
    {
        SyncRituals.ApplyPrepatches(module);
    }

    internal static void AddPrepatcherPrefix(ModuleDefinition module, int id, MethodInfo target, MethodInfo prefix)
    {
        var targetMethod = module.ImportReference(target).Resolve();
        var firstInst = targetMethod.Body.Instructions.First();

        IEnumerable<Instruction> PrefixInstructions()
        {
            yield return Instruction.Create(OpCodes.Ldc_I4, id); // Load index
            yield return Instruction.Create(OpCodes.Ldarg_0); // Load __instance

            // Create __args array
            yield return Instruction.Create(OpCodes.Ldc_I4, target.GetParameters().Length);
            yield return Instruction.Create(OpCodes.Newarr, module.TypeSystem.Object);

            int idx = 0;
            foreach (var pInfo in target.GetParameters())
            {
                var pType = pInfo.ParameterType;

                yield return Instruction.Create(OpCodes.Dup);
                yield return Instruction.Create(OpCodes.Ldc_I4, idx);
                yield return Instruction.Create(OpCodes.Ldarg, targetMethod.Parameters.ElementAt(idx));

                if (pType.IsByRef)
                    throw new Exception("Byrefs aren't handled");
                if (pType.IsValueType)
                    yield return Instruction.Create(OpCodes.Box, module.ImportReference(pType));

                yield return Instruction.Create(OpCodes.Stelem_Ref);

                idx++;
            }

            yield return Instruction.Create(OpCodes.Call, module.ImportReference(prefix));
            yield return Instruction.Create(OpCodes.Brtrue, firstInst);

            if (target.ReturnType == typeof(bool))
                yield return Instruction.Create(OpCodes.Ldc_I4_0);
            else if (target.ReturnType == typeof(void))
                yield return Instruction.Create(OpCodes.Nop);
            else
                throw new Exception($"Can't handle return type {target.ReturnType}");

            yield return Instruction.Create(OpCodes.Ret);
        }

        targetMethod.Body.Instructions.InsertRange(0, PrefixInstructions());
    }
}
