using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Saving;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(SavedGameLoaderNow))]
    [HarmonyPatch(nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow))]
    [HarmonyPatch(new[] { typeof(string) })]
    public static class LoadPatch
    {
        public static TempGameData gameToLoad;

        static bool Prefix()
        {
            if (gameToLoad == null) return true;

            SaveCompression.doSaveCompression = true;

            try
            {
                ScribeUtil.StartLoading(gameToLoad.SaveData);
                ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
                Scribe.EnterNode("game");
                Current.Game = new Game();
                Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()
                SemiPersistent.ReadSemiPersistent(gameToLoad.SemiPersistent);
            }
            finally
            {
                SaveCompression.doSaveCompression = false;
                gameToLoad = null;
            }

            Log.Message("Game loaded");

            if (Multiplayer.Client != null)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    // Inits all caches
                    foreach (ITickable tickable in TickPatch.AllTickables.Where(t => !(t is ConstantTicker)))
                        tickable.Tick();

                    if (!Current.Game.Maps.Any())
                    {
                        MemoryUtility.UnloadUnusedUnityAssets();
                        Find.World.renderer.RegenerateAllLayersNow();
                    }
                });
            }

            return false;
        }
    }
}
