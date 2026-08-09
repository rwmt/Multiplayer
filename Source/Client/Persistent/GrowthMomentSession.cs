using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent;

public class GrowthMomentSession(Map _) : ExposableSession(null), ITickingSession
{
    public static GrowthMomentSession GetSessionFor(Pawn pawn) =>
        // Null map: unspawned pawn (caravan child) - sessions are map-bound,
        // so there is none; callers fall back to the vanilla dialog whose
        // choices are still synced
        pawn.Map?.MpComp().sessionManager.GetFirstOfType<GrowthMomentSession>(sess => sess.Pawn == pawn);

    // There isn't a trait selected.
    public const int NullTraitIdx = -1; // intentionally the same as the "not found" return value for IndexOf

    // There's a chance to have a trait choice of "No Trait". It's represented by this
    public const int NoTraitTraitIdx = -2;

    private ChoiceLetter_GrowthMoment letter;
    public Pawn Pawn => letter.pawn;
    public int traitIdx = NullTraitIdx;
    public List<int> passionIndexes = [];
    public bool uiDirty; // if true, received or sent an update

    // The child's faction (stamped from the creation context) - only that
    // faction's players get the dialog and may make the choice. -1 = legacy
    // sessions from older saves: everyone, the old behavior.
    public int ownerFactionId = -1;

    public bool IsForLocalPlayer =>
        ownerFactionId < 0 || Multiplayer.RealPlayerFaction?.loadID == ownerFactionId;

    // Valid during synced execution, where Faction.OfPlayer is the issuer's
    private bool ContextFactionMayChoose =>
        ownerFactionId < 0 || Faction.OfPlayer?.loadID == ownerFactionId;

    public static GrowthMomentSession Create(ChoiceLetter_GrowthMoment letter) => new(null)
    {
        letter = letter,
        ownerFactionId = Multiplayer.GameComp.multifaction &&
                         Faction.OfPlayer is { IsPlayer: true } f &&
                         f != Multiplayer.WorldComp.spectatorFaction
            ? f.loadID
            : -1
    };

    // LetterWithTimeout.LastTickBeforeTimeout adjusted to use the map's time instead of Find.TickManager.TicksGame
    public override bool IsCurrentlyPausing(Map map) => Map == map && letter.TimeoutActive &&
                                                        letter.disappearAtTick <= map.AsyncTime().mapTicks + 1 &&
                                                        !letter.ArchiveView;

    public override bool IsSessionValid => !letter.ArchiveView;

    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
    {
        if (Map != entry.map || !IsForLocalPlayer) return null;
        return new FloatMenuOption("MpGrowthMomentSession".Translate(Pawn.Name.ToStringShort), OpenWindow);
    }

    public override Map Map => letter.pawn.Map;

    [SyncMethod]
    public void UpdateChoices(int traitIdx, List<int> passionIndexes)
    {
        // Another faction's player can't steer this child's choices
        if (!ContextFactionMayChoose) return;

        this.traitIdx = traitIdx;
        this.passionIndexes = passionIndexes;
        this.uiDirty = true;
    }

    public void OpenWindow()
    {
        if (!IsSessionValid) return;
        letter.TrySetChoices();
        var window = new GrowthMomentWindow(letter.text, letter);
        Find.WindowStack.Add(window);
    }

    public void Tick()
    {
        // Pawn off-map mid-session (formed a caravan): session lingers in its
        // original map's manager but Map follows the pawn - don't NRE
        if (Map == null) return;
        if (!IsSessionValid) Map.MpComp().sessionManager.RemoveSession(this);
        // The forced-open at timeout is UI-only - owner's players only
        else if (letter.TimeoutActive && letter.disappearAtTick <= Map.AsyncTime().mapTicks + 1 && IsForLocalPlayer)
            OpenWindow();
    }

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_References.Look(ref letter, "pawn");
        Scribe_Values.Look(ref traitIdx, "traitIdx");
        Scribe_Collections.Look(ref passionIndexes, "passionIndexes", LookMode.Value);
        Scribe_Values.Look(ref ownerFactionId, "ownerFactionId", -1);
    }

    [SyncMethod]
    public static GrowthMomentSession TryAddSession(ChoiceLetter_GrowthMoment letter)
    {
        if (letter.ArchiveView) return null;
        var pawn = letter.pawn;
        // Unspawned pawn (caravan child): sessions are map-bound, so no
        // session - the vanilla dialog handles it and MakeChoices is synced
        if (pawn.Map == null) return null;
        var sessionManager = pawn.Map.MpComp().sessionManager;
        var sess = sessionManager.GetFirstOfType<GrowthMomentSession>(sess => sess.Pawn == pawn);
        if (sess == null)
        {
            sess = Create(letter);
            if (!sessionManager.AddSession(sess))
            {
                // Shouldn't happen if the session doesn't exist already, show an error just in case
                Log.Error(
                    $"Failed trying to created a session of type {nameof(GrowthMomentSession)} - prior session did not exist and creating session failed.");
                return null;
            }
        }

        return sess;
    }
}

