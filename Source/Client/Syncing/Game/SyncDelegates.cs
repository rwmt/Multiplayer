using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public static class SyncDelegates
    {
        public static void Init()
        {
            const SyncContext mouseKeyContext = SyncContext.QueueOrder_Down | SyncContext.MapMouseCell;

            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GotoLocationOption), 0).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Goto
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 0).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Arrest
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 5).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Rescue
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 6).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Capture slave
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 7).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Capture prisoner
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 8).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Carry to cryptosleep casket
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 10).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Carry to shuttle
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 15).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Extract relic to inventory
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 27).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Reload
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 3).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to bed
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 4).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to bed (arrest)
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 5).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to transport shuttle
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 6).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to cryptosleep casket

            var extractRelicToArea = SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 16);                   // Extract relic to area
            extractRelicToArea.CancelIfAnyFieldNull().SetContext(mouseKeyContext);
            extractRelicToArea.TransformField("job", Serializer.New(
                (Job job) => (job.targetA.Thing, job.targetC),
                target =>
                {
                    var (container, pos) = target;
                    var containedThing = container.TryGetComp<CompThingContainer>().ContainedThing;
                    var job = JobMaker.MakeJob(JobDefOf.ExtractRelic, container, containedThing, pos);
                    job.count = 1;
                    return job;
                }));

            SyncDelegate.Lambda(typeof(HealthCardUtility), nameof(HealthCardUtility.GenerateSurgeryOption), 1).CancelIfAnyFieldNull(allowed: "part");   // Add medical bill
            SyncDelegate.Lambda(typeof(Command_SetPlantToGrow), nameof(Command_SetPlantToGrow.ProcessInput), 2);                                        // Set plant to grow
            SyncDelegate.Lambda(typeof(Building_Bed), nameof(Building_Bed.SetBedOwnerTypeByInterface), 0).RemoveNullsFromLists("bedsToAffect");         // Set bed owner type
            SyncDelegate.Lambda(typeof(ITab_Bills), nameof(ITab_Bills.FillTab), 2).SetContext(SyncContext.MapSelected).CancelIfNoSelectedMapObjects();  // Add bill

            SyncDelegate.Lambda(typeof(CompLongRangeMineralScanner), nameof(CompLongRangeMineralScanner.CompGetGizmosExtra), 1).SetContext(SyncContext.MapSelected); // Select mineral to scan for

            SyncMethod.Lambda(typeof(CompFlickable), nameof(CompFlickable.CompGetGizmosExtra), 1);      // Toggle flick designation
            SyncMethod.Lambda(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.GetGizmos), 1);   // Toggle release animals
            SyncMethod.Lambda(typeof(Building_TurretGun), nameof(Building_TurretGun.GetGizmos), 2);     // Toggle turret hold fire
            SyncMethod.Lambda(typeof(Building_Trap), nameof(Building_Trap.GetGizmos), 1);               // Toggle trap auto-rearm
            SyncMethod.Lambda(typeof(Building_Door), nameof(Building_Door.GetGizmos), 1);               // Toggle door hold open
            SyncMethod.Lambda(typeof(Zone_Growing), nameof(Zone_Growing.GetGizmos), 1);                 // Toggle zone allow sow
            SyncMethod.Lambda(typeof(Zone_Growing), nameof(Zone_Growing.GetGizmos), 3);                 // Toggle zone allow cut

            SyncMethod.Lambda(typeof(PriorityWork), nameof(PriorityWork.GetGizmos), 0);                 // Clear prioritized work
            SyncMethod.Lambda(typeof(Building_TurretGun), nameof(Building_TurretGun.GetGizmos), 1);     // Reset forced target
            SyncMethod.Lambda(typeof(UnfinishedThing), nameof(UnfinishedThing.GetGizmos), 0);           // Cancel unfinished thing
            SyncMethod.Lambda(typeof(CompTempControl), nameof(CompTempControl.CompGetGizmosExtra), 2);  // Reset temperature

            SyncDelegate.Lambda(typeof(CompTargetable), nameof(CompTargetable.SelectedUseOption), 0); // Use targetable

            SyncDelegate.LambdaInGetter(typeof(Designator), nameof(Designator.RightClickFloatMenuOptions), 0) // Designate all
                .TransformField("things", Serializer.SimpleReader(() => Find.CurrentMap.listerThings.AllThings));
            SyncDelegate.LambdaInGetter(typeof(Designator), nameof(Designator.RightClickFloatMenuOptions), 1) // Remove all designations
                .TransformField("designations", Serializer.SimpleReader(() => Find.CurrentMap.designationManager.allDesignations));

            SyncDelegate.Lambda(typeof(CaravanAbandonOrBanishUtility), nameof(CaravanAbandonOrBanishUtility.TryAbandonOrBanishViaInterface), 1, new[] { typeof(Thing), typeof(Caravan) }).CancelIfAnyFieldNull(); // Abandon caravan thing
            SyncDelegate.Lambda(typeof(CaravanAbandonOrBanishUtility), nameof(CaravanAbandonOrBanishUtility.TryAbandonOrBanishViaInterface), 0, new[] { typeof(TransferableImmutable), typeof(Caravan) }).CancelIfAnyFieldNull(); // Abandon caravan transferable

            SyncDelegate.Lambda(typeof(CaravanAbandonOrBanishUtility), nameof(CaravanAbandonOrBanishUtility.TryAbandonSpecificCountViaInterface), 0, new[] { typeof(Thing), typeof(Caravan) }).CancelIfAnyFieldNull();                  // Abandon thing specific count
            SyncDelegate.Lambda(typeof(CaravanAbandonOrBanishUtility), nameof(CaravanAbandonOrBanishUtility.TryAbandonSpecificCountViaInterface), 0, new[] { typeof(TransferableImmutable), typeof(Caravan) }).CancelIfAnyFieldNull();  // Abandon transferable specific count

            SyncDelegate.Lambda(typeof(CaravanVisitUtility), nameof(CaravanVisitUtility.TradeCommand), 0).CancelIfAnyFieldNull();       // Caravan trade with settlement
            SyncDelegate.Lambda(typeof(FactionGiftUtility), nameof(FactionGiftUtility.OfferGiftsCommand), 0).CancelIfAnyFieldNull();    // Caravan offer gifts

            SyncDelegate.Lambda(typeof(Building_Bed), nameof(Building_Bed.GetFloatMenuOptions), 0).CancelIfAnyFieldNull(); // Use medical bed

            SyncMethod.Lambda(typeof(CompRefuelable), nameof(CompRefuelable.CompGetGizmosExtra), 1);                    // Toggle Auto-refuel
            SyncMethod.Lambda(typeof(CompRefuelable), nameof(CompRefuelable.CompGetGizmosExtra), 2).SetDebugOnly();     // Set fuel to 0
            SyncMethod.Lambda(typeof(CompRefuelable), nameof(CompRefuelable.CompGetGizmosExtra), 3).SetDebugOnly();     // Set fuel to 0.1
            SyncMethod.Lambda(typeof(CompRefuelable), nameof(CompRefuelable.CompGetGizmosExtra), 4).SetDebugOnly();     // Set fuel to max

            SyncMethod.Lambda(typeof(CompShuttle), nameof(CompShuttle.CompGetGizmosExtra), 1);  // Toggle autoload
            SyncMethod.Lambda(typeof(ShipJob_Wait), nameof(ShipJob_Wait.GetJobGizmos), 1);      // Send shuttle

            SyncDelegate.LocalFunc(typeof(RoyalTitlePermitWorker_CallShuttle), nameof(RoyalTitlePermitWorker_CallShuttle.CallShuttleToCaravan), "Launch").ExposeParameter(1);  // Call shuttle permit on caravan

            SyncMethod.Lambda(typeof(MonumentMarker), nameof(MonumentMarker.GetGizmos), 1);                 // Build monument quest - monument marker: cancel/remove marker
            SyncMethod.Lambda(typeof(MonumentMarker), nameof(MonumentMarker.GetGizmos), 4).SetDebugOnly();  // Build monument quest - monument marker: dev build all

            SyncDelegate.Lambda(typeof(CompPlantable), nameof(CompPlantable.BeginTargeting), 3);    // Select cell to plant in with confirmation
            SyncMethod.Lambda(typeof(CompPlantable), nameof(CompPlantable.CompGetGizmosExtra), 0);  // Cancel planting all

            SyncMethod.Lambda(typeof(Pawn_ConnectionsTracker), nameof(Pawn_ConnectionsTracker.GetGizmos), 3);               // Return to healing pod
            SyncMethod.Lambda(typeof(CompTreeConnection), nameof(CompTreeConnection.CompGetGizmosExtra), 1).SetDebugOnly(); // Spawn dryad
            SyncMethod.Lambda(typeof(CompTreeConnection), nameof(CompTreeConnection.CompGetGizmosExtra), 2).SetDebugOnly(); // Increase connection strength by 10%
            SyncMethod.Lambda(typeof(CompTreeConnection), nameof(CompTreeConnection.CompGetGizmosExtra), 3).SetDebugOnly(); // Decrease connection strength by 10%
            SyncMethod.Lambda(typeof(CompDryadHolder), nameof(CompDryadHolder.CompGetGizmosExtra), 0).SetDebugOnly();       // Complete dryad cocoon action

            SyncMethod.Lambda(typeof(CompNeuralSupercharger), nameof(CompNeuralSupercharger.CompGetGizmosExtra), 1); // Neural supercharger: allow temporary pawns to use

            // Biosculpter pod
            SyncDelegate.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.SelectPawnCycleOption), 0); // Start cycle (should be universal for all cycle types, even modded)

            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 1);                // Interrupt cycle (eject contents)
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 3);                // Toggle auto load nutrition
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 5);                // Toggle auto age reversal
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 6).SetDebugOnly(); // Dev complete cycle
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 7).SetDebugOnly(); // Dev advance by 1 day
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 8).SetDebugOnly(); // Dev complete biotuner timer
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 9).SetDebugOnly(); // Dev fill nutrition and ingredients

            SyncDelegate.Lambda(typeof(ITab_Pawn_Visitor), nameof(ITab_Pawn_Visitor.FillTab), 1).SetContext(SyncContext.MapSelected).CancelIfNoSelectedMapObjects(); // Select target prisoner ideology
            SyncDelegate.Lambda(typeof(ITab_Pawn_Visitor), nameof(ITab_Pawn_Visitor.FillTab), 8).SetContext(SyncContext.MapSelected).CancelIfNoSelectedMapObjects(); // Cancel setting slave mode to execution

            SyncMethod.Lambda(typeof(ShipJob_Wait), nameof(ShipJob_Wait.GetJobGizmos), 0);  // Dismiss (unload) shuttle
            SyncMethod.Lambda(typeof(ShipJob_Wait), nameof(ShipJob_Wait.GetJobGizmos), 1);  // Send loaded shuttle

            SyncMethod.Lambda(typeof(Building_PodLauncher), nameof(Building_PodLauncher.GetGizmos), 0);  // Pod launcher gizmo: Build pod

            SyncMethod.Lambda(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.GetGizmos), 0)
                .TransformTarget(Serializer.New(t => t.pawn, (Pawn p) => p.carryTracker));  // Drop carried pawn

            // CompSpawner
            SyncMethod.Lambda(typeof(CompSpawner), nameof(CompSpawner.CompGetGizmosExtra), 0).SetDebugOnly();
            SyncMethod.Lambda(typeof(CompSpawnerHives), nameof(CompSpawnerHives.CompGetGizmosExtra), 0).SetDebugOnly();
            SyncMethod.Lambda(typeof(CompSpawnerItems), nameof(CompSpawnerItems.CompGetGizmosExtra), 0).SetDebugOnly();
            SyncMethod.Lambda(typeof(CompSpawnerPawn), nameof(CompSpawnerPawn.CompGetGizmosExtra), 0).SetDebugOnly();

            SyncMethod.Lambda(typeof(CompCanBeDormant), nameof(CompCanBeDormant.CompGetGizmosExtra), 0).SetDebugOnly();
            SyncMethod.Lambda(typeof(CompSendSignalOnCountdown), nameof(CompSendSignalOnCountdown.CompGetGizmosExtra), 0).SetDebugOnly();

            // CompRechargeable
            SyncMethod.Lambda(typeof(CompRechargeable), nameof(CompRechargeable.CompGetGizmosExtra), 0).SetDebugOnly(); // Recharge
            SyncMethod.Register(typeof(CompRechargeable), nameof(CompRechargeable.Discharge)).SetDebugOnly();

            SyncDelegate.Lambda(typeof(Ability), nameof(Ability.QueueCastingJob), 0, new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) });
            SyncDelegate.Lambda(typeof(Ability), nameof(Ability.QueueCastingJob), 0, new[] { typeof(GlobalTargetInfo) });

            InitRituals();
            InitChoiceLetters();
        }

        private static void InitRituals()
        {
            SyncMethod.Register(typeof(LordJob_Ritual), nameof(LordJob_Ritual.Cancel));
            SyncDelegate.Lambda(typeof(LordJob_Ritual), nameof(LordJob_Ritual.GetPawnGizmos), 0);   // Make pawn leave ritual

            SyncDelegate.Lambda(typeof(LordJob_BestowingCeremony), nameof(LordJob_BestowingCeremony.GetPawnGizmos), 2); // Cancel ceremony
            SyncDelegate.Lambda(typeof(LordJob_BestowingCeremony), nameof(LordJob_BestowingCeremony.GetPawnGizmos), 0); // Make pawn leave ceremony

            SyncDelegate.Lambda(typeof(LordToil_BestowingCeremony_Wait), nameof(LordToil_BestowingCeremony_Wait.ExtraFloatMenuOptions), 0); // Begin bestowing float menu
            SyncMethod.Register(typeof(Command_BestowerCeremony), nameof(Command_BestowerCeremony.ProcessInput)); // Begin bestowing gizmo

            SyncDelegate.Lambda(typeof(CompPsylinkable), nameof(CompPsylinkable.CompFloatMenuOptions), 0); // Psylinkable begin linking

            SyncDelegate.Lambda(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPawnRoleSelection), 0); // Begin role change: remove role
            SyncDelegate.Lambda(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPawnRoleSelection), 3); // Begin role change: assign role

            SyncDelegate.Lambda(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawRoleSelection), 0); // Select role: none
            SyncDelegate.Lambda(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawRoleSelection), 3); // Select role, set confirm text
            SyncDelegate.Lambda(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawRoleSelection), 4); // Select role, no confirm text

            /*
                Ritual dialog

                The UI's main interaction area is split into three types of groups of pawns.
                Each has three action handlers: (drop), (leftclick), (rightclick)
                The names in parenths below indicate what is synced for each handler.

                <Pawn Group>: <Handlers>
                (Zero or more) roles: (local TryAssignReplace, local TryAssign), (null), (delegate)
                Spectators: (assgn.TryAssignSpectate), (local TryAssignAnyRole), (assgn.RemoveParticipant)
                Not participating: (assgn.RemoveParticipant), (delegate), float menus: (assgn.TryAssignSpectate, local TryAssignReplace, local TryAssign)
            */

            var RitualRolesSerializer = Serializer.New(
                (IEnumerable<RitualRole> roles, object target, object[] args) =>
                {
                    var dialog = target.GetPropertyOrField(SyncDelegate.DELEGATE_THIS) as Dialog_BeginRitual;
                    var ids = from r in roles select r.id;
                    return (dialog.ritual.behavior.def, ids);
                },
                (data) => data.ids.Select(id => data.def.roles.FirstOrDefault(r => r.id == id))
            );

            SyncDelegate.LocalFunc(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawPawnList), "TryAssignReplace")
                .TransformArgument(1, RitualRolesSerializer);
            SyncDelegate.LocalFunc(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawPawnList), "TryAssignAnyRole");
            SyncDelegate.LocalFunc(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawPawnList), "TryAssign")
                .TransformArgument(1, RitualRolesSerializer);

            SyncDelegate.Lambda(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawPawnList), 27); // Roles right click delegate (try assign spectate)
            SyncDelegate.Lambda(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawPawnList), 15); // Not participating left click delegate (try assign any role or spectate)

            SyncMethod.Register(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.TryAssignSpectate));
            SyncMethod.Register(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.RemoveParticipant));
        }

        private static void InitChoiceLetters()
        {
            SyncDelegate.Lambda(typeof(ChoiceLetter_ChoosePawn), nameof(ChoiceLetter_ChoosePawn.Option_ChoosePawn), 0); // Choose pawn (currently used for quest rewards)

            SyncMethod.LambdaInGetter(typeof(ChoiceLetter_AcceptJoiner), nameof(ChoiceLetter_AcceptJoiner.Choices), 0); // Accept joiner
            CloseDialogsForExpiredLetters.rejectMethods[typeof(ChoiceLetter_AcceptJoiner)] =
                SyncMethod.LambdaInGetter(typeof(ChoiceLetter_AcceptJoiner), nameof(ChoiceLetter_AcceptJoiner.Choices), 1)
                    .methodDelegate; // Reject joiner

            SyncMethod.LambdaInGetter(typeof(ChoiceLetter_AcceptVisitors), nameof(ChoiceLetter_AcceptVisitors.Option_Accept), 0); // Accept visitors join offer
            CloseDialogsForExpiredLetters.rejectMethods[typeof(ChoiceLetter_AcceptVisitors)] =
                SyncMethod.LambdaInGetter(typeof(ChoiceLetter_AcceptVisitors), nameof(ChoiceLetter_AcceptVisitors.Option_RejectWithCharityConfirmation), 1)
                    .methodDelegate; // Reject visitors join offer

            SyncMethod.LambdaInGetter(typeof(ChoiceLetter_RansomDemand), nameof(ChoiceLetter_RansomDemand.Choices), 0); // Accept ransom demand
            CloseDialogsForExpiredLetters.rejectMethods[typeof(ChoiceLetter_RansomDemand)] =
                SyncMethod.LambdaInGetter(typeof(ChoiceLetter), nameof(ChoiceLetter.Option_Reject), 0)
                    .methodDelegate; // Generic reject (currently only used by ransom demand)
        }

        [MpPrefix(typeof(FormCaravanComp), nameof(FormCaravanComp.GetGizmos), lambdaOrdinal: 0)]
        static bool GizmoFormCaravan(MapParent ___mapParent)
        {
            if (Multiplayer.Client == null) return true;
            GizmoFormCaravan(___mapParent.Map, false);
            return false;
        }

        [MpPrefix(typeof(FormCaravanComp), nameof(FormCaravanComp.GetGizmos), lambdaOrdinal: 1)]
        static bool GizmoReformCaravan(MapParent ___mapParent)
        {
            if (Multiplayer.Client == null) return true;
            GizmoFormCaravan(___mapParent.Map, true);
            return false;
        }

        private static void GizmoFormCaravan(Map map, bool reform)
        {
            var comp = map.MpComp();

            if (comp.caravanForming != null)
                comp.caravanForming.OpenWindow();
            else
                CreateCaravanFormingSession(comp, reform);
        }

        [SyncMethod]
        private static void CreateCaravanFormingSession(MultiplayerMapComp comp, bool reform)
        {
            var session = comp.CreateCaravanFormingSession(reform, null, false);

            if (TickPatch.currentExecutingCmdIssuedBySelf)
            {
                session.OpenWindow();
                AsyncTimeComp.keepTheMap = true;
                Current.Game.CurrentMap = comp.map;
                Find.World.renderer.wantedMode = WorldRenderMode.None;
            }
        }

        [MpPostfix(typeof(CaravanVisitUtility), nameof(CaravanVisitUtility.TradeCommand))]
        static void ReopenTradingWindowLocally(Caravan caravan, Command __result)
        {
            var original = ((Command_Action)__result).action;

            ((Command_Action)__result).action = () =>
            {
                if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any(t => t.trader == CaravanVisitUtility.SettlementVisitedNow(caravan)))
                {
                    Find.WindowStack.Add(new TradingWindow());
                    return;
                }

                original();
            };
        }
    }

}
