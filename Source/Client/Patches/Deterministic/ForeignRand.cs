using HarmonyLib;
using System;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    internal static class ForeignRand
    {
        public static void Patch()
        {
            MultiplayerMod.harmony.Patch(
                AccessTools.Method(typeof(System.Random), "InternalSample"),
                new HarmonyMethod(typeof(ForeignRand), nameof(SystemRandomPrefix))
            );
            Memory.DetourMethod(
                AccessTools.Method(typeof(UnityEngine.Random), "RandomRangeInt"),
                AccessTools.Method(typeof(Rand), nameof(Rand.Range), new Type[2] { typeof(int), typeof(int) })
            );
            Memory.DetourMethod(
                AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new Type[2] { typeof(float), typeof(float) }),
                AccessTools.Method(typeof(Rand), nameof(Rand.Range), new Type[2] { typeof(float), typeof(float) })
            );
            Memory.DetourMethod(
                AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.value)),
                AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value))
            );
            Memory.DetourMethod(
                AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.onUnitSphere)),
                AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.UnitVector3))
            );
            Memory.DetourMethod(
                AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.insideUnitCircle)),
                AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.InsideUnitCircle))
            );
            Memory.DetourMethod(
                AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.insideUnitSphere)),
                AccessTools.Method(typeof(ForeignRand), nameof(RandomInsideUnitSphere))
            );
            Memory.DetourMethod(
                AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.rotation)),
                AccessTools.Method(typeof(ForeignRand), nameof(RandomRotation))
            );
            Memory.DetourMethod(
                AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.rotationUniform)),
                AccessTools.Method(typeof(ForeignRand), nameof(RandomRotationUniform))
            );
        }

        static Vector3 RandomInsideUnitSphere()
        {
            Vector3 result;
            do
            {
                result = new Vector3(Rand.Value - 0.5f, Rand.Value - 0.5f, Rand.Value - 0.5f) * 2f;
            }
            while (result.sqrMagnitude > 1f);
            return result;
        }

        static Quaternion RandomRotation()
        {
            return new Quaternion(Rand.Range(-1f, 1f), Rand.Range(-1f, 1f), Rand.Range(-1f, 1f), Rand.Range(-1f, 1f)).normalized;
        }

        static Quaternion RandomRotationUniform()
        {
            float w, x, y, z;
            float sq;
            do
            {
                w = Rand.Range(-1f, 1f);
                x = Rand.Range(-1f, 1f);
                y = Rand.Range(-1f, 1f);
                z = Rand.Range(-1f, 1f);
                sq = w * w + x * x + y * y + z * z;
            }
            while (sq > 1f || sq == 0f);
            return new Quaternion(x, y, z, w).normalized;
        }

        static bool SystemRandomPrefix(ref int __result)
        {
            if (!UnityData.IsInMainThread || Multiplayer.InInterface)
            {
                return true;
            }
            __result = Math.Abs(Rand.Int);
            return false;
        }
    }
}
