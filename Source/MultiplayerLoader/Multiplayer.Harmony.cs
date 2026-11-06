using System;
using HarmonyLib;

namespace MultiplayerLoader;

public partial class Multiplayer
{
    private void CheckHarmonyVersion()
    {
        // An old Prepatcher left enabled can supply Harmony 2.4.x instead of Harmony 3.
        // This build targets Harmony 3; check the loaded assembly before initializing
        // Multiplayer so the player gets a clear dependency error.
        var harmonyVersion = typeof(Harmony).Assembly.GetName().Version;
        if (harmonyVersion.Major < 3)
            throw new InvalidOperationException(
                $"Multiplayer requires Harmony 3, but Harmony {harmonyVersion} was loaded. " +
                "Update the Harmony mod and any mods that supply their own 0Harmony.dll.");
    }
}
