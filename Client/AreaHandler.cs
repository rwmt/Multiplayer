using Harmony;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Area))]
    [HarmonyPatch(nameof(Area.Invert))]
    public static class AreaInvertPatch
    {
        static bool Prefix(Area __instance)
        {
            if (Multiplayer.client == null) return true;

            Messages.Message("Action not available in multiplayer.", MessageTypeDefOf.RejectInput);
            return false;
        }
    }

    [HarmonyPatch(typeof(AreaManager))]
    [HarmonyPatch("Remove")]
    public static class AreaRemovePatch
    {
        static bool Prefix(Area __instance)
        {
            if (Multiplayer.client == null) return true;

            Messages.Message("Action not available in multiplayer.", MessageTypeDefOf.RejectInput);
            return false;
        }
    }

    [HarmonyPatch(typeof(AreaManager))]
    [HarmonyPatch(nameof(AreaManager.TryMakeNewAllowed))]
    public static class AreaAddPatch
    {
        public static bool ignore;

        static bool Prefix(Area __instance)
        {
            if (Multiplayer.client == null || ignore || Current.ProgramState != ProgramState.Playing) return true;

            Messages.Message("Action not available in multiplayer.", MessageTypeDefOf.RejectInput);
            return false;
        }
    }

}
