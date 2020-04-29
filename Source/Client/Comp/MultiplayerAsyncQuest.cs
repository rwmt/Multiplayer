using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Client.Comp
{

    [HarmonyPatch(typeof(Quest), nameof(Quest.TicksSinceAppeared), MethodType.Getter)]
    static class SetContextForQuestTicksSinceAppeared
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            __state = MultiplayerAsyncQuest.GetAsyncTimeCompOrNullForQuest(__instance);
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
            __state = MultiplayerAsyncQuest.GetAsyncTimeCompOrNullForQuest(__instance);
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
            __state = MultiplayerAsyncQuest.GetAsyncTimeCompOrNullForQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
        }
    }
#if false
    [HarmonyPatch(typeof(Quest), nameof(Quest.SetInitiallyAccepted))]
    static class SetContextForQuestSetInitiallyAccepted
    {
        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            __state = MultiplayerAsyncQuest.GetAsyncTimeCompOrNullForQuest(__instance);
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
            __state = MultiplayerAsyncQuest.GetAsyncTimeCompOrNullForQuest(__instance);
            __state?.PreContext();
        }

        static void Postfix(MapAsyncTimeComp __state)
        {
            __state?.PostContext();
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

        static void Prefix(Quest __instance, ref MapAsyncTimeComp __state)
        {
            //Make sure quest is accepted and async time is enabled and there are parts to this quest
            if (__instance.State != QuestState.NotYetAccepted || !MultiplayerWorldComp.asyncTime || __instance.parts == null) return;

            //Have to cache this since its a lookup
            MapAsyncTimeComp mapAsyncTimeComp = null;

            //Really terrible way to determine if any quest parts have a map which also has an async time
            foreach (var part in __instance.parts.Where(x => x != null && mapParentQuestPartTypes.Contains(x.GetType())))
            {
                if (part.GetType().GetField("mapParent")?.GetValue(part) is MapParent mapParent)
                {
                    mapAsyncTimeComp = GetAsyncTimeFromMapParent(mapParent);
                    if (mapAsyncTimeComp != null) break;
                }
            }

            //Does have a targetted map so using that maps AsyncTime
            if (mapAsyncTimeComp != null)
            {
                mapAsyncTimeComp.PreContext();
                QuestAsyncTime[__instance] = mapAsyncTimeComp;
                __state = mapAsyncTimeComp;
            }
        }

        static void Postfix(MapAsyncTimeComp __state) => __state?.PostContext();

        public static MapAsyncTimeComp GetAsyncTimeFromMapParent(MapParent mapParent)
            => mapParent.Map.IsPlayerHome ? mapParent.Map.AsyncTime() : null;
    }

    public static class MultiplayerAsyncQuest
    {
        public static MapAsyncTimeComp GetAsyncTimeCompOrNullForQuest(Quest quest)
        {
            if (!MultiplayerWorldComp.asyncTime || quest == null) return null;
            SetTicksForQuestsBasedOnMap.QuestAsyncTime.TryGetValue(quest, out var mapAsyncTimeComp);
            return mapAsyncTimeComp;
        }
    }
}
