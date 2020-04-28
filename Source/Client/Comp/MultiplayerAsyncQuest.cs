using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client.Comp
{
#if false
    [HarmonyPatch(typeof(Quest), nameof(Quest.TicksSinceAppeared), MethodType.Getter)]
    static class SetTicksSinceAppearedForQuestsBasedOnMap
    {
        static bool Prefix(Quest __instance, ref int __result)
        {
            __result = MultiplayerAsyncQuest.SetAsyncTimeIfNotExistWorldTime(__instance, __instance.appearanceTick);
            return true;
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.TicksSinceAccepted), MethodType.Getter)]
    static class SetTicksSinceAcceptedForQuestsBasedOnMap
    {
        static bool Prefix(Quest __instance, ref int __result)
        {
            if (__instance.acceptanceTick >= 0)
            {
                __result = MultiplayerAsyncQuest.SetAsyncTimeIfNotExistWorldTime(__instance, __instance.acceptanceTick);
            }
            else
            {
                __result = -1;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.TicksSinceCleanup), MethodType.Getter)]
    static class SetTicksSinceCleanupForQuestsBasedOnMap
    {
        static bool Prefix(Quest __instance, ref int __result)
        {
            if (!__instance.cleanedUp)
            {
                __result = -1;
                return true;
            }

            __result = MultiplayerAsyncQuest.SetAsyncTimeIfNotExistWorldTime(__instance, __instance.cleanupTick);
            return true;
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.SetInitiallyAccepted))]
    static class SetInitiallyAcceptedForQuestsBasedOnMap
    {
        static bool Prefix(Quest __instance)
        {
            __instance.acceptanceTick = MultiplayerAsyncQuest.SetAsyncTimeIfNotExistWorldTime(__instance);
            __instance.ticksUntilAcceptanceExpiry = -1;
            __instance.initiallyAccepted = true;
            return true;
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.CleanupQuestParts))]
    static class SetCleanupQuestPartsForQuestsBasedOnMap
    {
        static bool Prefix(Quest __instance)
        {
            if (__instance.cleanedUp)
            {
                return true;
            }
            __instance.cleanupTick = MultiplayerAsyncQuest.SetAsyncTimeIfNotExistWorldTime(__instance);
            for (int i = 0; i < __instance.parts.Count; i++)
            {
                try
                {
                    __instance.parts[i].Cleanup();
                }
                catch (Exception arg)
                {
                    Log.Error("Error in QuestPart cleanup: " + arg, false);
                }
            }
            __instance.cleanedUp = true;
            return true;
        }
    }
#endif
    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
    public static class SetTicksForQuestsBasedOnMap
    {
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

        public static Dictionary<Quest, MapAsyncTimeComp> QuestAsyncTime = new Dictionary<Quest, MapAsyncTimeComp>();

        static bool Prefix(Quest __instance, Pawn by)
        {
            if (__instance.State != QuestState.NotYetAccepted)
            {
                return true;
            }

            //Have to cache this since its a lookup
            MapAsyncTimeComp mapAsyncTimeComp = null;

            //Only attempt this if asyncTime is enabled
            if (MultiplayerWorldComp.asyncTime)
            {
                //Really terrible way to determine if a any quest parts have a map which also has an async time
                if (__instance.parts != null)
                {
                    foreach (var part in __instance.parts.Where(x => mapParentQuestPartTypes.Contains(x.GetType())))
                    {
                        var mapParent = part.GetType().GetField("mapParent").GetValue(part) as MapParent;
                        if (mapParent != null)
                        {
                            mapAsyncTimeComp = GetAsyncTimeFromMapParent(mapParent);
                            if (mapAsyncTimeComp != null) break;
                        }
                    }
                }
            }

            //Doesn't have a targetted map so using world time
            if (mapAsyncTimeComp == null)
            {
                Log.Message($"Could Not Find AsyncTimeMap!", true);
                __instance.acceptanceTick = Find.TickManager.TicksGame;
            }
            //Does have a targetted map so using that maps AsyncTime
            else
            {
                QuestAsyncTime[__instance] = mapAsyncTimeComp;
                __instance.acceptanceTick = mapAsyncTimeComp.mapTicks;
            }

            __instance.accepterPawn = by;
            __instance.dismissed = false;
            __instance.Initiate();
            return true;
        }

        public static MapAsyncTimeComp GetAsyncTimeFromMapParent(MapParent mapParent)
        {
            if (!mapParent.Map.IsPlayerHome) return null;
            var mapAsyncTimeComp = mapParent.Map.AsyncTime();
            if (mapAsyncTimeComp != null) Log.Message($"Found AsyncTimeMap - Name: {mapParent.Label}", true);
            return mapAsyncTimeComp;
        }
    }

    public static class MultiplayerAsyncQuest
    {
        public static int SetAsyncTimeIfNotExistWorldTime(Quest quest, int tick = 0)
        {
            if (MultiplayerWorldComp.asyncTime && SetTicksForQuestsBasedOnMap.QuestAsyncTime.TryGetValue(quest, out var mapAsyncTimeComp))
            {
                return mapAsyncTimeComp.mapTicks - tick;
            }
            //Uses world time if it can't find async time for this quest
            else
            {
                return Find.TickManager.TicksGame - tick;
            }
        }
    }
}
