using System;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncGame
    {
        public static void Init()
        {
            static void TryInit(string name, Action action)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception during {name} initialization: {e}");
                    Multiplayer.loadingErrors = true;
                }
            }

            TryInit("SyncMethods", SyncMethods.Init);
            TryInit("SyncFields", SyncFields.Init);
            TryInit("SyncDelegates", SyncDelegates.Init);
            TryInit("SyncActions", SyncActions.Init);

            SyncFieldUtil.ApplyWatchFieldPatches(typeof(SyncFields));
        }
    }
}
