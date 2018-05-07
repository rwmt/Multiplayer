using Harmony;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Multiplayer.Client
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class MpPatch : Attribute
    {
        public readonly Type type;
        public readonly string typeName;
        public readonly string method;

        protected MpPatch(Type type, string innerType, string method) : this($"{type}+{innerType}", method)
        {
        }

        protected MpPatch(string typeName, string method)
        {
            this.typeName = typeName;
            this.method = method;
        }

        protected MpPatch(Type type, string method)
        {
            this.type = type;
            this.method = method;
        }

        public static List<MethodBase> DoPatches(Type type)
        {
            List<MethodBase> result = new List<MethodBase>();

            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                foreach (MpPatch attr in m.AllAttributes<MpPatch>())
                {
                    Type declaring = attr.type ?? MpReflection.GetTypeByName(attr.typeName);
                    if (declaring == null)
                        throw new Exception("Couldn't find type " + attr.typeName);

                    MethodInfo patched = AccessTools.Method(declaring, attr.method);
                    if (patched == null)
                        throw new Exception("Couldn't find method " + attr.method + " in type " + declaring.FullName);

                    bool postfix = attr.GetType() == typeof(MpPostfix);

                    HarmonyMethod patch = new HarmonyMethod(m);
                    Multiplayer.harmony.Patch(patched, postfix ? null : patch, postfix ? patch : null);

                    result.Add(patched);
                }
            }

            return result;
        }
    }

    public class MpPrefix : MpPatch
    {
        public MpPrefix(string typeName, string method) : base(typeName, method)
        {
        }

        public MpPrefix(Type type, string method) : base(type, method)
        {
        }

        public MpPrefix(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }
    }

    public class MpPostfix : MpPatch
    {
        public MpPostfix(string typeName, string method) : base(typeName, method)
        {
        }

        public MpPostfix(Type type, string method) : base(type, method)
        {
        }

        public MpPostfix(Type type, string innerType, string method) : base(type, innerType, method)
        {
        }
    }
}
