using System;

namespace Multiplayer.Common
{
    public class ServerSettings
    {
        public string gameName;
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
        public bool desyncTraces = true;
        public bool syncConfigs = true;
        public AutoJoinPointFlags autoJoinPoint = AutoJoinPointFlags.Join | AutoJoinPointFlags.Desync;
        public DevModeScope devModeScope;
        public bool hasPassword;
        public string password = "";
        public PauseOnLetter pauseOnLetter = PauseOnLetter.AnyThreat;
        public bool pauseOnJoin = true;
        public bool pauseOnDesync = true;
        public TimeControl timeControl;

        public void ExposeData()
        {
            // Remember to mirror the default values

            ScribeLike.Look(ref directAddress!, "directAddress", $"0.0.0.0:{MultiplayerServer.DefaultPort}");
            ScribeLike.Look(ref maxPlayers, "maxPlayers", 8);
            ScribeLike.Look(ref autosaveInterval, "autosaveInterval", 1f);
            ScribeLike.Look(ref autosaveUnit, "autosaveUnit");
            ScribeLike.Look(ref steam, "steam");
            ScribeLike.Look(ref direct, "direct");
            ScribeLike.Look(ref lan, "lan", true);
            ScribeLike.Look(ref debugMode, "debugMode");
            ScribeLike.Look(ref desyncTraces, "desyncTraces", true);
            ScribeLike.Look(ref syncConfigs, "syncConfigs", true);
            ScribeLike.Look(ref autoJoinPoint, "autoJoinPoint", AutoJoinPointFlags.Join | AutoJoinPointFlags.Desync);
            ScribeLike.Look(ref devModeScope, "devModeScope");
            ScribeLike.Look(ref hasPassword, "hasPassword");
            ScribeLike.Look(ref password!, "password", "");
            ScribeLike.Look(ref pauseOnLetter, "pauseOnLetter", PauseOnLetter.AnyThreat);
            ScribeLike.Look(ref pauseOnJoin, "pauseOnJoin", true);
            ScribeLike.Look(ref pauseOnDesync, "pauseOnDesync", true);
            ScribeLike.Look(ref timeControl, "timeControl");
        }
    }

    public enum AutosaveUnit
    {
        Days,
        Minutes
    }

    [Flags]
    public enum AutoJoinPointFlags
    {
        Join = 1,
        Desync = 2,
        Autosave = 4
    }

    public enum DevModeScope
    {
        HostOnly,
        Everyone
    }

    public enum PauseOnLetter
    {
        Never,
        MajorThreat,
        AnyThreat,
        AnyLetter
    }

    public enum TimeControl
    {
        EveryoneControls,
        LowestWins,
        HostOnly
    }
}
