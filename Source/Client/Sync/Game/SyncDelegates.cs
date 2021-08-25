using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncDelegates
    {
        public static void Init()
        {
            SyncContext mouseKeyContext = SyncContext.QueueOrder_Down | SyncContext.MapMouseCell;

            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GotoLocationOption), 0).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Goto
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 0).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Arrest
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 5).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Rescue
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 6).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Capture slave
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 7).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Capture prisoner
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 8).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Carry to cryptosleep casket
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 10).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Carry to shuttle
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 27).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Reload

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

            SyncDelegate.Lambda(typeof(Designator), nameof(Designator.RightClickFloatMenuOptions), 0, parentMethodType: MethodType.Getter) // Designate all
                .TransformField("things", Serializer.New((List<Thing> things, object t, object[] args) => (object)null, obj => Find.CurrentMap.listerThings.AllThings));
            SyncDelegate.Lambda(typeof(Designator), nameof(Designator.RightClickFloatMenuOptions), 1, parentMethodType: MethodType.Getter) // Remove all designations
                .TransformField("designations", Serializer.New((List<Designation> dsgns, object t, object[] args) => (object)null, obj => Find.CurrentMap.designationManager.allDesignations));

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

            SyncMethod.Lambda(typeof(Pawn_ConnectionsTracker), nameof(Pawn_ConnectionsTracker.GetGizmos), 0);               // Return to healing pod
            SyncMethod.Lambda(typeof(CompTreeConnection), nameof(CompTreeConnection.CompGetGizmosExtra), 1).SetDebugOnly(); // Spawn dryad
            SyncMethod.Lambda(typeof(CompTreeConnection), nameof(CompTreeConnection.CompGetGizmosExtra), 2).SetDebugOnly(); // Increase connection strength by 10%
            SyncMethod.Lambda(typeof(CompTreeConnection), nameof(CompTreeConnection.CompGetGizmosExtra), 3).SetDebugOnly(); // Decrease connection strength by 10%
            SyncMethod.Lambda(typeof(CompDryadHolder), nameof(CompDryadHolder.CompGetGizmosExtra), 0).SetDebugOnly();       // Complete dryad cocoon action

            // (Un)assigning ideology roles
            SyncDelegate.Lambda(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPawnRole), 2); // Unassign role from a pawn
            SyncDelegate.Lambda(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPawnRole), 8); // Unassign current role and assign new one to a pawn
            SyncDelegate.Lambda(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPawnRole), 9); // Assign a role to a pawn

            SyncMethod.Lambda(typeof(CompNeuralSupercharger), nameof(CompNeuralSupercharger.CompGetGizmosExtra), 1); // Neural supercharger: allow temporary pawns to use

            // Biosculpter pod
            SyncDelegate.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 0);  // Start cycle (should be universal for all cycle types, even modded)

            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 1);                // Interrupt cucle (eject contents)
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 2);                // Cancel loading (eject contents)
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 3).SetDebugOnly(); // Dev complete cycle
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 4).SetDebugOnly(); // Dev advance by 1 day
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 5).SetDebugOnly(); // Dev complete biotuner timer

            SyncDelegate.Lambda(typeof(ITab_Pawn_Visitor), nameof(ITab_Pawn_Visitor.FillTab), 1).SetContext(SyncContext.MapSelected).CancelIfNoSelectedMapObjects(); // Select target prisoner ideology
            SyncDelegate.Lambda(typeof(ITab_Pawn_Visitor), nameof(ITab_Pawn_Visitor.FillTab), 8).SetContext(SyncContext.MapSelected).CancelIfNoSelectedMapObjects(); // Cancel setting slave mode to execution

            SyncMethod.Lambda(typeof(ShipJob_Wait), nameof(ShipJob_Wait.GetJobGizmos), 0);  // Dismiss (unload) shuttle
            SyncMethod.Lambda(typeof(ShipJob_Wait), nameof(ShipJob_Wait.GetJobGizmos), 1);  // Send loaded shuttle

            SyncMethod.Lambda(typeof(Building_PodLauncher), nameof(Building_PodLauncher.GetGizmos), 0);  // Pod launcher gizmo: Build pod

            InitRituals();
        }

        private static void InitRituals()
        {
            SyncDelegate.Lambda(typeof(LordJob_Ritual), nameof(LordJob_Ritual.GetPawnGizmos), 0);   // Make pawn leave ritual

            SyncDelegate.Lambda(typeof(LordJob_BestowingCeremony), nameof(LordJob_BestowingCeremony.GetPawnGizmos), 2); // Cancel ceremony
            SyncDelegate.Lambda(typeof(LordJob_BestowingCeremony), nameof(LordJob_BestowingCeremony.GetPawnGizmos), 0); // Make pawn leave ceremony

            SyncDelegate.Lambda(typeof(LordToil_BestowingCeremony_Wait), nameof(LordToil_BestowingCeremony_Wait.ExtraFloatMenuOptions), 0); // Begin bestowing float menu
            SyncMethod.Register(typeof(Command_BestowerCeremony), nameof(Command_BestowerCeremony.ProcessInput)); // Begin bestowing gizmo

            SyncDelegate.Lambda(typeof(CompPsylinkable), nameof(CompPsylinkable.CompFloatMenuOptions), 0); // Psylinkable begin linking

            /*
                Ritual dialog

                The UI's main interaction area is split into three types of groups of pawns.
                Each has three action handlers: (drop), (leftclick), (rightclick)
                The names in parenths indicate what is synced for each handler.

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
            SyncDelegate.Lambda(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawPawnList), 14); // Not participating left click delegate (try assign any role or spectate)

            SyncMethod.Register(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.TryAssignSpectate));
            SyncMethod.Register(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.RemoveParticipant));
        }

        [MpPrefix(typeof(FormCaravanComp), nameof(FormCaravanComp.GetGizmos), lambdaOrdinal: 0)]
        static bool GizmoFormCaravan(MapParent ___mapParent)
        {
            if (Multiplayer.Client == null) return true;
            GizmoFormCaravan(___mapParent.Map, false);
            return false;
        }

        [MpPrefix(typeof(FormCaravanComp), nameof(FormCaravanComp.GetGizmos), lambdaOrdinal: 1)]
        static bool GizmoRefomCaravan(MapParent ___mapParent)
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

        [MpTranspiler(typeof(LordJob_Ritual), nameof(LordJob_Ritual.GetCancelGizmo), 0)]
        static IEnumerable<CodeInstruction> GetCancelRitualGizmoTranspiler(IEnumerable<CodeInstruction> insts)
        {
            var target = typeof(LordJob_Ritual).GetMethod(nameof(LordJob_Ritual.Cancel));
            var replacement = AccessTools.Method(typeof(SyncDelegates), nameof(SyncCancelRitual));

            foreach (var inst in insts)
            {
                // Replace original method with our own
                if (inst.operand as MethodInfo == target)
                    inst.operand = replacement;

                yield return inst;
            }
        }

        [SyncMethod]
        static void SyncCancelRitual(LordJob_Ritual ritual) => ritual.Cancel();
    }

}
