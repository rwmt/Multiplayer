using HarmonyLib;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// Applies a normal Harmony patch, but allows multiple targets
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    [MeansImplicitUse(ImplicitUseTargetFlags.WithMembers)]
    public abstract class MpPatch : Attribute
    {
        private Type type;
        private string typeName;
        private string methodName;
        private Type[] argTypes;
        private MethodType methodType;
        private int? lambdaOrdinal;

        private MethodBase method;

        public Type Type
        {
            get
            {
                if (type != null)
                    return type;

                type = MpReflection.GetTypeByName(typeName);
                if (type == null)
                    throw new Exception($"Couldn't find type {typeName}");

                return type;
            }
        }

        public MethodBase Method
        {
            get
            {
                if (method != null)
                    return method;

                if (lambdaOrdinal != null)
                    return MpMethodUtil.GetLambda(Type, methodName, methodType, argTypes, lambdaOrdinal.Value);

                method = MpMethodUtil.GetMethod(Type, methodName, methodType, argTypes);
                if (method == null)
                    throw new MissingMethodException($"Couldn't find method {methodName} in type {Type}");

                return method;
            }
        }

        protected MpPatch(Type type, string innerType, string methodName) : this($"{type}+{innerType}", methodName)
        {
        }

        protected MpPatch(string typeName, string methodName)
        {
            this.typeName = typeName;
            this.methodName = methodName;
        }

        protected MpPatch(Type type, string methodName, Type[] argTypes = null)
        {
            this.type = type;
            this.methodName = methodName;
            this.argTypes = argTypes;
        }

        protected MpPatch(Type type, MethodType methodType, Type[] argTypes = null)
        {
            this.type = type;
            this.methodType = methodType;
            this.argTypes = argTypes;
        }

        protected MpPatch(Type type, string methodName, int lambdaOrdinal)
        {
            this.type = type;
            this.methodName = methodName;
            this.lambdaOrdinal = lambdaOrdinal;
        }
    }

    public static class MpPatchExtensions
    {
        public static void DoAllMpPatches(this Harmony harmony)
        {
            foreach (Type type in Assembly.GetCallingAssembly().GetTypes())
            {
                harmony.DoMpPatches(type);
            }
        }

        // Use null as harmony instance to just collect the methods
        public static List<MethodBase> DoMpPatches(this Harmony harmony, Type type)
        {
            List<MethodBase> result = null;

            foreach (var m in type.GetDeclaredMethods().Where(m => m.IsStatic))
            {
                foreach (MpPatch attr in m.AllAttributes<MpPatch>())
                {
                    try
                    {
                        MethodBase toPatch = attr.Method;
                        HarmonyMethod patch = new HarmonyMethod(m);

                        if (harmony != null) {
                            harmony.PatchMeasure(
                                toPatch,
                                (attr is MpPrefix) ? patch : null,
                                (attr is MpPostfix) ? patch : null,
                                (attr is MpTranspiler) ? patch : null
                            );
                        }

                        result ??= new List<MethodBase>();
                        result.Add(toPatch);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"MpPatch {m.DeclaringType}.{m.Name} failed with exception: {e}");
                        Multiplayer.loadingErrors = true;
                    }
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

        public MpPrefix(Type type, string method, Type[] argTypes = null) : base(type, method, argTypes)
        {
        }

        public MpPrefix(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }

        public MpPrefix(Type parentType, string parentMethod, int lambdaOrdinal) : base(parentType, parentMethod, lambdaOrdinal)
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

        public MpPostfix(Type type, string method, Type[] argTypes = null) : base(type, method, argTypes)
        {
        }

        public MpPostfix(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }

        public MpPostfix(Type parentType, string parentMethod, int lambdaOrdinal) : base(parentType, parentMethod, lambdaOrdinal)
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

        public MpTranspiler(Type type, string method, Type[] argTypes) : base(type, method, argTypes)
        {
        }

        public MpTranspiler(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }

        public MpTranspiler(Type parentType, string parentMethod, int lambdaOrdinal) : base(parentType, parentMethod, lambdaOrdinal)
        {
        }
    }

}
