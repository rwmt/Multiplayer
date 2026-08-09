using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

// Multifaction: GameComponent_Bossgroup state is a game singleton - one player's
// bossgroup call escalated threat difficulty and cooldown for every faction.
// Dict/list swap by reference on faction switch; the cooldown int is copied in
// and written back by the postfix on its single writer. Copy-in and write-back
// are both multifaction-gated: plain MP leaves the vanilla component untouched.
public class FactionBossgroupData : IExposable
{
    public int lastBossgroupCalled = -9999999;
    public Dictionary<BossgroupDef, int> timesCalledBossgroups = new();
    public List<BossDef> killedBosses = new();

    public void ExposeData()
    {
        Scribe_Values.Look(ref lastBossgroupCalled, "lastBossgroupCalled", -9999999);
        Scribe_Collections.Look(ref timesCalledBossgroups, "timesCalledBossgroups", LookMode.Def, LookMode.Value);
        Scribe_Collections.Look(ref killedBosses, "killedBosses", LookMode.Def);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            timesCalledBossgroups ??= new Dictionary<BossgroupDef, int>();
            killedBosses ??= new List<BossDef>();
        }
    }

    public static FactionBossgroupData New()
    {
        return new FactionBossgroupData();
    }

    public static FactionBossgroupData FromCurrent()
    {
        var comp = Current.Game?.GetComponent<GameComponent_Bossgroup>();
        if (comp == null)
            return new FactionBossgroupData();

        return new FactionBossgroupData
        {
            lastBossgroupCalled = comp.lastBossgroupCalled,
            timesCalledBossgroups = comp.timesCalledBossgroups,
            killedBosses = comp.killedBosses,
        };
    }
}

[HarmonyPatch(typeof(GameComponent_Bossgroup), nameof(GameComponent_Bossgroup.Notify_BossgroupCalled))]
static class BossgroupLastCalledWriteBack
{
    static void Postfix(GameComponent_Bossgroup __instance)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        if (Multiplayer.WorldComp.factionData.TryGetValue(Faction.OfPlayer.loadID, out var data))
            data.bossgroup.lastBossgroupCalled = __instance.lastBossgroupCalled;
    }
}
