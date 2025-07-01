using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Client.Persistent;

public class PsychicRitualSession : SemiPersistentSession, ISessionWithCreationRestrictions
{
    public Map map;
    public PsychicRitualDef ritual;
    public PsychicRitualCandidatePool candidatePool;
    public MpPsychicRitualAssignments assignments;

    public override Map Map => map;

    public PsychicRitualSession(Map map) : base(map)
    {
        this.map = map;
    }

    public PsychicRitualSession(Map map, PsychicRitualDef ritual, PsychicRitualCandidatePool candidatePool, MpPsychicRitualAssignments assignments) : this(map)
    {
        this.ritual = ritual;
        this.assignments = assignments;
        this.candidatePool = candidatePool;
        this.assignments.session = this;
    }

    public static void OpenOrCreateSession(PsychicRitualDef_InvocationCircle ritual, Thing target)
    {
        // We need Find.CurrentMap to match the map we're creating the session in
        var map = Find.CurrentMap;
        if (map != target.Map)
        {
            Log.Error($"Error opening/creating {nameof(PsychicRitualSession)} - current map ({Find.CurrentMap}) does not match ritual spot map ({target.Map}).");
            return;
        }

        var session = map.MpComp().sessionManager.GetFirstOfType<PsychicRitualSession>();
        if (session == null)
            CreateSession(ritual, target);
        else
            session.OpenWindow();
    }

    // Need CurrentMap for PsychicRitualDef.FindCandidatePool call
    [SyncMethod(SyncContext.CurrentMap)]
    public static void CreateSession(PsychicRitualDef_InvocationCircle ritual, Thing target)
    {
        var map = Find.CurrentMap;

        // Get role assignments and candidate pool
        var candidatePool = ritual.FindCandidatePool();
        var assignments = MpUtil.ShallowCopy(ritual.BuildRoleAssignments(target), new MpPsychicRitualAssignments());

        var manager = map.MpComp().sessionManager;
        var session = manager.GetOrAddSession(new PsychicRitualSession(map, ritual, candidatePool, assignments));

        if (TickPatch.currentExecutingCmdIssuedBySelf)
            session.OpenWindow();
    }

    [SyncMethod]
    public void Remove()
    {
        map.MpComp().sessionManager.RemoveSession(this);
    }

    [SyncMethod]
    public void Start()
    {
        Remove();
        ritual.MakeNewLord(assignments);
        Find.PsychicRitualManager.RegisterCooldown(ritual);
    }

    public void OpenWindow(bool sound = true)
    {
        var dialog = new PsychicRitualBeginProxy(
            ritual,
            candidatePool,
            assignments,
            map);

        if (!sound)
            dialog.soundAppear = null;

        Find.WindowStack.Add(dialog);
    }

    public override void Sync(SyncWorker sync)
    {
        sync.Bind(ref ritual);
        sync.Bind(ref candidatePool);

        SyncType assignmentsType = typeof(MpPsychicRitualAssignments);
        assignmentsType.expose = true;
        sync.Bind(ref assignments, assignmentsType);
        assignments.session = this;
    }

    public override bool IsCurrentlyPausing(Map map) => map == this.map;

    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
    {
        return new FloatMenuOption("MpPsychicRitualSession".Translate(), () =>
        {
            SwitchToMapOrWorld(entry.map);
            OpenWindow();
        });
    }

    public bool CanExistWith(Session other) => other is not PsychicRitualSession;
}

public class MpPsychicRitualAssignments : PsychicRitualRoleAssignments
{
    public PsychicRitualSession session;
}
