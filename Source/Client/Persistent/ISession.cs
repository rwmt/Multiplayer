using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent
{
    public interface ISession
    {
        Map Map { get; }

        int SessionId { get; }
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
}
