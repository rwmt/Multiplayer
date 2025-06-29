using RimWorld;
using System.Collections.Generic;

namespace Multiplayer.Client.AsyncTime.Quests
{
    internal class TickableQuestCache
    {
        public IReadOnlyCollection<Quest> Quests => _quests;

        HashSet<Quest> _quests;
        List<Quest> _questsToRemove;

        public TickableQuestCache()
        {
            _quests = new();
            _questsToRemove = new();
        }

        public void AddQuest(Quest quest)
        {
            _quests.Add(quest);
        }

        public void AddQuestRange(IEnumerable<Quest> quests)
        {
            _quests.UnionWith(quests);
        }

        public void MarkQuestAsRemovable(Quest quest)
        {
            _questsToRemove.Add(quest);
        }

        public bool RemoveQuest(Quest quest)
        {
            _questsToRemove.Remove(quest);
            return _quests.Remove(quest);
        }

        public bool HasQuest(Quest quest)
        {
            return _quests.Contains(quest);
        }

        public void TickQuests()
        {
            foreach (Quest quest in _quests)
            {
                quest.QuestTick();
            }

            ProcessQuestsToRemove();
        }

        private void ProcessQuestsToRemove()
        {
            if (_questsToRemove.Count == 0) return;

            foreach (Quest quest in _questsToRemove)
            {
                _quests.Remove(quest);
            }
        }
    }
}
