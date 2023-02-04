namespace Multiplayer.Client
{
    public static class SyncDict
    {
        internal static SyncWorkerDictionaryTree syncWorkers;

        public static void Init()
        {
            syncWorkers = SyncWorkerDictionaryTree.Merge(
                SyncDictMisc.syncWorkers,
                SyncDictRimWorld.syncWorkers,
                SyncDictDlc.syncWorkers,
                SyncDictMultiplayer.syncWorkers
            );
        }
    }
}
