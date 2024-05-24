using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using static Verse.Widgets;

namespace Multiplayer.Client.Persistent
{
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
            var dialog = new BeginRitualProxy(
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

    public class MpRitualAssignments : RitualRoleAssignments
    {
        public RitualSession session;
    }

    public class BeginRitualProxy : Dialog_BeginRitual, ISwitchToMap
    {
        public static BeginRitualProxy drawing;

        public RitualSession Session => map.MpComp().sessionManager.GetFirstOfType<RitualSession>();

        // In the base type, the unused fields are used to create RitualRoleAssignments which already exist in an MP session
        // They are here to help keep the base constructor in sync with this one
        public BeginRitualProxy(
            RitualRoleAssignments assignments,
            string ritualLabel,
            Precept_Ritual ritual,
            TargetInfo target,
            Map map,
            ActionCallback action,
            Pawn organizer,
            RitualObligation obligation,
            PawnFilter filter = null,
            string okButtonText = null,
            // ReSharper disable once UnusedParameter.Local
            List<Pawn> requiredPawns = null,
            // ReSharper disable once UnusedParameter.Local
            Dictionary<string, Pawn> forcedForRole = null,
            RitualOutcomeEffectDef outcome = null,
            List<string> extraInfoText = null,
            // ReSharper disable once UnusedParameter.Local
            Pawn selectedPawn = null) :
            base(assignments, ritual, target, ritual?.outcomeEffect?.def ?? outcome)
        {
            this.obligation = obligation;
            this.filter = filter;
            this.organizer = organizer;
            this.map = map;
            this.action = action;
            ritualExplanation = ritual?.ritualExplanation;
            this.ritualLabel = ritualLabel;
            this.okButtonText = okButtonText ?? "OK".Translate();
            extraInfos = extraInfoText;

            soundClose = SoundDefOf.TabClose;

            // This gets cancelled in the base constructor if called from ticking/cmd in DontClearDialogBeginRitualCache
            // Note that: cachedRoles is a static field, cachedRoles is only used for UI drawing
            cachedRoles.Clear();
            if (ritual is { ideo: not null })
            {
                cachedRoles.AddRange(ritual.ideo.RolesListForReading.Where(r => !r.def.leaderRole));
                Precept_Role preceptRole = Faction.OfPlayer.ideos.PrimaryIdeo.RolesListForReading.FirstOrDefault(p => p.def.leaderRole);
                if (preceptRole != null)
                    cachedRoles.Add(preceptRole);
                cachedRoles.SortBy(x => x.def.displayOrderInImpact);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            drawing = this;

            try
            {
                var session = Session;

                if (session == null)
                {
                    soundClose = SoundDefOf.Click;
                    Close();
                }

                // Make space for the "Switch to map" button
                inRect.yMin += 20f;

                base.DoWindowContents(inRect);
            }
            finally
            {
                drawing = null;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
    static class MakeCancelRitualButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (BeginRitualProxy.drawing == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        static void Postfix(bool __state, ref DraggableResult __result)
        {
            if (!__state) return;

            GUI.color = Color.white;
            if (__result.AnyPressed())
            {
                BeginRitualProxy.drawing.Session?.Remove();
                __result = DraggableResult.Idle;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.Start))]
    static class HandleStartRitual
    {
        static bool Prefix(Dialog_BeginRitual __instance)
        {
            if (__instance is BeginRitualProxy proxy)
            {
                proxy.Session.Start();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogBeginRitual
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.Client != null
                && window.GetType() == typeof(Dialog_BeginRitual) // Doesn't let BeginRitualProxy through
                && (Multiplayer.ExecutingCmds || Multiplayer.Ticking))
            {
                var tempDialog = (Dialog_BeginRitual)window;
                tempDialog.PostOpen(); // Completes initialization

                var comp = tempDialog.map.MpComp();
                var session = comp.sessionManager.GetFirstOfType<RitualSession>();

                if (session != null &&
                    (session.data.ritual != tempDialog.ritual ||
                     session.data.outcome != tempDialog.outcome))
                {
                    Messages.Message("MpAnotherRitualInProgress".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                if (session == null)
                {
                    var data = new RitualData
                    {
                        ritual = tempDialog.ritual,
                        target = tempDialog.target,
                        obligation = tempDialog.obligation,
                        outcome = tempDialog.outcome,
                        extraInfos = tempDialog.extraInfos,
                        action = tempDialog.action,
                        ritualLabel = tempDialog.ritualLabel,
                        confirmText = tempDialog.confirmText,
                        organizer = tempDialog.organizer,
                        assignments = MpUtil.ShallowCopy(tempDialog.assignments, new MpRitualAssignments())
                    };

                    session = comp.CreateRitualSession(data);
                }

                if (TickPatch.currentExecutingCmdIssuedBySelf)
                    session?.OpenWindow();

                return false;
            }

            return true;
        }
    }

    // Note that cachedRoles is a static field and is only used for UI drawing
    [HarmonyPatch]
    static class DontClearDialogBeginRitualCache
    {
        private static MethodInfo listClear = AccessTools.Method(typeof(List<Precept_Role>), "Clear");

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(Dialog_BeginRitual).GetConstructors(BindingFlags.Public | BindingFlags.Instance).First();
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, ILGenerator gen)
        {
            var list = insts.ToList();
            var brLabel = gen.DefineLabel();

            foreach (var inst in list)
            {
                if (inst.operand == listClear)
                {
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(DontClearDialogBeginRitualCache), nameof(ShouldCancelCacheClear)));
                    yield return new CodeInstruction(OpCodes.Brfalse, brLabel);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ret);
                    yield return new CodeInstruction(OpCodes.Nop) { labels = { brLabel } };
                }

                yield return inst;
            }
        }

        static bool ShouldCancelCacheClear()
        {
            return Multiplayer.Ticking || Multiplayer.ExecutingCmds;
        }
    }

    // We do not sync Ability.QueueCastingJob - instead syncing the delegate method queueing the job.
    // We do this to avoid confirmation dialogs from some of the abilities, like neuroquake and a few ones added in Biotech.
    // This causes an issue with abilities like throne speech or leader speech - the ritual dialog itself is treated as the confirmation dialog.
    // This patch exists to catch abilities that would create ritual "confirmation" dialogs, and instead syncs the call to QueueCastingJob instead.
    // There is no need to do this for global abilities as there are none that create rituals, and (at least up to 1.4) there are no other abilities that create non-confirmation dialogs
    [HarmonyPatch(typeof(Ability), nameof(Ability.QueueCastingJob), typeof(LocalTargetInfo), typeof(LocalTargetInfo))]
    static class RedirectRitualDialogCast
    {
        static bool Prefix(Ability __instance, LocalTargetInfo target, LocalTargetInfo destination)
        {
            if (Multiplayer.Client == null ||
                Multiplayer.ExecutingCmds ||
                Multiplayer.Ticking ||
                !__instance.EffectComps.Any(effect => effect is CompAbilityEffect_StartRitual))
                return true;

            SyncedOpenDialog(__instance, target, destination);
            return false;
        }

        [SyncMethod]
        static void SyncedOpenDialog(Ability ability, LocalTargetInfo target, LocalTargetInfo destination)
            => ability.QueueCastingJob(target, destination);
    }

    // Fixes the issue where the pawn list gets reset to default every time you re-open the dialog.
    // Additionally, due to the pawns being assigned to spectating first in PostOpen through RitualRoleAssignments.TryAssignSpectate,
    // a method which we sync, it called that method after the pawns had their forced assignments. This could cause situations like
    // no pawn being selected for the leader/throne speech ritual (being forced as a spectator instead). This fixes that issue as well.
    [HarmonyPatch(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.PostOpen))]
    static class PreventRepeatedRitualPostOpenCalls
    {
        static bool Prefix() => Multiplayer.Client == null || Multiplayer.ExecutingCmds;
    }


}
