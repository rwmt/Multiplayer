using RimWorld;

namespace Multiplayer.Client.Persistent;

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
