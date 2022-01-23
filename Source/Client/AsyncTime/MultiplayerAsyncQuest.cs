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
        static void Prefix(MapParent settlement)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;

            var mapAsyncTimeComp = settlement.Map.AsyncTime();
            if (mapAsyncTimeComp != null)
            {
                MultiplayerAsyncQuest.TryRemoveCachedMap(mapAsyncTimeComp);
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

            MultiplayerAsyncQuest.CacheQuest(__result);
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
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;

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
        static IEnumerable<MethodInfo> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.TicksSinceAppeared));
            yield return AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.TicksSinceAccepted));
            yield return AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.TicksSinceCleanup));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.SetInitiallyAccepted));
            yield return AccessTools.Method(typeof(Quest), nameof(Quest.CleanupQuestParts));
        }

        static void Prefix(Quest __instance, ref AsyncTimeComp __state)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.GameComp.asyncTime) return;

            __state = MultiplayerAsyncQuest.TryGetCachedQuestMap(__instance);
            __state?.PreContext();
        }

        static void Postfix(AsyncTimeComp __state) => __state?.PostContext();
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
    static class SetContextForAccept
    {
        static void Prefix(Quest __instance, ref AsyncTimeComp __state)
        {
            if (Multiplayer.Client == null) return;

            //Make sure quest is accepted and async time is enabled and there are parts to this quest
            if (__instance.State != QuestState.NotYetAccepted || !Multiplayer.GameComp.asyncTime || __instance.parts == null) return;

            __state = MultiplayerAsyncQuest.CacheQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(AsyncTimeComp __state) => __state?.PostContext();
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    static class RemoveQuestFromCacheOnQuestEnd
    {
        static void Postfix(Quest __instance)
        {
            if (Multiplayer.Client == null) return;
            MultiplayerAsyncQuest.TryRemoveCachedQuest(__instance);
        }
    }

    public static class MultiplayerAsyncQuest
    {
        //Probably should move this to a class so its not a Dictionary of Lists
        private static readonly Dictionary<AsyncTimeComp, List<Quest>> mapQuestsCache = new Dictionary<AsyncTimeComp, List<Quest>>();
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

        /// <summary>
        /// Tries to remove Map from cache, then moves all Quests cached to that map to WorldQuestsCache
        /// </summary>
        /// <param name="mapAsyncTimeComp">Map to remove</param>
        /// <returns>If map is found in cache</returns>
        public static bool TryRemoveCachedMap(AsyncTimeComp mapAsyncTimeComp)
        {
            if (mapQuestsCache.TryGetValue(mapAsyncTimeComp, out var quests))
            {
                worldQuestsCache.AddRange(quests);
                return mapQuestsCache.Remove(mapAsyncTimeComp);
            }

            return false;
        }

        /// <summary>
        /// Tries to remove Quest from Map and World cache
        /// </summary>
        /// <param name="quest">Quest to remove</param>
        /// <returns>If quest is found in cache</returns>
        public static bool TryRemoveCachedQuest(Quest quest)
            => mapQuestsCache.SingleOrDefault(x => x.Value.Contains(quest)).Value?.Remove(quest) ?? false | worldQuestsCache.Remove(quest);

        /// <summary>
        /// Attempts to get the MapAsyncTimeComp cached for that quest
        /// </summary>
        /// <param name="quest">Quest to find MapAsyncTimeComp for</param>
        /// <returns>MapAsyncTimeComp for that quest or Null if not found</returns>
        public static AsyncTimeComp TryGetCachedQuestMap(Quest quest)
        {
            if (!Multiplayer.GameComp.asyncTime || quest == null) return null;
            return mapQuestsCache.FirstOrDefault(x => x.Value.Contains(quest)).Key;
        }

        /// <summary>
        /// Determines if a quest has a MapAsyncTimeComp, if it does it will cache it to the mapQuestsCache otherwise to worldQuestsCache
        /// </summary>
        /// <param name="quest">Quest to Cache</param>
        /// <returns>MapAsyncTimeComp for that quest or Null if not found</returns>
        public static AsyncTimeComp CacheQuest(Quest quest)
        {
            if (!Multiplayer.GameComp.asyncTime || quest == null) return null;

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

        /// <summary>
        /// Clears all cache for both mapQuestsCache and worldQuestsCache as they are static and requires manually clearing on load
        /// </summary>
        public static void Reset()
        {
            mapQuestsCache.Clear();
            worldQuestsCache.Clear();
        }

        /// <summary>
        /// Runs QuestTick() on all quests cached for worldQuestsCache
        /// </summary>
        public static void TickWorldQuests()
        {
            TickQuests(worldQuestsCache);
        }

        /// <summary>
        /// Runs QuestTick() on all quests cached for a specific map
        /// </summary>
        /// <param name="mapAsyncTimeComp">Map to tick Quests for</param>
        public static void TickMapQuests(AsyncTimeComp mapAsyncTimeComp)
        {
            if (mapQuestsCache.TryGetValue(mapAsyncTimeComp, out var quests))
            {
                TickQuests(quests);
            }
        }

        /// <summary>
        /// Runs QuestTick() on all quests passed
        /// </summary>
        /// <param name="quests">Quests to run QuestTick() on</param>
        private static void TickQuests(IEnumerable<Quest> quests)
        {
            foreach (var quest in quests)
            {
                quest.QuestTick();
            }
        }

        /// <summary>
        /// Attempts to find the map the quest will target, via its Quest Parts. Filtering on Map.IsPlayerHome and then check if it contains a MapAsyncTimeComp
        /// </summary>
        /// <param name="quest">Quest to check Map Parts on</param>
        /// <returns>MapAsyncTimeComp for that quest or Null if not found</returns>
        private static AsyncTimeComp TryGetQuestMap(Quest quest)
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

        /// <summary>
        /// Attempts to update or insert a new cache for a Map and Quest, will always attempt to remove the quest before adding in case it existed on another map.
        /// </summary>
        /// <param name="mapAsyncTimeComp">Map that will hold the quest</param>
        /// <param name="quest">Quest for the map</param>
        private static void UpsertQuestMap(AsyncTimeComp mapAsyncTimeComp, Quest quest)
        {
            //if there is more then one map with the quest something went wrong - removes quest object in case it changes map
            TryRemoveCachedQuest(quest);

            if (mapQuestsCache.TryGetValue(mapAsyncTimeComp, out var quests))
            {
                quests.Add(quest);
            }
            else
            {
                mapQuestsCache[mapAsyncTimeComp] = new List<Quest> { quest };
            }
        }
    }
}
