using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent
{
    public interface ISession
    {
        Map Map { get; }

        int SessionId { get; set; }

        bool IsSessionValid { get; }

        bool IsCurrentlyPausingAll();

        bool IsCurrentlyPausingMap(Map map);

        FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry);
    }

    // todo unused for now
    public interface IPausingWithDialog
    {
        void OpenWindow(bool sound = true);
    }

    public interface ISessionWithTransferables : ISession
    {
        Transferable GetTransferableByThingId(int thingId);

        void Notify_CountChanged(Transferable tr);
    }

    public interface IExposableSession : ISession, IExposable
    {
    }

    public interface ISemiPersistentSession : ISession
    {
        void Write(SyncWorker sync);

        void Read(SyncWorker sync);
    }

    public interface ISessionWithCreationRestrictions
    {
        bool CanExistWith(ISession other);
    }

    public interface ITickingSession
    {
        void Tick();
    }
}
