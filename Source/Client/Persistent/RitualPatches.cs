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
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
    static class MakeCancelRitualButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (RitualBeginProxy.drawing == null) return;
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
                RitualBeginProxy.drawing.Session?.Remove();
                __result = DraggableResult.Idle;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.Start))]
    static class HandleStartRitual
    {
        static bool Prefix(Dialog_BeginRitual __instance)
        {
            if (__instance is RitualBeginProxy proxy)
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
                && window.GetType() == typeof(Dialog_BeginRitual) // Doesn't let RitualBeginProxy through
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
