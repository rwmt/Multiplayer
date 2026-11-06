using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Multiplayer.Client.Util;

internal static class HashCodeTranspiler
{
    public static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method
                && method.DeclaringType?.FullName == "System.HashCode" && method.Name == "Combine")
            {
                var args = method.GetParameters();
                if (args.Length is >= 2 and <= 8 && args.All(p => p.ParameterType == typeof(int)))
                    instruction.operand = AccessTools.Method(typeof(DeterministicHash), nameof(DeterministicHash.HashCombineInt),
                        Enumerable.Repeat(typeof(int), args.Length).ToArray());
            }

            // Retain labels and exception blocks on replaced calls.
            yield return instruction;
        }
    }
}
