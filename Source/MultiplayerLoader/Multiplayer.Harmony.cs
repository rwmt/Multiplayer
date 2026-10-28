using System;
using HarmonyLib;

namespace MultiplayerLoader;

public partial class Multiplayer
{
    private void CheckHarmonyVersion()
    {
        var harmonyVersion = typeof(Harmony).Assembly.GetName().Version;
        if (harmonyVersion.Major < 3)
            throw new InvalidOperationException(
                $"Multiplayer requires Harmony 3, but Harmony {harmonyVersion} was loaded. " +
                "Update the Harmony mod and any mods that supply their own 0Harmony.dll.");
    }
}
