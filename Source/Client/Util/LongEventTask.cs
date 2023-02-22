using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client.Util;

public static class LongEventTask
{
    public static Task ContinueInLongEvent(string textKey, bool async)
    {
        TaskCompletionSource<object> source = new();
        LongEventHandler.QueueLongEvent(
            () => source.SetResult(null),
            textKey,
            async,
            null
        );
        return source.Task;
    }
}
