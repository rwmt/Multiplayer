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

    [SyncMethod]
    public void Start()
    {
        if (data.action != null && data.action(data.assignments))
            Remove();
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
}
