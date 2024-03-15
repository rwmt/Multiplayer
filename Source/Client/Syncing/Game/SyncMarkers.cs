using RimWorld;

namespace Multiplayer.Client;

public static class SyncMarkers
{
    public static bool manualPriorities;

    [MpPrefix(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoManualPrioritiesCheckbox))]
    static void ManualPriorities_Prefix() => manualPriorities = true;

    [MpPostfix(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoManualPrioritiesCheckbox))]
    static void ManualPriorities_Postfix() => manualPriorities = false;
}
