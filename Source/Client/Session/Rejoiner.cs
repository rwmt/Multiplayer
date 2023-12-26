using Multiplayer.Client.Util;
using Multiplayer.Common;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client;

public static class Rejoiner
{
    public static void DoRejoin()
    {
        Multiplayer.Client.Send(Packets.Client_RequestRejoin);

        Multiplayer.Client.ChangeState(ConnectionStateEnum.ClientLoading);
        Multiplayer.Client.GetState<ClientLoadingState>()!.subState = LoadingState.Waiting;
        Multiplayer.Client.Lenient = true;

        Multiplayer.session.desynced = false;

        Log.Message("Multiplayer: rejoining");

        // From GenScene.GoToMainMenu
        LongEventHandler.ClearQueuedEvents();
        LongEventHandler.QueueLongEvent(() =>
        {
            MemoryUtility.ClearAllMapsAndWorld();
            Current.Game = null;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                MpUI.ClearWindowStack();
                Find.WindowStack.Add(new RejoiningWindow());
            });
        }, "Entry", "LoadingLongEvent", true, null, false);
    }
}
