using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client
{
    public static class ExposableSerialization
    {
        private static readonly MethodInfo ReadExposableDefinition =
            AccessTools.Method(typeof(ScribeUtil), nameof(ScribeUtil.ReadExposable));

        private static Dictionary<Type, MethodInfo> readExposableCache = new();

        public static IExposable ReadExposable(Type type, byte[] data)
        {
            return (IExposable)readExposableCache.AddOrGet(type, newType => ReadExposableDefinition.MakeGenericMethod(newType)).Invoke(null, new[] { (object)data, null });
        }
    }
}
