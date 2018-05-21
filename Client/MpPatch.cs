using Harmony;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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

        public MpPatch(Type type, string methodName, params Type[] argTypes)
        {
            this.type = type;
            this.methodName = methodName;
            this.argTypes = argTypes;
        }

        public static List<MethodBase> DoPatches(Type type)
        {
            List<MethodBase> result = new List<MethodBase>();

            foreach (MpPatch attr in type.AllAttributes<MpPatch>())
            {
                MethodInfo toPatch = attr.Method;
                HarmonyMethod harmonyMethod = new HarmonyMethod
                {
                    originalType = toPatch.DeclaringType,
                    methodName = toPatch.Name,
                    parameter = toPatch.GetParameters().Types()
                };

                new PatchProcessor(Multiplayer.harmony, type, harmonyMethod).Patch();
                result.Add(toPatch);
            }

            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                foreach (MpPatch attr in m.AllAttributes<MpPatch>())
                {
                    MethodInfo toPatch = attr.Method;
                    bool postfix = attr.GetType() == typeof(MpPostfix);

                    HarmonyMethod patch = new HarmonyMethod(m);
                    Multiplayer.harmony.Patch(toPatch, postfix ? null : patch, postfix ? patch : null);

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
}
