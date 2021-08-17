using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Multiplayer.Client
{
    public class CodeFinder
    {
        private MethodBase inMethod;
        private int pos;
        private List<CodeInstruction> list;

        public int Pos => pos;

        public CodeFinder(MethodBase inMethod, List<CodeInstruction> list)
        {
            this.inMethod = inMethod;
            this.list = list;
        }

        public CodeFinder Advance(int steps)
        {
            pos += steps;
            return this;
        }

        public CodeFinder Forward(OpCode opcode, object operand = null)
        {
            Find(opcode, operand, 1);
            return this;
        }

        public CodeFinder Backward(OpCode opcode, object operand = null)
        {
            Find(opcode, operand, -1);
            return this;
        }

        public CodeFinder Find(OpCode opcode, object operand, int direction)
        {
            while (pos < list.Count && pos >= 0)
            {
                if (Matches(list[pos], opcode, operand)) return this;
                pos += direction;
            }

            throw new Exception($"Couldn't find instruction ({opcode}) with operand ({operand}) in {inMethod.FullDescription()}.");
        }

        public CodeFinder Find(Predicate<CodeInstruction> predicate, int direction)
        {
            while (pos < list.Count && pos >= 0)
            {
                if (predicate(list[pos])) return this;
                pos += direction;
            }

            throw new Exception($"Couldn't find instruction using predicate ({predicate.Method}) in method {inMethod.FullDescription()}.");
        }

        public CodeFinder Start()
        {
            pos = 0;
            return this;
        }

        public CodeFinder End()
        {
            pos = list.Count - 1;
            return this;
        }

        private bool Matches(CodeInstruction inst, OpCode opcode, object operand)
        {
            if (inst.opcode != opcode) return false;
            if (operand == null) return true;

            if (opcode == OpCodes.Stloc_S)
                return (inst.operand as LocalBuilder).LocalIndex == (int)operand;

            return Equals(inst.operand, operand);
        }

        public static implicit operator int(CodeFinder finder)
        {
            return finder.pos;
        }
    }

}
