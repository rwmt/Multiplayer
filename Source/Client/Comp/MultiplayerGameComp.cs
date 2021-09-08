using Verse;

namespace Multiplayer.Client.Comp
{
    public class MultiplayerGameComp : IExposable
    {
        public bool asyncTime;
        public bool debugMode;
        public bool logDesyncTraces;

        public MultiplayerGameComp(Game game)
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref asyncTime, "asyncTime", true, true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref logDesyncTraces, "logDesyncTraces");
        }
    }
}
