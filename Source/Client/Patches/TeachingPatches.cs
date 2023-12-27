using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches;

[HarmonyPatch(typeof(WorkGiver_Teach), nameof(WorkGiver_Teach.JobOnThing))]
static class WorkGiverTeachUnsetTarget
{
    static bool Prefix(Thing t, ref Job __result)
    {
        // Only prevent the change from happening in interface, as it'll be called from
        // FloatMenuMakerMap.AddJobGiverWorkOrders despite not assigning the job yet.
        if (!Multiplayer.InInterface)
            return true;
        // If target is not pawn, no result
        if (t is not Pawn pawn)
            return false;

        // Alternative approach would be to store the current target of the lesson taking pawn in the
        // prefix before it's changed, and restore it in the finalizer.
        __result = JobMaker.MakeJob(JobDefOf.Lessongiving, pawn.CurJob.GetTarget(TargetIndex.A), pawn);
        return false;
    }
}

[HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
static class PawnJobTrackerSetTeacher
{
    static void Prefix(Pawn_JobTracker __instance, Job job)
    {
        // Check if it's lessongiving job, as we don't want to do anything else in any other case.
        if (job.def != JobDefOf.Lessongiving)
            return;

        // We only need to set the teacher if we're executing a sync command, as the teacher
        // wasn't set in interface due to us no allowing it to do so to prevent a desync.
        if (!Multiplayer.ExecutingCmds)
            return;

        var student = job.GetTarget(TargetIndex.B).Pawn;
        // Make sure the target B (JobDriver_Lessongiving.Student) is a non-null Pawn.
        if (student == null)
            return;

        // Make sure the student is still doing the lessontaking job
        if (student.CurJobDef == JobDefOf.Lessontaking)
            student.CurJob.SetTarget(TargetIndex.B, __instance.pawn);

        // We could check if the pawn can reach their target desk, but it'll
        // be handled by JobDriver_Lessongiving.TryMakePreToilReservations
    }
}
