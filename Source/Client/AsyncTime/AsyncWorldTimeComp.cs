using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Saving;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.AsyncTime;

public class AsyncWorldTimeComp : IExposable, ITickable
{
    public static bool tickingWorld;
    private TimeSpeed timeSpeedInt;

    public float TimeToTickThrough { get; set; }

    public float TickRateMultiplier(TimeSpeed speed)
    {
        if (Multiplayer.GameComp.asyncTime)
        {
            var enforcePause = Multiplayer.WorldComp.sessionManager.IsAnySessionCurrentlyPausing(null);

            if (enforcePause)
                return 0f;
        }

        return speed switch
        {
            TimeSpeed.Paused => 0f,
            TimeSpeed.Normal => 1f,
            TimeSpeed.Fast => 3f,
            TimeSpeed.Superfast => 6f,
            TimeSpeed.Ultrafast => 15f,
            _ => -1f
        };
    }

    // Run at the speed of the fastest map or at chosen speed if there are no maps.
    // A map can be in Find.Maps before its AsyncTimeComp is registered (map
    // generation, singleplayer conversion) - a comp-less map isn't a running map,
    // so skip it rather than throw.
    public TimeSpeed DesiredTimeSpeed
    {
        get => !Find.Maps.Any()
            ? timeSpeedInt
            : Find.Maps.Select(m => m.AsyncTime())
                .Where(a => a != null && a.ActualRateMultiplier(a.DesiredTimeSpeed) != 0f)
                .Max(a => a?.DesiredTimeSpeed) ?? TimeSpeed.Paused;
        set => timeSpeedInt = value;
    }

    public Queue<ScheduledCommand> Cmds => cmds;
    public Queue<ScheduledCommand> cmds = new();

    private int cachedPlanetViewerCount;
    private int cachedPlayerCountVersion = -1;

    // Players currently in the planet view, derived from the synced view
    // table (see MultiplayerGameComp.playerViewedMaps). The old incremental
    // count had no floor on this comp and a post-reload disconnect could
    // drive it negative, pinning world objects at the no-viewer rate even
    // with the planet view open.
    public int CurrentPlayerCount
    {
        get
        {
            var gameComp = Multiplayer.GameComp;
            if (cachedPlayerCountVersion != gameComp.playerViewsVersion)
            {
                cachedPlanetViewerCount = 0;
                foreach (var viewedMapId in gameComp.playerViewedMaps.Values)
                    if (viewedMapId == VTRSync.WorldMapId)
                        cachedPlanetViewerCount++;
                cachedPlayerCountVersion = gameComp.playerViewsVersion;
            }

            return cachedPlanetViewerCount;
        }
    }

    // World objects run at full rate while anyone is connected, not only
    // while someone has the planet view open. Vanilla's rate-15 default for
    // an unwatched world assumes nobody can see it; in multiplayer caravans
    // and world motion are visible from map view edges and the session is
    // always watched by someone, and a permanently lumping world was field-
    // reported as "world rendering strangeness". Derived from synced state,
    // identical on every client.
    public int VTR => Multiplayer.GameComp.playerViewedMaps.Count > 0 ? VTRSync.MinimumVtr : VTRSync.MaximumVtr;

    public int TickableId => -1;

    public World world;
    public ulong randState;

    public int worldTicks;

    // The global slot fields the world previously had no owner for. TimeSlower
    // is transient in vanilla (never scribed), so a fresh instance is correct;
    // gameStartAbsTick is captured at construction, which runs after
    // ExposeSmallComponents has loaded the TickManager on every path
    // (deserialization via SaveWorldComp, its comp-missing fallback, and the
    // singleplayer conversion in HostUtil).
    public TimeSlower slower = new();
    public int worldGameStartAbsTick;

    public AsyncWorldTimeComp(World world)
    {
        this.world = world;

        // Use the world's constant rand seed as our initial randState.
        // Only fill the seed part, leave the iterations out.
        randState = (uint)world.ConstantRandSeed;

        worldGameStartAbsTick = Find.TickManager?.gameStartAbsTick ?? 0;
    }

