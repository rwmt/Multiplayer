using System;

namespace Multiplayer.Client.Patches;

internal sealed class ViewedMapTracker
{
    public int LastReportedMapId { get; private set; } = -1;

    public void Report(int currentMapId, Action<int, int> send)
    {
        if (currentMapId == LastReportedMapId) return;

        send(LastReportedMapId, currentMapId);
        LastReportedMapId = currentMapId;
    }

    public void Reset() => LastReportedMapId = -1;
}
