namespace Multiplayer.Client.Quests
{
    public interface IAsyncQuestContext
    {
        void PreContext();
        void PostContext();

        string GetQuestContextInfo();
    }
}
