using Multiplayer.Client.Quests;
using RimWorld;

namespace Multiplayer.Client.AsyncTime.Quests
{
    internal interface IAsyncQuestContextProvider
    {
        IAsyncQuestContext TryGetAsyncQuestContext(Quest quest);
    }
}
