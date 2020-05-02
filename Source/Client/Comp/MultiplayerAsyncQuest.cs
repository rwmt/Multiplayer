using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Multiplayer.Client.Comp
{

    [HarmonyPatch(typeof(SettlementAbandonUtility), nameof(SettlementAbandonUtility.Abandon))]
    static class RemoveMapCacheOnAbandon
    {
        static void Prefix(MapParent mapParent)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            var mapAsyncTimeComp = mapParent.Map.AsyncTime();
            if (mapAsyncTimeComp != null)
            {
                MultiplayerAsyncQuest.RemoveCachedMap(mapAsyncTimeComp);
            }
        }
    }

    [HarmonyPatch(typeof(QuestGen), nameof(QuestGen.Generate))]
    static class CacheQuestAfterGeneration
    {
        static void Postfix(ref Quest __result)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            MultiplayerAsyncQuest.CacheQuest(__result);
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.QuestManagerTick))]
    [HarmonyPriority(Priority.Last)]
    static class DisableQuestManagerTickTest
    {
        static bool Prefix()
        {
            if (!MultiplayerWorldComp.asyncTime) return true;

            //Only tick world quest during world time
            MultiplayerAsyncQuest.TickWorldQuests();
            return false;
        }
    }

    //Clear Cache then Cache all quests on game load
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    [HarmonyPriority(Priority.Last)]
    static class SetupAsyncTimeLookupForQuests
    {
        static void Postfix()
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            MultiplayerAsyncQuest.Reset();

            foreach (var quest in Find.QuestManager.QuestsListForReading)
            {
                MultiplayerAsyncQuest.CacheQuest(quest);
            }
        }
    }

    [HarmonyPatch]
    static class SetContextForQuest
    {
        static IEnumerable<MethodInfo> TargetMethod()
        {
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.TicksSinceAppeared));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.TicksSinceAccepted));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.TicksSinceCleanup));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.SetInitiallyAccepted));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.CleanupQuestParts));
        }

        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            __state = MultiplayerAsyncQuest.TryGetCachedQuestMap(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
    public static class SetContextForAccept
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            //Make sure quest is accepted and async time is enabled and there are parts to this quest
            if (__instance.State != QuestState.NotYetAccepted || !MultiplayerWorldComp.asyncTime || __instance.parts == null) return;

            __state = MultiplayerAsyncQuest.CacheQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state) => __state?.PostContext();
    }

    public static class MultiplayerAsyncQuest
    {
        private static readonly Dictionary<MapAsyncTimeComp, List<Quest>> mapQuestsCache = new Dictionary<MapAsyncTimeComp, List<Quest>>();
        private static readonly List<Quest> worldQuestsCache = new List<Quest>();

        //List of quest parts that have mapParent as field
        private static readonly List<Type> questPartsToCheck = new List<Type>()
        {
            typeof(QuestPart_DropPods),
            typeof(QuestPart_SpawnThing),
            typeof(QuestPart_PawnsArrive),
            typeof(QuestPart_Incident),
            typeof(QuestPart_RandomRaid),
            typeof(QuestPart_ThreatsGenerator),
            typeof(QuestPart_Infestation),
            typeof(QuestPart_GameCondition),
            typeof(QuestPart_JoinPlayer),
            typeof(QuestPart_TrackWhenExitMentalState),
            typeof(QuestPart_RequirementsToAcceptBedroom),
            typeof(QuestPart_MechCluster),
            typeof(QuestPart_DropMonumentMarkerCopy),
            typeof(QuestPart_PawnsAvailable)
        };

        public static void RemoveCachedMap(MapAsyncTimeComp mapAsyncTimeComp)
        {
            mapQuestsCache.Remove(mapAsyncTimeComp);
        }

        public static List<Quest> TryGetQuestsForMap(MapAsyncTimeComp mapAsyncTimeComp)
        {
            if (!MultiplayerWorldComp.asyncTime || mapAsyncTimeComp == null) return null;
            mapQuestsCache.TryGetValue(mapAsyncTimeComp, out var quests);
            return quests;
        }

        public static MapAsyncTimeComp TryGetCachedQuestMap(Quest quest)
        {
            if (!MultiplayerWorldComp.asyncTime || quest == null) return null;
            return mapQuestsCache.FirstOrDefault(x => x.Value.Contains(quest)).Key;
        }

        public static MapAsyncTimeComp CacheQuest(Quest quest)
        {
            if (!MultiplayerWorldComp.asyncTime || quest == null) return null;

            //Check if quest targets players map
            var mapAsyncTimeComp = TryGetQuestMap(quest);

            //if it does add it to the cache for that map
            if (mapAsyncTimeComp != null)
            {
                UpsertQuestMap(mapAsyncTimeComp, quest);
                if (MpVersion.IsDebug)
                    Log.Message($"Info: Found AsyncTimeMap: '{mapAsyncTimeComp.map.Parent.Label}' for Quest: '{quest.name}'");
            }
            //if it doesn't add it to the world quest list
            else
            {
                worldQuestsCache.Add(quest);
                if (MpVersion.IsDebug)
                    Log.Message($"Info: Could not find AsyncTimeMap for Quest: '{quest.name}'");
            }

            return mapAsyncTimeComp;
        }

        //Clears all cached quest as this class is static
        public static void Reset()
        {
            mapQuestsCache.Clear();
            worldQuestsCache.Clear();
        }

        public static void TickWorldQuests()
        {
            foreach (var quest in worldQuestsCache)
            {
                quest.QuestTick();
            }
        }

        public static void TickMapQuests(MapAsyncTimeComp mapAsyncTimeComp)
        {
            if (mapQuestsCache.TryGetValue(mapAsyncTimeComp, out var quests))
            {
                foreach (var quest in quests)
                {
                    quest.QuestTick();
                }
            }
        }

        private static MapAsyncTimeComp TryGetQuestMap(Quest quest)
        {
            //Really terrible way to determine if any quest parts have a map which also has an async time
            foreach (var part in quest.parts.Where(x => x != null && questPartsToCheck.Contains(x.GetType())))
            {
                if (part.GetType().GetField("mapParent")?.GetValue(part) is MapParent mapParent)
                {
                    var mapAsyncTimeComp = mapParent.Map.IsPlayerHome ? mapParent.Map.AsyncTime() : null;
                    if (mapAsyncTimeComp != null) return mapAsyncTimeComp;
                }
            }

            return null;
        }

        private static void UpsertQuestMap(MapAsyncTimeComp mapAsyncTimeComp, Quest quest)
        {
            if (mapQuestsCache.TryGetValue(mapAsyncTimeComp, out var quests))
            {
                if (!quests.Contains(quest))
                {
                    quests.Add(quest);
                }
            }
            else
            {
                mapQuestsCache[mapAsyncTimeComp] = new List<Quest> { quest };
            }
        }
    }
}
