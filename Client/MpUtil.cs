using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class MpUtil
    {
        public static string FixedEllipsis()
        {
            int num = Mathf.FloorToInt(Time.realtimeSinceStartup) % 3;
            if (num == 0)
                return ".  ";
            if (num == 1)
                return ".. ";
            return "...";
        }

        public static IEnumerable<Type> AllModTypes()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
                for (int i = 0; i < mod.assemblies.loadedAssemblies.Count; i++)
                    foreach (Type t in mod.assemblies.loadedAssemblies[i].GetTypes())
                        yield return t;
        }

        public unsafe static void MarkNoInlining(MethodBase method)
        {
            ushort* iflags = (ushort*)(method.MethodHandle.Value) + 1;
            *iflags |= (ushort)MethodImplOptions.NoInlining;
        }
    }

}
