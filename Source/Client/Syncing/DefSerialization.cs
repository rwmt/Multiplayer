using System;
using System.Collections.Generic;
using System.Reflection;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client
{
    public static class DefSerialization
    {
        public static Type[] DefTypes { get; private set; }
        private static Dictionary<Type, Type> hashableType = new();

        public static void Init()
        {
            DefTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(Def));

            foreach (var defType in DefTypes)
            {
                var hashable = defType;
                for (var t = defType; t != typeof(Def); t = t.BaseType)
                    if (!t.IsAbstract)
                        hashable = t;

                hashableType[defType] = hashable;
            }
        }

        private static Dictionary<Type, MethodInfo> methodCache = new();

        public static Def GetDef(Type defType, ushort hash)
        {
            return (Def)methodCache.AddOrGet(
                hashableType[defType],
                static t => typeof(DefDatabase<>).MakeGenericType(t).GetMethod("GetByShortHash")
            ).Invoke(null, new[] { (object)hash });
        }
    }
}
