using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Factions;
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
    public static bool executingCmdWorld;
    private TimeSpeed timeSpeedInt;

    public float TimeToTickThrough { get; set; }

    public float TickRateMultiplier(TimeSpeed speed)
    {
        if (Multiplayer.GameComp.asyncTime)
        {
            var enforcePause = Multiplayer.WorldComp.splitSession != null ||
                               AsyncTimeComp.pauseLocks.Any(x => x(null));

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

    // Run at the speed of the fastest map or at chosen speed if there are no maps
    public TimeSpeed DesiredTimeSpeed => !Find.Maps.Any() ?
        timeSpeedInt :
        Find.Maps.Select(m => m.AsyncTime())
        .Where(a => a.ActualRateMultiplier(a.DesiredTimeSpeed) != 0f)
        .Max(a => a?.DesiredTimeSpeed) ?? TimeSpeed.Paused;

    public void SetDesiredTimeSpeed(TimeSpeed speed)
    {
        timeSpeedInt = speed;
    }

    public Queue<ScheduledCommand> Cmds => cmds;
    public Queue<ScheduledCommand> cmds = new();

    public int TickableId => -1;

    public World world;
    public ulong randState = 2;

    public int worldTicks;

    public AsyncWorldTimeComp(World world)
    {
        this.world = world;
    }

    public void ExposeData()
    {
        var timer = TickPatch.Timer;
        Scribe_Values.Look(ref timer, "timer");
        TickPatch.SetTimer(timer);

        Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");
        Scribe_Custom.LookULong(ref randState, "randState", 2);

        TimeSpeed timeSpeed = Find.TickManager.CurTimeSpeed;
        Scribe_Values.Look(ref timeSpeed, "timeSpeed");
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            Find.TickManager.CurTimeSpeed = timeSpeed;

        if (Scribe.mode == LoadSaveMode.LoadingVars)
            Multiplayer.game.worldComp = new MultiplayerWorldComp(world);

        Multiplayer.game.worldComp.ExposeData();

        if (Scribe.mode == LoadSaveMode.LoadingVars)
            worldTicks = Find.TickManager.TicksGame;
    }

    public void Tick()
    {
        tickingWorld = true;
        PreContext();

        try
        {
            Find.TickManager.DoSingleTick();
            worldTicks++;
            Multiplayer.WorldComp.TickWorldTrading();

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

    public void PreContext()
    {
        Find.TickManager.CurTimeSpeed = DesiredTimeSpeed;
        Rand.PushState();
        Rand.StateCompressed = randState;

        if (Multiplayer.GameComp.multifaction)
        {
            FactionExtensions.PushFaction(null, Multiplayer.WorldComp.spectatorFaction, force: true);
            foreach (var map in Find.Maps)
                map.MpComp().SetFaction(Multiplayer.WorldComp.spectatorFaction);
        }
    }

    public void PostContext()
    {
        if (Multiplayer.GameComp.multifaction)
        {
            var f = FactionExtensions.PopFaction();
            foreach (var map in Find.Maps)
                map.MpComp().SetFaction(f);
        }

        randState = Rand.StateCompressed;
        Rand.PopState();
    }

    public void ExecuteCmd(ScheduledCommand cmd)
    {
        CommandType cmdType = cmd.type;
        LoggingByteReader data = new LoggingByteReader(cmd.data);
        data.Log.Node($"{cmdType} Global");

        executingCmdWorld = true;
        TickPatch.currentExecutingCmdIssuedBySelf = cmd.issuedBySelf && !TickPatch.Simulating;

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
                LongEventHandler.QueueLongEvent(CreateJoinPointAndSendIfHost, "MpCreatingJoinPoint", false, null);
            }

            if (cmdType == CommandType.InitPlayerData)
            {
                var playerId = data.ReadInt32();
                var canUseDevMode = data.ReadBool();
                Multiplayer.GameComp.playerData[playerId] = new PlayerData { canUseDevMode = canUseDevMode };
            }
        }
        catch (Exception e)
        {
            Log.Error($"World cmd exception ({cmdType}): {e}");
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
            executingCmdWorld = false;

            Multiplayer.game.sync.TryAddCommandRandomState(randState);

            if (cmdType != CommandType.GlobalTimeSpeed)
                Multiplayer.ReaderLog.AddCurrentNode(data);
        }
    }

    private static void CreateJoinPointAndSendIfHost()
    {
        Multiplayer.session.dataSnapshot = SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveAndReload(), Multiplayer.GameComp.multifaction);

        if (!TickPatch.Simulating && !Multiplayer.IsReplay &&
            (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
            SaveLoad.SendGameData(Multiplayer.session.dataSnapshot, true);
    }

    public void SetTimeEverywhere(TimeSpeed speed)
    {
        foreach (var map in Find.Maps)
            map.AsyncTime().SetDesiredTimeSpeed(speed);
        SetDesiredTimeSpeed(speed);
    }

    public static float lastSpeedChange;

    private void HandleTimeSpeed(ScheduledCommand cmd, ByteReader data)
    {
        TimeSpeed speed = (TimeSpeed)data.ReadByte();
        SetDesiredTimeSpeed(speed);

        if (!Multiplayer.GameComp.asyncTime)
        {
            SetTimeEverywhere(speed);

            if (!cmd.issuedBySelf)
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
            tickable.SetDesiredTimeSpeed(Multiplayer.GameComp.GetLowestTimeVote(tickableId));
    }

    public void FinalizeInit()
    {
        Multiplayer.game.SetThingMakerSeed((int)(randState >> 32));
    }
}
