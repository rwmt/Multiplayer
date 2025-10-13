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
        pawn.Map.MpComp().sessionManager.GetFirstOfType<GrowthMomentSession>(sess => sess.Pawn == pawn);

    // There isn't a trait selected.
    public const int NullTraitIdx = -1; // intentionally the same as the "not found" return value for IndexOf

    // There's a chance to have a trait choice of "No Trait". It's represented by this
    public const int NoTraitTraitIdx = -2;

    private ChoiceLetter_GrowthMoment letter;
    public Pawn Pawn => letter.pawn;
    public int traitIdx = NullTraitIdx;
    public List<int> passionIndexes = [];
    public bool uiDirty; // if true, received or sent an update

    public static GrowthMomentSession Create(ChoiceLetter_GrowthMoment letter) => new(null)
    {
        letter = letter
    };

    // LetterWithTimeout.LastTickBeforeTimeout adjusted to use the map's time instead of Find.TickManager.TicksGame
    public override bool IsCurrentlyPausing(Map map) => Map == map && letter.TimeoutActive &&
                                                        letter.disappearAtTick <= map.AsyncTime().mapTicks + 1 &&
                                                        !letter.ArchiveView;

    public override bool IsSessionValid => !letter.ArchiveView;

    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
    {
        if (Map != entry.map) return null;
        return new FloatMenuOption("MpGrowthMomentSession".Translate(Pawn.Name.ToStringShort), OpenWindow);
    }

    public override Map Map => letter.pawn.Map;

    [SyncMethod]
    public void UpdateChoices(int traitIdx, List<int> passionIndexes)
    {
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
        if (!IsSessionValid) Map.MpComp().sessionManager.RemoveSession(this);
        else if (letter.TimeoutActive && letter.disappearAtTick <= Map.AsyncTime().mapTicks + 1) OpenWindow();
    }

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_References.Look(ref letter, "pawn");
        Scribe_Values.Look(ref traitIdx, "traitIdx");
        Scribe_Collections.Look(ref passionIndexes, "passionIndexes", LookMode.Value);
    }

    [SyncMethod]
    public static GrowthMomentSession TryAddSession(ChoiceLetter_GrowthMoment letter)
    {
        if (letter.ArchiveView) return null;
        var pawn = letter.pawn;
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

        if (GrowthMomentSession.GetSessionFor(dialog.letter.pawn) is { } session)
            session.OpenWindow();
        else
            OpenSessionWindow(dialog.letter);

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
