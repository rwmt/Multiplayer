using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Multiplayer.Client.Util
{
    public static class ILUtil
    {
        public const int LocalIndexNotFound = -1;

        public static bool TryGetStoredLocalIndex(this CodeInstruction ci, out int index)
        {
            index = LocalIndexNotFound;

            if (ci.opcode == OpCodes.Stloc_0) { index = 0; return true; }
            if (ci.opcode == OpCodes.Stloc_1) { index = 1; return true; }
            if (ci.opcode == OpCodes.Stloc_2) { index = 2; return true; }
            if (ci.opcode == OpCodes.Stloc_3) { index = 3; return true; }
            if (ci.opcode == OpCodes.Stloc_S || ci.opcode == OpCodes.Stloc)
            {
                if (ci.operand is LocalBuilder lbs) { index = lbs.LocalIndex; return true; }
                if (ci.operand is int idx) { index = idx; return true; }
            }

            return false;
        }

        public static bool TryGetLoadedLocalIndex(this CodeInstruction ci, out int index)
        {
            index = LocalIndexNotFound;

            if (ci.opcode == OpCodes.Ldloc_0) { index = 0; return true; }
            if (ci.opcode == OpCodes.Ldloc_1) { index = 1; return true; }
            if (ci.opcode == OpCodes.Ldloc_2) { index = 2; return true; }
            if (ci.opcode == OpCodes.Ldloc_3) { index = 3; return true; }
            if (ci.opcode == OpCodes.Ldloc_S || ci.opcode == OpCodes.Ldloc)
            {
                if (ci.operand is LocalBuilder lbs) { index = lbs.LocalIndex; return true; }
                if (ci.operand is int idx) { index = idx; return true; }
            }

            return false;
        }

        public static CodeInstruction Ldloc(int idx) => idx switch
        {
            0 => new(OpCodes.Ldloc_0),
            1 => new(OpCodes.Ldloc_1),
            2 => new(OpCodes.Ldloc_2),
            3 => new(OpCodes.Ldloc_3),
            _ => new(OpCodes.Ldloc_S, (byte)idx)
        };

        public static int FindLocalIndexStoredAfterConstructor(List<CodeInstruction> instructions, ConstructorInfo constructor)
        {
            for (int i = 0; i < instructions.Count - 1; i++)
                if (instructions[i].opcode == OpCodes.Newobj
                    && Equals(instructions[i].operand, constructor)
                    && instructions[i + 1].TryGetStoredLocalIndex(out var index))
                    return index;

            return LocalIndexNotFound;
        }
    }
}

