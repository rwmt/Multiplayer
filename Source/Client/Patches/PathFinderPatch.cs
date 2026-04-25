using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

#if true
namespace Multiplayer.Client.Patches
{
    //This is a full patch to replace all the Multithreading race calls in work threads
    internal class PathFinderPatch
    {
        // PathGridDoorsBlockedJob is a cross-tick Unity Job: scheduled at end of MapPreTick(N),
        // completes at start of MapPreTick(N+1). During MapTick(N) it runs concurrently on a
        // worker thread and reads live pawn.Position while the main thread writes
        // pawn.Position = nextCell in Pawn_PathFollower.PatherTick(). Host and client see
        // different enemy positions depending on thread scheduling → different blocked cells →
        // different A* path → different nextCell for the pathing pawn → WillCollideNextCell
        // differs → one client enters the attack branch → ChooseMeleeVerb → Rand.Chance → desync.
        //
        // Fix: snapshot all cached pawn positions on the main thread right after
        // ScheduleBatchedPathJobs (still inside MapPreTick, before any pawn ticking).
        // Worker threads reading pawn.Position during Execute() get the stable snapshot.

        // There are extra datas needed about jobs
        // Related line
        // bool collideWithNonHostile = this.pawn.CurJob != null && (this.pawn.CurJob.collideWithPawns
        //  || this.pawn.CurJob.def.collideWithPawns || this.pawn.jobs.curDriver.collideWithPawns);
        // Rebuild necessary data too
        class PawnSnapShot(Pawn pawn)
        {
            public IntVec3 Position = pawn.Position;
            public bool CollideWithNonHostile = GetCollideWithNoneHostile(pawn);
            public bool ShouldCollideWithPawns = PawnUtility.ShouldCollideWithPawns(pawn);
            public static bool GetCollideWithNoneHostile(Pawn pawn)
            {
                return pawn.CurJob != null && (pawn.CurJob.collideWithPawns
                        || pawn.CurJob.def.collideWithPawns || pawn.jobs.curDriver.collideWithPawns);
            }
        }


        static class PawnPositionSnapshot
        {
            // Written once per tick on the main thread before jobs are dispatched;
            // read concurrently by worker threads. Dictionary is safe for concurrent reads.
            static readonly Dictionary<Pawn, PawnSnapShot> snapshotDict = [];

            public static void Rebuild(IReadOnlyList<Pawn> pawns)
            {
                snapshotDict.Clear();
                foreach (var pawn in pawns)
                    snapshotDict[pawn] = new PawnSnapShot(pawn);
            }

            public static bool TryGet(Pawn pawn, out PawnSnapShot snapshot) =>
                snapshotDict.TryGetValue(pawn, out snapshot);
        }

        [HarmonyPatch(typeof(PathFinder), "ScheduleBatchedPathJobs")]
        static class ScheduleBatchedPathJobsSnapshotPositions
        {
            static readonly FieldInfo CachedPawns =
                AccessTools.Field(typeof(PathFinder), "cachedPawns");

            static void Prefix(PathFinder __instance)
            {
                if (Multiplayer.Client == null) return;
                // Snapshot pawn positions immediately before PathGridDoorsBlockedJob is
                // dispatched so worker threads read stable positions instead of live ones.
                PawnPositionSnapshot.Rebuild((IReadOnlyList<Pawn>)CachedPawns.GetValue(__instance));
            }
        }

        [HarmonyPatch]
        static class PathGridDoorsBlockedJobPositionPatch
        {
            static readonly MethodInfo PositionGetter =
                AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Position));

            static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(PathGridDoorsBlockedJob), "Execute");
                yield return AccessTools.Method(typeof(PathGridDoorsBlockedJob), "CanBlockEver");
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
            {
                foreach (var inst in insts)
                {
                    if (inst.Calls(PositionGetter))
                        inst.operand = AccessTools.Method(
                            typeof(PathGridDoorsBlockedJobPositionPatch), nameof(GetPosition));
                    yield return inst;
                }
            }

            internal static IntVec3 GetPosition(Pawn pawn)
            {
                if (Multiplayer.Client != null
                    && PawnPositionSnapshot.TryGet(pawn, out PawnSnapShot snapshot))
                    return snapshot.Position;
                return pawn.Position;
            }
        }
        [HarmonyPatch]
        static class PathGridDoorsBlockedJobCollideWithNonHostilePatch
        {
            //ShouldCollideWithPawns
            static readonly MethodInfo MethodShouldCollideWithPawns = AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.ShouldCollideWithPawns));
            static MethodInfo MethodShouldCollideWithPawnsGetter = AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.ShouldCollideWithPawns));
            //CollideWithNonHostile
            static readonly MethodInfo MethodCanBlockEver =
                AccessTools.Method(typeof(PathGridDoorsBlockedJob), nameof(PathGridDoorsBlockedJob.CanBlockEver));
            static readonly FieldInfo FieldPawn =
                AccessTools.Field(typeof(PathGridDoorsBlockedJob), nameof(PathGridDoorsBlockedJob.pawn));
            static readonly MethodInfo MethodGetCollideWithNonHostile =
                AccessTools.Method(typeof(PathGridDoorsBlockedJobCollideWithNonHostilePatch), nameof(GetCollideWithNonHostile));
            static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(PathGridDoorsBlockedJob), "Execute");
            }
            // start
            // calls to ShouldCollideWithPawns, replace with cached one
            // calls to CanBlockEver
            // ldarg.0
            // skip all and set result of V_13(collideWithNonHostile) here
            // stloc.s V_13
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
            {
                List<CodeInstruction> insts = e.ToList();
                // ShouldCollideWithPawns
                {
                    foreach (var inst in insts)
                        if (inst.Calls(MethodShouldCollideWithPawns))
                            inst.operand = MethodShouldCollideWithPawnsGetter;
                }
                // CollideWithNonHostile
                {
                    var finder = new CodeFinder(original, insts);

                    int start = finder.Forward(OpCodes.Call, MethodCanBlockEver)
                        .Forward(OpCodes.Ldarg_0);
                    int end = finder.Forward(OpCodes.Stloc_S, 13);

                    insts.RemoveRange(start, end - start);
                    insts.Insert(start,
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, FieldPawn),
                        new CodeInstruction(OpCodes.Call, MethodGetCollideWithNonHostile)
                        );
                }

                return insts;
            }

            internal static bool GetShouldCollideWithPawns(Pawn pawn)
            {
                if (Multiplayer.Client != null
                    && PawnPositionSnapshot.TryGet(pawn, out PawnSnapShot snapshot))
                    return snapshot.ShouldCollideWithPawns;
                return PawnUtility.ShouldCollideWithPawns(pawn);
            }
            internal static bool GetCollideWithNonHostile(Pawn pawn)
            {
                if (Multiplayer.Client != null
                    && PawnPositionSnapshot.TryGet(pawn, out PawnSnapShot snapshot))
                    return snapshot.CollideWithNonHostile;

                return PawnSnapShot.GetCollideWithNoneHostile(pawn);
            }
        }
    }
}
#endif
