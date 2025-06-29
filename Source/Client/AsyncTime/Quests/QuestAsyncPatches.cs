using HarmonyLib;
using Multiplayer.Client.Quests;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Multiplayer.Client.AsyncTime.Quests
{

    [HarmonyPatch(typeof(SettlementAbandonUtility), nameof(SettlementAbandonUtility.Abandon))]
    static class RemoveQuestsForMap
    {
        static void Prefix(MapParent settlement)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;

            IAsyncQuestContext asyncQuestContext = settlement.Map.AsyncTime();

            if (asyncQuestContext != null)
            {
                Multiplayer.QuestManagerAsync.RemoveQuestsForContext(asyncQuestContext);
            }
        }
    }

    [HarmonyPatch(typeof(QuestGen), nameof(QuestGen.Generate))]
    static class CacheQuestAfterGeneration
    {
        static void Postfix(ref Quest __result)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;

            Multiplayer.QuestManagerAsync.AddQuest(__result);
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.QuestManagerTick))]
    [HarmonyPriority(Priority.Last)]
    static class DisableQuestManagerTickTest
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            if (!Multiplayer.GameComp.asyncTime) return true;

            return false;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    [HarmonyPriority(Priority.Last)]
    static class SetupAsyncTimeLookupForQuests
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;

            Multiplayer.QuestManagerAsync.SetQuestsAfterGameInit(Find.QuestManager.QuestsListForReading);
        }
    }

    [HarmonyPatch]
    static class SetContextForQuest
    {
        static IEnumerable<MethodInfo> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.TicksSinceAppeared));
            yield return AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.TicksSinceAccepted));
            yield return AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.TicksSinceCleanup));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.SetInitiallyAccepted));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.CleanupQuestParts));
        }

        static void Prefix(Quest __instance, ref IAsyncQuestContext __state, MethodBase __originalMethod)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;

            __state = Multiplayer.QuestManagerAsync.TryGetAsyncQuestContext(__instance);
            __state?.PreContext();
        }

        static void Postfix(IAsyncQuestContext __state) => __state?.PostContext();
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
    static class SetContextForAccept
    {
        static void Prefix(Quest __instance, ref IAsyncQuestContext __state)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;
            if (__instance.State != QuestState.NotYetAccepted) return;
            if (__instance.parts == null) return;

            __state = Multiplayer.QuestManagerAsync.AddQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(IAsyncQuestContext __state) => __state?.PostContext();
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    static class RemoveQuestFromCacheOnQuestEnd
    {
        // TODO: Quests that expire before being accepted and end up in Quest history aren’t removed from the cache.
        //       Add logic to detect these expired quests and ensure they’re purged.
        static void Postfix(Quest __instance)
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.QuestManagerAsync.EndQuest(__instance);
        }
    }
}
