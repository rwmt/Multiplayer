using System.Collections.Generic;
using Multiplayer.API;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Comp
{
    public class MultiplayerGameComp : IExposable, IHasSemiPersistentData
    {
        public bool asyncTime;
        public bool debugMode;
        public bool logDesyncTraces;
        public Dictionary<int, PlayerData> playerData = new(); // player id to player data

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

        public void WriteSemiPersistent(ByteWriter writer)
        {
            SyncSerialization.WriteSync(writer, playerData);
        }

        public void ReadSemiPersistent(ByteReader reader)
        {
            playerData = SyncSerialization.ReadSync<Dictionary<int, PlayerData>>(reader);
            DebugSettings.godMode = playerData.TryGetValue(Multiplayer.session.playerId, out var data) && data.godMode;
        }

        [SyncMethod(debugOnly = true)]
        public void SetGodMode(int playerId, bool godMode)
        {
            playerData[playerId].godMode = godMode;
        }
    }

    public class PlayerData : ISynchronizable
    {
        public bool canUseDevMode;
        public bool godMode;

        public void Sync(SyncWorker sync)
        {
            sync.Bind(ref canUseDevMode);
        }

        public void SetContext()
        {
            Prefs.data.devMode = canUseDevMode;
            DebugSettings.godMode = godMode;
        }
    }
}
