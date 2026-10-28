#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;

namespace Multiplayer.Client.Patches;

[HarmonyPatch]
static class KeepDebugResolution
{
    // Include every overload: RimWorld 1.7 also accepts FullScreenMode.
    static IEnumerable<MethodInfo> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(ResolutionUtility))
            .Where(method => method.Name == nameof(ResolutionUtility.SetResolutionRaw));

    static bool Prefix() => false;
}
#endif
