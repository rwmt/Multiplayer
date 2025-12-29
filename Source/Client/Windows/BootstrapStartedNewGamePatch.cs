using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// When the vanilla flow finishes generating the new game, this fires once the map and pawns are ready.
    /// Use it as a backup signal to kick the bootstrap save pipeline in case FinalizeInit was missed or delayed.
    /// </summary>
    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.StartedNewGame))]
    public static class BootstrapStartedNewGamePatch
    {
        static void Postfix()
        {
            var window = BootstrapConfiguratorWindow.Instance;
            if (window == null)
            {
                UnityEngine.Debug.Log("[Bootstrap] StartedNewGame called but bootstrap window not present");
                return;
            }

            // Arm bootstrap map init regardless of previous timing.
            BootstrapConfiguratorWindow.AwaitingBootstrapMapInit = true;

            // Run on main thread: close landing popups and proceed to save pipeline.
            OnMainThread.Enqueue(() =>
            {
                window.TryClearStartingLettersOnce();
                window.TryCloseLandingDialogsOnce();
                window.OnBootstrapMapInitialized();
            });

            UnityEngine.Debug.Log("[Bootstrap] StartedNewGame: armed + cleanup queued");
        }
    }
}