public class GrowthMomentWindow : Dialog_GrowthMomentChoices
{
#nullable enable
    public GrowthMomentSession? Session => GrowthMomentSession.GetSessionFor(letter.pawn);

    public GrowthMomentWindow(TaggedString text, ChoiceLetter_GrowthMoment letter) : base(text, letter) =>
        UpdateChoicesFromSession();

    // 6: max number of available passions in vanilla rimworld
    private static List<SkillDef> tmpChosenPassions = new(capacity: 6);

    public override void DoWindowContents(Rect inRect)
    {
        var session = Session;
        if (session == null || letter.pawn.DestroyedOrNull())
        {
            Close();
            return;
        }

        if (session.uiDirty) UpdateChoicesFromSession();

        var prevChosenTrait = chosenTrait;
        chosenPassions.CopyToList(tmpChosenPassions);
        base.DoWindowContents(inRect);
        if (chosenTrait != prevChosenTrait || !tmpChosenPassions.SequenceEqual(chosenPassions))
        {
            var traitIdx = chosenTrait == ChoiceLetter_GrowthMoment.NoTrait
                ? GrowthMomentSession.NoTraitTraitIdx
                : letter.traitChoices.IndexOf(chosenTrait);
            var passionIndexes = chosenPassions.Select(passion => letter.passionChoices.IndexOf(passion)).ToList();
            session.UpdateChoices(traitIdx, passionIndexes);
        }
    }

    private void UpdateChoicesFromSession()
    {
        var session = Session;
        if (session == null) return;
        chosenTrait = session.traitIdx switch
        {
            GrowthMomentSession.NoTraitTraitIdx => ChoiceLetter_GrowthMoment.NoTrait,
            GrowthMomentSession.NullTraitIdx => null,
            _ => letter.traitChoices[session.traitIdx]
        };

        chosenPassions.Clear();
        foreach (var passionIdx in session.passionIndexes)
            chosenPassions.Add(letter.passionChoices[passionIdx]);

        session.uiDirty = false;
    }

    public override void PostClose() => Session?.Tick();

    [MpPrefix(typeof(LetterStack), nameof(LetterStack.ReceiveLetter),
        [typeof(Letter), typeof(string), typeof(int), typeof(bool)])]
    static void ReceiveLetterPatch(Letter let, int delayTicks)
    {
        if (Multiplayer.Client == null || delayTicks != 0) return;
        if (let is ChoiceLetter_GrowthMoment letterGrowth) GrowthMomentSession.TryAddSession(letterGrowth);
    }

    [MpPrefix(typeof(WindowStack), nameof(WindowStack.Add), [typeof(Window)])]
    static bool WindowStackAddPatch(ref Window window)
    {
        if (Multiplayer.Client == null || window is not Dialog_GrowthMomentChoices dialog ||
            dialog.letter.ArchiveView || window is GrowthMomentWindow)
            return true;

        // Unspawned pawn (caravan child): no map-bound session possible - let
        // the vanilla dialog open locally; MakeChoices itself is synced
        if (dialog.letter.pawn.Map == null)
            return true;

        if (GrowthMomentSession.GetSessionFor(dialog.letter.pawn) is { } session)
            session.OpenWindow();
        else
            OpenSessionWindow(dialog.letter);

        return false;
    }

    // The choice itself is faction authority: only the child's faction may
    // apply it (UpdateChoices already guards the live-sync path)
    [MpPrefix(typeof(ChoiceLetter_GrowthMoment), nameof(ChoiceLetter_GrowthMoment.MakeChoices))]
    static bool MakeChoicesGuard(ChoiceLetter_GrowthMoment __instance)
    {
        if (Multiplayer.Client == null || !Multiplayer.ExecutingCmds) return true;
        var session = GrowthMomentSession.GetSessionFor(__instance.pawn);
        if (session == null || session.ownerFactionId < 0 || Faction.OfPlayer?.loadID == session.ownerFactionId)
            return true;

        Log.Message($"MP: ignored growth-moment choice for {__instance.pawn} from non-owner faction {Faction.OfPlayer?.Name}");
        return false;
    }

    [MpPostfix(typeof(ChoiceLetter_GrowthMoment), nameof(ChoiceLetter_GrowthMoment.MakeChoices))]
    static void MakeChoicesPatch(ChoiceLetter_GrowthMoment __instance)
    {
        if (Multiplayer.Client == null) return;
        // The code would work fine without this patch, however, the dialog button under the colonist bar would be
        // removed only after a tick passed. Thanks to this patch, it is instant.
        if (!__instance.choiceMade) return;
        GrowthMomentSession.GetSessionFor(__instance.pawn)?.Tick();
    }

    [SyncMethod]
    static void OpenSessionWindow(ChoiceLetter_GrowthMoment letter)
    {
        var sess = GrowthMomentSession.TryAddSession(letter);
        if (Multiplayer.ExecutingCmds && TickPatch.currentExecutingCmdIssuedBySelf) sess?.OpenWindow();
    }
}
