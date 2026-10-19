using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

public class RitualSession : SemiPersistentSession
{
    public Map map;
    public RitualData data;

    public override Map Map => map;

    public RitualSession(Map map) : base(map)
    {
        this.map = map;
    }

    public RitualSession(Map map, RitualData data) : this(map)
    {
        this.data = data;
        this.data.assignments.session = this;
    }

    [SyncMethod]
    public void Remove()
    {
        map.MpComp().sessionManager.RemoveSession(this);
    }

    // Called by the starting player's dialog
    public void Start() => StartWithAssignments(data.assignments);

    // Without Prepatcher the role selection widget's methods aren't synced, so each client may have its own role
    // assignments by now. The starting player's assignments are sent along and applied on every client (with
    // Prepatcher they're the same everywhere already).
    [SyncMethod(exposeParameters = new[] { 0 })]
    private void StartWithAssignments(MpRitualAssignments startingAssignments)
    {
        // Into the session's object, which the open dialogs and the assignments' sync worker refer to
        if (startingAssignments != null)
            ApplyStartingAssignments(startingAssignments, data.assignments);

        // Handle the same stuff as Dialog_BeginGravshipLaunch
        if (data.isGravshipRitual)
        {
            if (data.ritual.behavior is RitualBehaviorWorker_GravshipLaunch behavior)
            {
                behavior.forceVisitorsToLeave = data.forceVisitorsToLeave;
                behavior.boardColonyAnimals = data.boardColonyAnimals;
                behavior.boardColonyMechs = data.boardColonyMechs;
            }
            else Log.Error($"Gravship ritual is using an incorrect ritual behavior. Expected {nameof(RitualBehaviorWorker_GravshipLaunch)} (or its subtype), received: {(data.ritual.behavior?.GetType()).ToStringSafe()}");
        }

        if (data.action != null && data.action(data.assignments))
            Remove();
    }

    // Only what the role selection widgets change, the rest stays from the live object. E.g. the ritual (and so the
    // roles) is saved by reference and precepts created during the game (a new ideoligion, a reform) aren't in the
    // shared cross refs, so they'd read back as null. Pawns which didn't resolve (despawned) are left out.
    private static void ApplyStartingAssignments(RitualRoleAssignments from, RitualRoleAssignments to)
    {
        if (from.assignedRoles != null)
        {
            foreach (var pawns in from.assignedRoles.Values)
                pawns?.RemoveAll(p => p == null);
            to.assignedRoles = from.assignedRoles;
        }

        if (from.spectators != null)
        {
            from.spectators.RemoveAll(p => p == null);
            to.spectators = from.spectators;
        }

        if (from.roleChangeSelection != null)
            to.roleChangeSelection = from.roleChangeSelection;
    }

    public void OpenWindow(bool sound = true)
    {
        var dialog = new RitualBeginProxy(
            data.assignments,
            data.ritualLabel,
            data.ritual,
            data.target,
            map,
            data.action,
            data.organizer,
            data.obligation,
            null,
            data.confirmText,
            null,
            null,
            data.outcome,
            data.extraInfos,
            null
        );

        if (!sound)
            dialog.soundAppear = null;

        Find.WindowStack.Add(dialog);
    }

    public override void Sync(SyncWorker sync)
    {
        if (sync.isWriting)
        {
            sync.Write(data);
        }
        else
        {
            data = sync.Read<RitualData>();
            data.assignments.session = this;
        }
    }

    public override bool IsCurrentlyPausing(Map map) => map == this.map;

    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
    {
        return new FloatMenuOption("MpRitualSession".Translate(), () =>
        {
            SwitchToMapOrWorld(entry.map);
            OpenWindow();
        });
    }

    // This method is only really relevant (and used by) gravship ritual sessions
    [SyncMethod]
    public void SetGravshipRitualData(bool forceVisitorsToLeave, bool boardColonyAnimals, bool boardColonyMechs)
    {
        data.forceVisitorsToLeave = forceVisitorsToLeave;
        data.boardColonyAnimals = boardColonyAnimals;
        data.boardColonyMechs = boardColonyMechs;
    }
}
