using System;
using Verse;

namespace Multiplayer.Common
{
    public class ServerSettings : IExposable
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

            Scribe_Values.Look(ref directAddress, "directAddress", $"0.0.0.0:{MultiplayerServer.DefaultPort}");
            Scribe_Values.Look(ref maxPlayers, "maxPlayers", 8);
            Scribe_Values.Look(ref autosaveInterval, "autosaveInterval", 1f);
            Scribe_Values.Look(ref autosaveUnit, "autosaveUnit");
            Scribe_Values.Look(ref steam, "steam");
            Scribe_Values.Look(ref direct, "direct");
            Scribe_Values.Look(ref lan, "lan", true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref desyncTraces, "desyncTraces", true);
            Scribe_Values.Look(ref syncConfigs, "syncConfigs", true);
            Scribe_Values.Look(ref autoJoinPoint, "autoJoinPoint", AutoJoinPointFlags.Join | AutoJoinPointFlags.Desync);
            Scribe_Values.Look(ref devModeScope, "devModeScope");
            Scribe_Values.Look(ref hasPassword, "hasPassword");
            Scribe_Values.Look(ref password, "password", "");
            Scribe_Values.Look(ref pauseOnLetter, "pauseOnLetter", PauseOnLetter.AnyThreat);
            Scribe_Values.Look(ref pauseOnJoin, "pauseOnJoin", true);
            Scribe_Values.Look(ref pauseOnDesync, "pauseOnDesync", true);
            Scribe_Values.Look(ref timeControl, "timeControl");
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
