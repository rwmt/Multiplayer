using Verse;

namespace Multiplayer.Common
{
    public class ServerSettings : IExposable
    {
        public string gameName;
        public string bindAddress;
        public int bindPort;
        public string lanAddress;

        public string directAddress = $"0.0.0.0:{MultiplayerServer.DefaultPort}";
        public int maxPlayers = 8;
        public float autosaveInterval = 1f;
        public AutosaveUnit autosaveUnit;
        public bool steam;
        public bool direct;
        public bool lan = true;
        public bool arbiter;
        public bool debugMode;
        public bool desyncTraces;

        public void ExposeData()
        {
            Scribe_Values.Look(ref directAddress, "directAddress", $"0.0.0.0:{MultiplayerServer.DefaultPort}");
            Scribe_Values.Look(ref maxPlayers, "maxPlayers", 8);
            Scribe_Values.Look(ref autosaveInterval, "autosaveInterval", 1f);
            Scribe_Values.Look(ref autosaveUnit, "autosaveUnit");
            Scribe_Values.Look(ref steam, "steam");
            Scribe_Values.Look(ref direct, "direct");
            Scribe_Values.Look(ref lan, "lan", true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref desyncTraces, "desyncTraces");
        }
    }

    public enum AutosaveUnit
    {
        Days, Minutes
    }
}
