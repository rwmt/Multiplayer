using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Comp
{
    public class MultiplayerGameComp : IExposable
    {
        public bool asyncTime;
        public bool debugMode;
        public bool logDesyncTraces;

        public IdBlock globalIdBlock = new(int.MaxValue / 2, 1_000_000_000);

        public MultiplayerGameComp(Game game)
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref asyncTime, "asyncTime", true, true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref logDesyncTraces, "logDesyncTraces");

            Scribe_Custom.LookIdBlock(ref globalIdBlock, "globalIdBlock");

            if (globalIdBlock == null)
            {
                // todo globalIdBlock was previously in WorldComp, this is a quick hack to make old saves compatible
                Log.Warning("Global id block was null, fixing...");
                globalIdBlock = new IdBlock(int.MaxValue / 2, 1_000_000_000);
            }
        }
    }
}