    public void ExposeData()
    {
        var timer = TickPatch.Timer;
        Scribe_Values.Look(ref timer, "timer");
        TickPatch.SetTimer(timer);

        Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");
        Scribe_Custom.LookULong(ref randState, "randState", 2);

        // Read the world's own speed, not the global TickManager - the global only
        // held it because PreContext used to leave it installed, so a save taken from
        // a UI context would persist the viewed map's speed instead. Guarded on
        // Saving: DesiredTimeSpeed walks Find.Maps, empty during LoadingVars.
        // Own node: sharing "timeSpeed" with the field above made this a no-op, since
        // Look resolves a label to the first matching child. timeSpeedInt is the
        // default, so saves without the node fall back to the speed loaded above.
        TimeSpeed globalTimeSpeed = Scribe.mode == LoadSaveMode.Saving ? DesiredTimeSpeed : timeSpeedInt;
        Scribe_Values.Look(ref globalTimeSpeed, "globalTimeSpeed", timeSpeedInt);
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            Find.TickManager.CurTimeSpeed = globalTimeSpeed;

        if (Scribe.mode == LoadSaveMode.LoadingVars)
            Multiplayer.game.worldComp = new MultiplayerWorldComp(world);

        Multiplayer.game.worldComp.ExposeData();

        // World-basis tick stamps (CooldownClockPatches) survive reload only if
        // the world clock itself does; absent node (older saves) falls back to
        // the old rebuild-from-TicksGame behavior
        Scribe_Values.Look(ref worldTicks, "worldTicks", -1);
        if (Scribe.mode == LoadSaveMode.LoadingVars && worldTicks < 0)
            worldTicks = Find.TickManager.TicksGame;

        // Not scribed - always the global TickManager's value, re-derived on
        // load in case the ctor ran before the meta components were current
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            worldGameStartAbsTick = Find.TickManager.gameStartAbsTick;
    }

