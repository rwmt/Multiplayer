using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate))]
    static class AddOrRemoveNeedMoodPatch
    {
        static void Postfix(Pawn_NeedsTracker __instance)
        {
            MultiplayerPawnComp comp = __instance.pawn.GetComp<MultiplayerPawnComp>();
            if (__instance.mood == null)
            {
                comp.thoughtsForInterface = null;
            }
            else
            {
                SituationalThoughtHandler thoughts = new SituationalThoughtHandler(__instance.pawn);
                comp.thoughtsForInterface = thoughts;
            }
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.Notify_SituationalThoughtsDirty))]
    static class NotifySituationalThoughtsPatch
    {
        private static bool ignore;

        static void Prefix(SituationalThoughtHandler __instance)
        {
            if (ignore) return;

            ignore = true;

            SituationalThoughtHandler thoughts = __instance.pawn.GetComp<MultiplayerPawnComp>().thoughtsForInterface;
            thoughts?.Notify_SituationalThoughtsDirty();

            ignore = false;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.RemoveExpiredThoughtsFromCache))]
    static class RemoveExpiredThoughtsFromCachePatch
    {
        private static bool ignore;

        static void Prefix(SituationalThoughtHandler __instance)
        {
            if (ignore) return;

            ignore = true;

            SituationalThoughtHandler thoughts = __instance.pawn.GetComp<MultiplayerPawnComp>().thoughtsForInterface;
            thoughts?.RemoveExpiredThoughtsFromCache();

            ignore = false;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.AppendMoodThoughts))]
    [HarmonyPriority(Priority.First)]
    static class AppendMoodThoughtsPatch
    {
        private static bool Cancel => Multiplayer.Client != null && !Multiplayer.Ticking && !Multiplayer.ExecutingCmds;
        private static bool ignore;

        static bool Prefix(SituationalThoughtHandler __instance, List<Thought> outThoughts)
        {
            if (!Cancel || ignore) return true;

            ignore = true;

            SituationalThoughtHandler thoughts = __instance.pawn.GetComp<MultiplayerPawnComp>().thoughtsForInterface;
            thoughts?.AppendMoodThoughts(outThoughts);

            ignore = false;

            return false;
        }
    }

    [HarmonyPatch(typeof(SituationalThoughtHandler), nameof(SituationalThoughtHandler.AppendSocialThoughts))]
    [HarmonyPriority(Priority.First)]
    static class AppendSocialThoughtsPatch
    {
        private static bool Cancel => Multiplayer.Client != null && !Multiplayer.Ticking && !Multiplayer.ExecutingCmds;
        private static bool ignore;

        static bool Prefix(SituationalThoughtHandler __instance, Pawn otherPawn, List<ISocialThought> outThoughts)
        {
            if (!Cancel || ignore) return true;

            ignore = true;

            SituationalThoughtHandler thoughts = __instance.pawn.GetComp<MultiplayerPawnComp>().thoughtsForInterface;
            thoughts?.AppendSocialThoughts(otherPawn, outThoughts);

            ignore = false;

            return false;
        }
    }
}
