using RimWorld;

namespace Multiplayer.Client;

public static class SyncMarkers
{
    public static bool manualPriorities;

    [MpPrefix(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoManualPrioritiesCheckbox))]
    static void ManualPriorities_Prefix() => manualPriorities = true;

    [MpFinalizer(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoManualPrioritiesCheckbox))]
    static void ManualPriorities_Finalizer() => manualPriorities = false;
}
