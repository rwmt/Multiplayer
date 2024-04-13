using Multiplayer.API;

namespace Multiplayer.Client;

public static class SyncDictPrimitives
{
    internal static SyncWorkerDictionary syncWorkers = new()
    {
        // missing decimal and char, good?
        { (SyncWorker sync, ref bool b   ) =>  sync.Bind(ref b) },
        { (SyncWorker sync, ref byte b   ) =>  sync.Bind(ref b) },
        { (SyncWorker sync, ref sbyte b  ) =>  sync.Bind(ref b) },
        { (SyncWorker sync, ref double d ) =>  sync.Bind(ref d) },
        { (SyncWorker sync, ref float f  ) =>  sync.Bind(ref f) },
        { (SyncWorker sync, ref int i    ) =>  sync.Bind(ref i) },
        { (SyncWorker sync, ref uint i   ) =>  sync.Bind(ref i) },
        { (SyncWorker sync, ref long l   ) =>  sync.Bind(ref l) },
        { (SyncWorker sync, ref ulong l  ) =>  sync.Bind(ref l) },
        { (SyncWorker sync, ref short s  ) =>  sync.Bind(ref s) },
        { (SyncWorker sync, ref ushort s ) =>  sync.Bind(ref s) },
        { (SyncWorker sync, ref string t ) =>  sync.Bind(ref t) },
    };
}
