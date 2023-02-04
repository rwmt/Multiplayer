using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent
{
    public class MpTransferableReference
    {
        public ISessionWithTransferables session;
        public Transferable transferable;

        public MpTransferableReference(ISessionWithTransferables session, Transferable transferable)
        {
            this.session = session;
            this.transferable = transferable;
        }

        public int CountToTransfer
        {
            get => transferable.CountToTransfer;
            set => transferable.CountToTransfer = value;
        }

        public override int GetHashCode() => transferable.GetHashCode();
        public override bool Equals(object obj) => obj is MpTransferableReference tr && tr.transferable == transferable;
    }

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