    public void Tick()
    {
        tickingWorld = true;
        PreContext();

        try
        {
            Find.TickManager.DoSingleTick();
            worldTicks++;

            // PreContext installed worldTicks and DoSingleTick incremented the
            // ambient, so the two can only disagree if something else moved one
            // of them - which would silently shift every world-clock read
            if (MpVersion.IsDebug && Find.TickManager.ticksGameInt != worldTicks)
                Log.Error($"MP: world clock mismatch: ambient {Find.TickManager.ticksGameInt} != worldTicks {worldTicks}");

            Multiplayer.WorldComp.TickWorldSessions();

            if (ModsConfig.BiotechActive)
            {
                // Vanilla puts those into a separate try/catch blocks
                try
                {
                    CompDissolutionEffect_Goodwill.WorldUpdate();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
                try
                {
                    CompDissolutionEffect_Pollution.WorldUpdate();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
        }
        finally
        {
            PostContext();
            tickingWorld = false;

            Multiplayer.game.sync.TryAddWorldRandomState(randState);
        }
    }

// The world's clock lives on the global TickManager only while installed
    // here; the world tick is self-contained. PreContext installs the full
    // world snapshot - ticksGameInt = worldTicks, the scribed mirror that
    // DoSingleTick's increment tracks - and PostContext restores whatever the
    // frame had. Readers that want world time between ticks (letters, alerts,
    // world render, saving) install it explicitly. A stack because world
    // commands can nest a world context inside the world tick.
    private readonly Stack<TimeSnapshot?> prevTimes = new();

    // Expected faction stack depth per nested bracket - see AsyncTimeComp
    private readonly Stack<int> prevFactionDepths = new();

    public void PreContext()
    {
        prevTimes.Push(TimeSnapshot.GetAndSetFromWorld());
        Rand.PushState();
        Rand.StateCompressed = randState;

        if (Multiplayer.GameComp.multifaction)
        {
            FactionExtensions.PushFaction(null, Multiplayer.WorldComp.spectatorFaction, force: true);
            foreach (var map in Find.Maps)
                map.MpComp().SetFaction(Multiplayer.WorldComp.spectatorFaction);
        }

        // Recorded unconditionally: the command path pushes the command's
        // faction after PreContext even outside multifaction, and a throw in
        // the handler must not strand it
        prevFactionDepths.Push(FactionContext.stack.Count);
    }

    public void PostContext()
    {
        if (prevFactionDepths.Count == 0)
            Log.Error("MP: unbalanced faction depth on the world clock");
        else
            FactionExtensions.UnwindFactionStack(null, prevFactionDepths.Pop(), "world bracket");

        if (Multiplayer.GameComp.multifaction)
        {
            // Restore must be unconditional: PreContext swapped EVERY map onto
            // the spectator faction's data (resourceCounter, zone/area/
            // designation managers), and leaving any map on it corrupts every
            // UI read until something else swaps it back (alternating only on
            // frames that ran a world tick - a per-frame flicker/re-ping
            // shape). A balanced pop returns the pre-world-tick OfPlayer,
            // which is the client-local faction, so falling back to
            // RealPlayerFaction on an unbalanced pop restores the same thing
            // the balanced path would have. This is the faction half of the
            // same PreContext/PostContext imbalance whose speed half caused
            // the paused-map time-context bug.
            var f = FactionExtensions.PopFaction() ?? Multiplayer.RealPlayerFaction;
            foreach (var map in Find.Maps)
                map.MpComp().SetFaction(f);
        }

        randState = Rand.StateCompressed;
        Rand.PopState();

        if (prevTimes.Count == 0)
            Log.Error("MP: unbalanced PostContext on the world clock");
        else
            prevTimes.Pop()?.Set();
    }

    public void ExecuteCmd(ScheduledCommand cmd)
    {
        CommandType cmdType = cmd.type;
        LoggingByteReader data = new LoggingByteReader(cmd.data);
        data.Log.Node($"{cmdType} Global");

        TickPatch.currentExecutingCmdIssuedBySelf = cmd.IsIssuedBySelf() && !TickPatch.Simulating;
        TickPatch.currentExecutingCmdType = cmdType;

        PreContext();
        FactionExtensions.PushFaction(null, cmd.GetFaction());

        bool prevDevMode = Prefs.data.devMode;
        var prevGodMode = DebugSettings.godMode;
        Multiplayer.GameComp.playerData.GetValueOrDefault(cmd.playerId)?.SetContext();

        var randCalls1 = DeferredStackTracing.randCalls;

        try
        {
            if (cmdType == CommandType.Sync)
            {
                var handler = SyncUtil.HandleCmd(data);
                data.Log.current.text = handler.ToString();
            }

            if (cmdType == CommandType.DebugTools)
            {
                DebugSync.HandleCmd(data);
            }

            if (cmdType == CommandType.GlobalTimeSpeed)
            {
                HandleTimeSpeed(cmd, data);
            }

            if (cmdType == CommandType.TimeSpeedVote)
            {
                HandleTimeVote(cmd, data);
            }

            if (cmdType == CommandType.PauseAll)
            {
                SetTimeEverywhere(TimeSpeed.Paused);
            }

            if (cmdType == CommandType.CreateJoinPoint)
            {
                if (Multiplayer.session?.ConnectedToStandaloneServer == true && !TickPatch.currentExecutingCmdIssuedBySelf)
                    return;

                LongEventHandler.QueueLongEvent(CreateJoinPointAndSendIfHost, "MpCreatingJoinPoint", false, null);
            }

            if (cmdType == CommandType.InitPlayerData)
            {
                var playerId = data.ReadInt32();
                var canUseDevMode = data.ReadBool();
                Multiplayer.GameComp.playerData[playerId] = new PlayerData { canUseDevMode = canUseDevMode };
            }

            if (cmdType == CommandType.PlayerCount)
            {
                // Payload: (playerId, viewedMapId). InvalidMapId removes the
                // entry (server-sent when the player disconnects). The VTR
                // counts derive from the table - see
                // MultiplayerGameComp.playerViewedMaps.
                int playerId = data.ReadInt32();
                int viewedMapId = data.ReadInt32();
                Multiplayer.GameComp.SetPlayerViewedMap(playerId, viewedMapId);

                MpLog.Debug($"[{worldTicks}|{Multiplayer.session.remoteTickUntil}] Player view: player={playerId}, map={viewedMapId}, views={Multiplayer.GameComp.playerViewedMaps.Count}");
            }
        }
        catch (Exception e)
        {
            SimulationFailures.Handle($"World cmd exception ({cmdType})", e);
        }
        finally
        {
            DebugSettings.godMode = prevGodMode;
            Prefs.data.devMode = prevDevMode;

            MpLog.Debug($"rand calls {DeferredStackTracing.randCalls - randCalls1}");
            MpLog.Debug("rand state " + Rand.StateCompressed);

            FactionExtensions.PopFaction();
            PostContext();
            TickPatch.currentExecutingCmdIssuedBySelf = false;
            TickPatch.currentExecutingCmdType = null;

            Multiplayer.game.sync.TryAddCommandRandomState(randState);

            if (cmdType != CommandType.GlobalTimeSpeed)
                Multiplayer.ReaderLog.AddCurrentNode(data);
        }
    }

    private static void CreateJoinPointAndSendIfHost()
    {
        Multiplayer.session.dataSnapshot = SaveLoad.SaveReloadAndCreateSnapshot(
            Multiplayer.GameComp.multifaction,
            ReloadOptimizationMode.ForJoinPointSnapshot
        );

        if (!TickPatch.Simulating && !Multiplayer.IsReplay)
        {
            if (Multiplayer.session?.ConnectedToStandaloneServer == true)
            {
                // Standalone: every client uploads world data + individual snapshots
                SaveLoad.SendGameData(Multiplayer.session.dataSnapshot, true);
                SaveLoad.SendStandaloneMapSnapshots(Multiplayer.session.dataSnapshot);
                SaveLoad.SendStandaloneWorldSnapshot(Multiplayer.session.dataSnapshot);
            }
            else if (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance)
            {
                // Hosted: only host/arbiter uploads world data
                SaveLoad.SendGameData(Multiplayer.session.dataSnapshot, true);
            }
        }
    }

    public void SetTimeEverywhere(TimeSpeed speed)
    {
        foreach (var map in Find.Maps)
            map.AsyncTime().DesiredTimeSpeed = speed;
        DesiredTimeSpeed = speed;
    }

    public static float lastSpeedChange;

    private void HandleTimeSpeed(ScheduledCommand cmd, ByteReader data)
    {
        TimeSpeed speed = (TimeSpeed)data.ReadByte();
        DesiredTimeSpeed = speed;

        if (!Multiplayer.GameComp.asyncTime)
        {
            SetTimeEverywhere(speed);

            if (!cmd.IsIssuedBySelf())
                lastSpeedChange = Time.realtimeSinceStartup;
        }

        MpLog.Debug($"Set world speed {speed} {TickPatch.Timer} {Find.TickManager.TicksGame}");
    }

    private void HandleTimeVote(ScheduledCommand cmd, ByteReader data)
    {
        TimeVote vote = (TimeVote)data.ReadByte();
        int tickableId = data.ReadInt32();

        // Update the vote
        if (vote >= TimeVote.ResetTickable)
            Multiplayer.GameComp.playerData.Do(p => p.Value.SetTimeVote(tickableId, vote));
        else if (Multiplayer.GameComp.playerData.GetValueOrDefault(cmd.playerId) is { } playerData)
            playerData.SetTimeVote(tickableId, vote);

        // Update the time speed
        if (!Multiplayer.GameComp.asyncTime || vote == TimeVote.ResetGlobal)
            SetTimeEverywhere(Multiplayer.GameComp.GetLowestTimeVote(TickableId));
        else if (TickPatch.TickableById(tickableId) is { } tickable)
            tickable.DesiredTimeSpeed = Multiplayer.GameComp.GetLowestTimeVote(tickableId);
    }

    public void FinalizeInit()
    {
        Multiplayer.game.SetThingMakerSeed((int)(randState >> 32));
    }
}
