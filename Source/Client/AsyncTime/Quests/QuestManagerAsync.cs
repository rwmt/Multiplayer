using Multiplayer.Client.Quests;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client.AsyncTime.Quests
{
    public class QuestManagerAsync
    {
        readonly TickableQuestCache _worldTickingQuests;
        readonly Dictionary<IAsyncQuestContext, TickableQuestCache> _asyncTickingQuests;

        readonly IAsyncQuestContextProvider _asyncQuestContextProvider;

        public QuestManagerAsync()
        {
            _asyncQuestContextProvider = new AsyncQuestContextProviderByMap();
            _asyncTickingQuests = new Dictionary<IAsyncQuestContext, TickableQuestCache>();
            _worldTickingQuests = new TickableQuestCache();
        }

        public void TickQuestsAsync(IAsyncQuestContext context)
        {
            if(_asyncTickingQuests.TryGetValue(context, out var questTicker))
            {
                questTicker.TickQuests();
            }
        }

        public void TickWorldQuests()
        {
            _worldTickingQuests.TickQuests();
        }

        public void SetQuestsAfterGameInit(List<Quest> quests)
        {
            foreach (var quest in quests)
            {
                AddQuest(quest);
            }
        }

        public IAsyncQuestContext AddQuest(Quest quest)
        {
            if (!Multiplayer.GameComp.asyncTime || quest == null) return null;

            var questContext = _asyncQuestContextProvider.TryGetAsyncQuestContext(quest);

            if (questContext != null)
            {
                RegisterInContext(questContext, quest);
                MpLog.Debug($"Info: Found AsyncTimeMap: '{questContext.GetQuestContextInfo()}' for Quest: '{quest.name}'");
            }
            else
            {
                _worldTickingQuests.AddQuest(quest);
                MpLog.Debug($"Info: Could not find QuestContext for Quest: '{quest.name}'");
            }

            return questContext;
        }

        public void EndQuest(Quest quest)
        {
            var allQuests = _asyncTickingQuests.Values.Append(_worldTickingQuests);

            foreach (var quests in allQuests)
            {
                if (quests.HasQuest(quest))
                {
                    quests.MarkQuestAsRemovable(quest);
                }
            }
        }

        public void RemoveQuestsForContext(IAsyncQuestContext questContext)
        {
            if (_asyncTickingQuests.TryGetValue(questContext, out var tickableQuests))
            {
                // TODO: Evaluate whether adding quests to worldTickingQuests is necessary.
                // This method is triggered when the context—specifically a settlement with an AsyncTimeComponent—is abandoned.
                // Since abandoning a settlement also removes its associated quests,
                // it's unclear why these quests need to be added to worldTickingQuests at this point.
                _worldTickingQuests.AddQuestRange(tickableQuests.Quests);

                _asyncTickingQuests.Remove(questContext);
            }
        }

        public IAsyncQuestContext TryGetAsyncQuestContext(Quest quest)
        {
            if (!Multiplayer.GameComp.asyncTime || quest == null) return null;

            return _asyncTickingQuests.FirstOrDefault(x => x.Value.HasQuest(quest)).Key;
        }
  
        private void RegisterInContext(IAsyncQuestContext mapAsyncTimeComp, Quest quest)
        {
            RemoveQuest(quest);

            if (_asyncTickingQuests.TryGetValue(mapAsyncTimeComp, out var mapTickableQuests))
            {
                mapTickableQuests.AddQuest(quest);
            }
            else
            {
                _asyncTickingQuests[mapAsyncTimeComp] = new TickableQuestCache();
                _asyncTickingQuests[mapAsyncTimeComp].AddQuest(quest);
            }
        }
        private void RemoveQuest(Quest quest)
        {
            int questRemovedCount = 0;

            if (_worldTickingQuests.RemoveQuest(quest))
                questRemovedCount++;

            foreach (TickableQuestCache quests in _asyncTickingQuests.Values)
            {
                if (quests.RemoveQuest(quest))
                    questRemovedCount++;
            }

            if (questRemovedCount > 1)
            {
                MpLog.Debug($"Warning: Quest '{quest?.name ?? "null"}' was removed from multiple caches ({questRemovedCount} times). This may indicate a logic error or duplication.");
            }
        }
    }
}

