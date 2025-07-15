using Multiplayer.Client.Quests;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Client.AsyncTime.Quests
{
    internal class AsyncQuestContextProviderByMap : IAsyncQuestContextProvider
    {
        //List of quest parts that have mapParent as field
        private List<Type> questPartsToCheck = new List<Type>()
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

        public IAsyncQuestContext TryGetAsyncQuestContext(Quest quest)
        {
            //Really terrible way to determine if any quest parts have a map which also has an async time
            foreach (var part in quest.parts.Where(x => x != null && questPartsToCheck.Contains(x.GetType())))
            {
                if (part.GetType().GetField("mapParent")?.GetValue(part) is MapParent { Map: not null } mapParent)
                {
                    // TODO: Issue with IsPlayerHome logic
                    // Example: Hospitality_Refugee quest (generated on client with dev tool)
                    // Problem:
                    // - When the quest is generated on the client, IsPlayerHome returns false,
                    //   so the quest is incorrectly added to the world quest cache.
                    // - When the quest is accepted, it is correctly added to the async context.
                    var mapAsyncTimeComp = mapParent.Map.IsPlayerHome ? mapParent.Map.AsyncTime() : null;
                    if (mapAsyncTimeComp != null) return mapAsyncTimeComp;
                }
            }

            return null;
        }
    }
}
