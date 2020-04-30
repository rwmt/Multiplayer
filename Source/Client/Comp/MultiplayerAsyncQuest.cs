using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client.Comp
{
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
            foreach (var quest in MultiplayerAsyncQuest.WorldQuests)
            {
                quest.QuestTick();
            }
            return false;
        }
    }

    //Cache all quests on game load
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    [HarmonyPriority(Priority.Last)]
    static class SetupAsyncTimeLookupForQuests
    {
        static void Postfix()
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            foreach (var quest in Find.QuestManager.QuestsListForReading)
            {
                MultiplayerAsyncQuest.CacheQuest(quest);
            }
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.TicksSinceAppeared), MethodType.Getter)]
    static class SetContextForQuestTicksSinceAppeared
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            __state = MultiplayerAsyncQuest.GetCachedMapAsyncTimeCompForQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.TicksSinceAccepted), MethodType.Getter)]
    static class SetContextForQuestTicksSinceAccepted
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            __state = MultiplayerAsyncQuest.GetCachedMapAsyncTimeCompForQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.TicksSinceCleanup), MethodType.Getter)]
    static class SetContextForQuestTicksSinceCleanup
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            __state = MultiplayerAsyncQuest.GetCachedMapAsyncTimeCompForQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.SetInitiallyAccepted))]
    static class SetContextForQuestSetInitiallyAccepted
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            __state = MultiplayerAsyncQuest.GetCachedMapAsyncTimeCompForQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.CleanupQuestParts))]
    static class SetContextForQusetCleanupQuestParts
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            if (!MultiplayerWorldComp.asyncTime) return;

            __state = MultiplayerAsyncQuest.GetCachedMapAsyncTimeCompForQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
    public static class SetTicksForQuestsBasedOnMap
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
        //Should probably move this to its own class so its not a dictionary of lists.
        public static Dictionary<MapAsyncTimeComp, List<Quest>> QuestMapAsyncTimeCompsCache = new Dictionary<MapAsyncTimeComp, List<Quest>>();
        public static List<Quest> WorldQuests = new List<Quest>();

        //List of quest parts that have mapParent as field
        public static List<Type> mapParentQuestPartTypes = new List<Type>()
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

        public static MapAsyncTimeComp GetCachedMapAsyncTimeCompForQuest(Quest quest)
        {
            if (!MultiplayerWorldComp.asyncTime || quest == null) return null;
            return QuestMapAsyncTimeCompsCache.FirstOrDefault(x => x.Value.Contains(quest)).Key;
        }

        public static MapAsyncTimeComp CacheQuest(Quest quest)
        {
            if (!MultiplayerWorldComp.asyncTime || quest == null) return null;

            //Check if quest targets players map
            var mapAsyncTimeComp = GetMapAsyncTimeCompForQuest(quest);

            //if it does add it to the cache for that map
            if (mapAsyncTimeComp != null)
            {
                AddOrInsertNewQuestMapAsyncTimeComp(mapAsyncTimeComp, quest);
                if (MpVersion.IsDebug)
                    Log.Message($"Info: Found AsyncTimeMap: '{mapAsyncTimeComp.map.Parent.Label}' for Quest: '{quest.name}'");
            }
            //if it doesn't add it to the world quest list
            else
            {
                WorldQuests.Add(quest);
                if (MpVersion.IsDebug)
                    Log.Message($"Info: Could not find AsyncTimeMap for Quest: '{quest.name}'");
            }

            return mapAsyncTimeComp;
        }

        private static MapAsyncTimeComp GetMapAsyncTimeCompForQuest(Quest quest)
        {
            //Really terrible way to determine if any quest parts have a map which also has an async time
            foreach (var part in quest.parts.Where(x => x != null && mapParentQuestPartTypes.Contains(x.GetType())))
            {
                if (part.GetType().GetField("mapParent")?.GetValue(part) is MapParent mapParent)
                {
                    var mapAsyncTimeComp = mapParent.Map.IsPlayerHome ? mapParent.Map.AsyncTime() : null;
                    if (mapAsyncTimeComp != null) return mapAsyncTimeComp;
                }
            }

            return null;
        }

        private static void AddOrInsertNewQuestMapAsyncTimeComp(MapAsyncTimeComp mapAsyncTimeComp, Quest quest)
        {
            if (QuestMapAsyncTimeCompsCache.TryGetValue(mapAsyncTimeComp, out var quests))
            {
                quests.Add(quest);
            }
            else
            {
                QuestMapAsyncTimeCompsCache[mapAsyncTimeComp] = new List<Quest> { quest };
            }
        }
    }
}
