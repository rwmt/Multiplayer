using Harmony;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// Applies a normal Harmony patch, but allows multiple targets
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class MpPatch : Attribute
    {
        private Type type;
        private string typeName;
        private MethodInfo method;
        private Type[] argTypes;
        private string methodName;

        public Type Type
        {
            get
            {
                if (type != null)
                    return type;

                type = MpReflection.GetTypeByName(typeName);
                if (type == null)
                    throw new Exception("Couldn't find type " + typeName);

                return type;
            }
        }

        public MethodInfo Method
        {
            get
            {
                if (method != null)
                    return method;

                method = AccessTools.Method(Type, methodName, argTypes?.Length > 0 ? argTypes : null);
                if (method == null)
                    throw new Exception($"Couldn't find method {methodName} in type {Type}");

                return method;
            }
        }

        public MpPatch(Type type, string innerType, string methodName) : this($"{type}+{innerType}", methodName)
        {
        }

        public MpPatch(string typeName, string methodName)
        {
            this.typeName = typeName;
            this.methodName = methodName;
        }

        public MpPatch(Type type, string methodName) : this(type, methodName, (Type[])null)
        {
        }

        public MpPatch(Type type, string methodName, params Type[] argTypes)
        {
            this.type = type;
            this.methodName = methodName;
            this.argTypes = argTypes;
        }
    }

    public static class MpPatchExtensions
    {
        public static void DoAllMpPatches(this HarmonyInstance harmony)
        {
            foreach (Type type in Assembly.GetCallingAssembly().GetTypes())
            {
                harmony.DoMpPatches(type);
            }
        }

        // Use null as harmony instance to just collect the methods
        public static List<MethodBase> DoMpPatches(this HarmonyInstance harmony, Type type)
        {
            List<MethodBase> result = null;

            // On whole type
            foreach (MpPatch attr in type.AllAttributes<MpPatch>())
            {
                MethodInfo toPatch = attr.Method;
                HarmonyMethod harmonyMethod = new HarmonyMethod
                {
                    declaringType = toPatch.DeclaringType,
                    methodName = toPatch.Name,
                    argumentTypes = toPatch.GetParameters().Types()
                };

                if (harmony != null)
                    new PatchProcessor(harmony, type, harmonyMethod).Patch();

                if (result == null)
                    result = new List<MethodBase>();

                result.Add(toPatch);
            }

            // On methods
            foreach (MethodInfo m in type.GetDeclaredMethods().Where(m => m.IsStatic))
            {
                foreach (MpPatch attr in m.AllAttributes<MpPatch>())
                {
                    MethodInfo toPatch = attr.Method;
                    HarmonyMethod patch = new HarmonyMethod(m);

                    if (harmony != null)
                        harmony.Patch(toPatch, (attr is MpPrefix) ? patch : null, (attr is MpPostfix) ? patch : null, (attr is MpTranspiler) ? patch : null);

                    if (result == null)
                        result = new List<MethodBase>();

                    result.Add(toPatch);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Prefix method attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class MpPrefix : MpPatch
    {
        public MpPrefix(string typeName, string method) : base(typeName, method)
        {
        }

        public MpPrefix(Type type, string method, params Type[] argTypes) : base(type, method, argTypes)
        {
        }

        public MpPrefix(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }
    }

    /// <summary>
    /// Postfix method attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class MpPostfix : MpPatch
    {
        public MpPostfix(string typeName, string method) : base(typeName, method)
        {
        }

        public MpPostfix(Type type, string method, params Type[] argTypes) : base(type, method, argTypes)
        {
        }

        public MpPostfix(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }
    }

    /// <summary>
    /// Transpiler method attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class MpTranspiler : MpPatch
    {
        public MpTranspiler(string typeName, string method) : base(typeName, method)
        {
        }

        public MpTranspiler(Type type, string method, params Type[] argTypes) : base(type, method, argTypes)
        {
        }

        public MpTranspiler(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }
    }

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

    public static class MpPriority
    {
        public const int MpLast = Priority.Last - 1;
        public const int MpFirst = Priority.First + 1;
    }

}
