using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncDelegates
    {
        public static void Init()
        {
            SyncContext mouseKeyContext = SyncContext.QueueOrder_Down | SyncContext.MapMouseCell;

            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass13_0", "<GotoLocationOption>b__0").CancelIfAnyFieldNull().SetContext(mouseKeyContext);  // Goto
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_1", "<AddHumanlikeOrders>b__0").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Arrest
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_7", "<AddHumanlikeOrders>b__5").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Rescue
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_7", "<AddHumanlikeOrders>b__6").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Capture slave
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_7", "<AddHumanlikeOrders>b__7").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Capture prisoner
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_9", "<AddHumanlikeOrders>b__8").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Carry to cryptosleep casket
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_10", "<AddHumanlikeOrders>b__10").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Carry to shuttle
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_21", "<AddHumanlikeOrders>b__27").CancelIfAnyFieldNull().SetContext(mouseKeyContext);  // Reload

            SyncDelegate.Register(typeof(HealthCardUtility), "<>c__DisplayClass26_0", "<GenerateSurgeryOption>b__1").CancelIfAnyFieldNull(without: "part");      // Add medical bill
            SyncDelegate.Register(typeof(Command_SetPlantToGrow), "<>c__DisplayClass5_0", "<ProcessInput>b__2");                                                // Set plant to grow
            SyncDelegate.Register(typeof(Building_Bed), "<>c__DisplayClass52_0", "<SetBedOwnerTypeByInterface>b__0").RemoveNullsFromLists("bedsToAffect");    // Set bed owner type
            SyncDelegate.Register(typeof(ITab_Bills), "<>c__DisplayClass10_2", "<FillTab>b__2").SetContext(SyncContext.MapSelected).CancelIfNoSelectedObjects(); // Add bill

            SyncDelegate.Register(typeof(CompLongRangeMineralScanner), "<>c__DisplayClass7_0", "<CompGetGizmosExtra>b__1").SetContext(SyncContext.MapSelected); // Select mineral to scan for

            SyncMethod.Register(typeof(CompFlickable), "<CompGetGizmosExtra>b__20_1"); // Toggle flick designation
            SyncMethod.Register(typeof(Pawn_PlayerSettings), "<GetGizmos>b__33_1");    // Toggle release animals
            SyncMethod.Register(typeof(Building_TurretGun), "<GetGizmos>b__59_2");     // Toggle turret hold fire
            SyncMethod.Register(typeof(Building_Trap), "<GetGizmos>b__23_1");          // Toggle trap auto-rearm
            SyncMethod.Register(typeof(Building_Door), "<GetGizmos>b__61_1");          // Toggle door hold open
            SyncMethod.Register(typeof(Zone_Growing), "<GetGizmos>b__14_1");           // Toggle zone allow sow
            SyncMethod.Register(typeof(Zone_Growing), "<GetGizmos>b__14_3");           // Toggle zone allow cut
            
            SyncMethod.Register(typeof(PriorityWork), "<GetGizmos>b__17_0");                // Clear prioritized work
            SyncMethod.Register(typeof(Building_TurretGun), "<GetGizmos>b__59_1");          // Reset forced target
            SyncMethod.Register(typeof(UnfinishedThing), "<GetGizmos>b__27_0");             // Cancel unfinished thing
            SyncMethod.Register(typeof(CompTempControl), "<CompGetGizmosExtra>b__8_2");     // Reset temperature

            SyncDelegate.Register(typeof(CompTargetable), "<>c__DisplayClass6_0", "<SelectedUseOption>b__0");           // Use targetable

            SyncDelegate.Register(typeof(Designator), "<>c__DisplayClass32_1", "<get_RightClickFloatMenuOptions>b__0"); // Designate all
            SyncDelegate.Register(typeof(Designator), "<>c__DisplayClass32_2", "<get_RightClickFloatMenuOptions>b__1"); // Remove all designations

            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass0_0", "<TryAbandonOrBanishViaInterface>b__1").CancelIfAnyFieldNull();      // Abandon caravan thing
            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass1_0", "<TryAbandonOrBanishViaInterface>b__0").CancelIfAnyFieldNull();      // Abandon caravan transferable
            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass2_0", "<TryAbandonSpecificCountViaInterface>b__0").CancelIfAnyFieldNull(); // Abandon thing specific count
            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass3_0", "<TryAbandonSpecificCountViaInterface>b__0").CancelIfAnyFieldNull(); // Abandon transferable specific count

            SyncDelegate.Register(typeof(CaravanVisitUtility), "<>c__DisplayClass2_0", "<TradeCommand>b__0").CancelIfAnyFieldNull();     // Caravan trade with settlement
            SyncDelegate.Register(typeof(FactionGiftUtility), "<>c__DisplayClass1_0", "<OfferGiftsCommand>b__0").CancelIfAnyFieldNull(); // Caravan offer gifts

            SyncDelegate.Register(typeof(Building_Bed), "<>c__DisplayClass54_0", "<GetFloatMenuOptions>b__0").CancelIfAnyFieldNull(); // Use medical bed

            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_1"); // Toggle Auto-refuel
            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_2").SetDebugOnly(); // Set fuel to 0
            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_3").SetDebugOnly(); // Set fuel to 0.1
            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_4").SetDebugOnly(); // Set fuel to max

            SyncMethod.Register(typeof(CompShuttle), "<CompGetGizmosExtra>b__40_1"); // Toggle autoload
            SyncMethod.Register(typeof(ShipJob_Wait), "<GetJobGizmos>b__11_1"); // Send shuttle

            SyncMethod.Register(typeof(MonumentMarker), "<GetGizmos>b__29_1"); // Build Monument Quest - Monument Marker: cancel/remove marker
            SyncMethod.Register(typeof(MonumentMarker), "<GetGizmos>b__29_4").SetDebugOnly(); // Build Monument Quest - Monument Marker: dev build all

            SyncDelegate.Register(typeof(ITab_ContentsTransporter), "<>c__DisplayClass11_0", "<DoItemsLists>b__0").SetContext(SyncContext.MapSelected); // Discard loaded thing
			
            // Inventory (medicine) stock up
            SyncMethod.Register(typeof(Pawn_InventoryStockTracker), nameof(Pawn_InventoryStockTracker.SetCountForGroup));
            SyncMethod.Register(typeof(Pawn_InventoryStockTracker), nameof(Pawn_InventoryStockTracker.SetThingForGroup));

            SyncDelegate.Register(typeof(CompPlantable), "<>c__DisplayClass9_0", "<BeginTargeting>b__3");    // Select cell to plant to after confirmation
            SyncMethod.Register(typeof(CompPlantable), "<CompGetGizmosExtra>b__8_1");                        // Cancel planting
            SyncMethod.Register(typeof(CompTreeConnection), "<CompGetGizmosExtra>b__72_1").SetDebugOnly();   // Spawn dryad
            SyncMethod.Register(typeof(CompTreeConnection), "<CompGetGizmosExtra>b__72_2").SetDebugOnly();   // Increase connection strength by 10%
            SyncMethod.Register(typeof(CompTreeConnection), "<CompGetGizmosExtra>b__72_3").SetDebugOnly();   // Decrease connection strength by 10%

            // (Un)assigning ideology roles
            SyncDelegate.Register(typeof(SocialCardUtility), "<>c__DisplayClass28_0", "<DrawPawnRole>b__2"); // Unnasign role from a pawn
            SyncDelegate.Register(typeof(SocialCardUtility), "<>c__DisplayClass28_2", "<DrawPawnRole>b__8"); // Unassign current role and assign new one to a pawn
            SyncDelegate.Register(typeof(SocialCardUtility), "<>c__DisplayClass28_2", "<DrawPawnRole>b__9"); // Assign a role to a pawn
			
            SyncMethod.Register(typeof(CompNeuralSupercharger), "<CompGetGizmosExtra>b__16_1"); // Neural supercharger: allow temporary pawns to use

            // Biosculpter pod
            SyncDelegate.Register(typeof(CompBiosculpterPod), "<>c__DisplayClass58_0", "<CompGetGizmosExtra>b__0"); // Start cycle (should be universal for all cycle types, even modded)
            SyncMethod.Register(typeof(CompBiosculpterPod), "<CompGetGizmosExtra>b__58_1");                         // Interrupt cucle (eject contents)
            SyncMethod.Register(typeof(CompBiosculpterPod), "<CompGetGizmosExtra>b__58_2");                         // Cancel loading (eject contents)
            SyncMethod.Register(typeof(CompBiosculpterPod), "<CompGetGizmosExtra>b__58_3").SetDebugOnly();          // Dev complete cycle
            SyncMethod.Register(typeof(CompBiosculpterPod), "<CompGetGizmosExtra>b__58_4").SetDebugOnly();          // Dev advance by 1 day
            SyncMethod.Register(typeof(CompBiosculpterPod), "<CompGetGizmosExtra>b__58_5").SetDebugOnly();          // Dev complete biotuner timer

            SyncDelegate.Register(typeof(ITab_Pawn_Visitor), "<>c__DisplayClass7_0", "<FillTab>b__1").SetContext(SyncContext.MapSelected).CancelIfNoSelectedObjects(); // Select target prisoner ideology
            SyncDelegate.Register(typeof(ITab_Pawn_Visitor), "<>c__DisplayClass7_1", "<FillTab>b__8").SetContext(SyncContext.MapSelected).CancelIfNoSelectedObjects(); // Cancel setting slave mode to execution

            SyncMethod.Register(typeof(ShipJob_Wait), "<GetJobGizmos>b__11_0"); // Dismiss (unload) shuttle
            SyncMethod.Register(typeof(ShipJob_Wait), "<GetJobGizmos>b__11_1"); // Send loaded shuttle
        }

        [MpPrefix(typeof(FormCaravanComp), "<>c__DisplayClass17_0", "<GetGizmos>b__0")]
        static bool GizmoFormCaravan(MapParent ___mapParent)
        {
            if (Multiplayer.Client == null) return true;
            GizmoFormCaravan(___mapParent.Map, false);
            return false;
        }

        [MpPrefix(typeof(FormCaravanComp), "<>c__DisplayClass17_0", "<GetGizmos>b__1")]
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
    }

}
