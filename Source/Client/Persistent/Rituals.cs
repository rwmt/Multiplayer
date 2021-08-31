using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static Verse.Widgets;

namespace Multiplayer.Client.Persistent
{
    public class RitualSession : ISession
    {
        public Map map;
        public RitualData data;

        public int SessionId { get; private set; }

        public RitualSession(Map map)
        {
            this.map = map;
        }

        public RitualSession(Map map, RitualData data)
        {
            SessionId = Multiplayer.GlobalIdBlock.NextId();

            this.map = map;
            this.data = data;
            this.data.Assignments.session = this;
        }

        [SyncMethod]
        public void Remove()
        {
            map.MpComp().ritualSession = null;
        }

        [SyncMethod]
        public void Start()
        {
            if (data.Action != null && data.Action(data.Assignments))
                Remove();
        }

        public void OpenWindow()
        {
            var dialog = new BeginRitualProxy(
                null,
                data.RitualLabel,
                data.Ritual,
                data.Target,
                map,
                data.Action,
                data.Organizer,
                data.Obligation,
                null,
                data.ConfirmText,
                null,
                null,
                null,
                data.Outcome,
                data.ExtraInfos,
                null
            )
            {
                assignments = data.Assignments
            };

            Find.WindowStack.Add(dialog);
        }

        public void Write(ByteWriter writer)
        {
            writer.WriteInt32(SessionId);
            writer.MpContext().map = map;

            RitualData.Write(writer, data);
        }

        public void Read(ByteReader reader)
        {
            SessionId = reader.ReadInt32();
            reader.MpContext().map = map;

            data = RitualData.Read(reader);
            data.Assignments.session = this;
        }
    }

    public class MpRitualAssignments : RitualRoleAssignments
    {
        public RitualSession session;
    }

    public class BeginRitualProxy : Dialog_BeginRitual, ISwitchToMap
    {
        public static BeginRitualProxy drawing;

        public RitualSession Session => map.MpComp().ritualSession;

        public BeginRitualProxy(string header, string ritualLabel, Precept_Ritual ritual, TargetInfo target, Map map, ActionCallback action, Pawn organizer, RitualObligation obligation, Func<Pawn, bool, bool, bool> filter = null, string confirmText = null, List<Pawn> requiredPawns = null, Dictionary<string, Pawn> forcedForRole = null, string ritualName = null, RitualOutcomeEffectDef outcome = null, List<string> extraInfoText = null, Pawn selectedPawn = null) : base(header, ritualLabel, ritual, target, map, action, organizer, obligation, filter, confirmText, requiredPawns, forcedForRole, ritualName, outcome, extraInfoText, selectedPawn)
        {
            soundClose = SoundDefOf.TabClose;
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

    [HarmonyPatch]
    static class HandleStartRitual
    {
        static MethodBase TargetMethod()
        {
            return MpUtil.GetLocalFunc(
                typeof(Dialog_BeginRitual),
                nameof(Dialog_BeginRitual.DoWindowContents),
                localFunc: "Start"
            );
        }

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
            return Multiplayer.Client == null || window.GetType() != typeof(Dialog_BeginRitual);
        }
    }

    [HarmonyPatch(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.PostOpen))]
    static class CancelDialogBeginRitualPostOpen
    {
        static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [HarmonyPatch]
    static class DialogBeginRitualCtorPatch
    {
        static MethodBase TargetMethod() => AccessTools.GetDeclaredConstructors(typeof(Dialog_BeginRitual))[0];

        static void Postfix(Dialog_BeginRitual __instance, Func<Pawn, bool, bool, bool> filter, Map map)
        {
            if (Multiplayer.Client == null || __instance is BeginRitualProxy) return;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                // Completes initialization. Taken from Dialog_BeginRitual.PostOpen
                __instance.assignments.FillPawns(filter);

                var comp = map.MpComp();

                if (comp.ritualSession != null &&
                    (comp.ritualSession.data.Ritual != __instance.ritual ||
                    comp.ritualSession.data.Outcome != __instance.outcome))
                {
                    Messages.Message("MpAnotherRitualInProgress".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (comp.ritualSession == null)
                {
                    comp.CreateRitualSession(new RitualData(
                        __instance.ritual,
                        __instance.target,
                        __instance.obligation,
                        __instance.outcome,
                        __instance.extraInfos,
                        __instance.action,
                        __instance.ritualLabel,
                        __instance.confirmText,
                        __instance.organizer,
                        MpUtil.ShallowCopy(__instance.assignments, new MpRitualAssignments())
                    ));
                }

                if (TickPatch.currentExecutingCmdIssuedBySelf)
                    comp.ritualSession.OpenWindow();
            }
        }
    }

}
