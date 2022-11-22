using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client.Patches;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncDelegates
    {
        public static void Init()
        {
            const SyncContext mouseKeyContext = SyncContext.QueueOrder_Down | SyncContext.MapMouseCell;

            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GotoLocationOption), 0).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Goto
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 1).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Arrest
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 8).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Rescue
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 7).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Capture slave
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 9).CancelIfAnyFieldNull().SetContext(mouseKeyContext);     // Capture prisoner
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 10).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Carry to cryptosleep casket
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 12).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Carry to shuttle
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddHumanlikeOrders), 42).CancelIfAnyFieldNull().SetContext(mouseKeyContext);    // Reload
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 3).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to bed
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 4).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to bed (arrest)
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 5).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to transport shuttle
            SyncDelegate.Lambda(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.AddDraftedOrders), 6).CancelIfAnyFieldNull().SetContext(mouseKeyContext);       // Drafted carry to cryptosleep casket

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
            SyncDelegate.LambdaInGetter(typeof(Designator), nameof(Designator.RightClickFloatMenuOptions), 1); // Remove all designations

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

            SyncMethod.Lambda(typeof(CompNeuralSupercharger), nameof(CompNeuralSupercharger.CompGetGizmosExtra), 1); // Neural supercharger: allow temporary pawns to use

            // Biosculpter pod
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 1);                // Interrupt cycle (eject contents)
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 3);                // Toggle auto load nutrition
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 5);                // Toggle auto age reversal
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 6).SetDebugOnly(); // Dev complete cycle
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 7).SetDebugOnly(); // Dev advance by 1 day
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 8).SetDebugOnly(); // Dev complete biotuner timer
            SyncMethod.Lambda(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.CompGetGizmosExtra), 9).SetDebugOnly(); // Dev fill nutrition and ingredients

            SyncDelegate.Lambda(typeof(ITab_Pawn_Visitor), nameof(ITab_Pawn_Visitor.FillTab), 3).SetContext(SyncContext.MapSelected).CancelIfNoSelectedMapObjects();  // Select target prisoner ideology
            SyncDelegate.Lambda(typeof(ITab_Pawn_Visitor), nameof(ITab_Pawn_Visitor.FillTab), 10).SetContext(SyncContext.MapSelected).CancelIfNoSelectedMapObjects(); // Cancel setting slave mode to execution

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

            // Style selection dialog
            SyncDelegate.Lambda(typeof(Dialog_StyleSelection), nameof(Dialog_StyleSelection.DoWindowContents), 0); // Remove style
            SyncDelegate.Lambda(typeof(Dialog_StyleSelection), nameof(Dialog_StyleSelection.DoWindowContents), 1); // Replace style
            SyncDelegate.Lambda(typeof(Dialog_StyleSelection), nameof(Dialog_StyleSelection.DoWindowContents), 2); // Add style

            SyncDelegate.Lambda(typeof(CompActivable_RocketswarmLauncher), nameof(CompActivable_RocketswarmLauncher.TargetLocation), 0);
            SyncDelegate.Lambda(typeof(CompAtomizer), nameof(CompAtomizer.CompGetGizmosExtra), 3).SetDebugOnly(); // Set next atomization
            SyncDelegate.Lambda(typeof(CompBandNode), nameof(CompBandNode.CompGetGizmosExtra), 7); // Select pawn to tune to
            SyncDelegate.Lambda(typeof(CompDissolution), nameof(CompDissolution.CompGetGizmosExtra), 4).SetDebugOnly(); // Set next dissolve time
            SyncDelegate.Lambda(typeof(CompPollutionPump), nameof(CompPollutionPump.CompGetGizmosExtra), 1).SetDebugOnly(); // Set next pollution cycle
            SyncDelegate.Lambda(typeof(CompToxifier), nameof(CompToxifier.CompGetGizmosExtra), 3).SetDebugOnly(); // Set next pollution time
            SyncDelegate.Lambda(typeof(Gene_Deathrest), nameof(Gene_Deathrest.GetGizmos), 5).SetDebugOnly(); // Set capacity

            // Mechanitor
            SyncDelegate.Lambda(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos), 1).SetDebugOnly(); // Recruit
            SyncDelegate.Lambda(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos), 2).SetDebugOnly(); // Kill
            SyncMethod.Register(typeof(MechanitorUtility), nameof(MechanitorUtility.ForceDisconnectMechFromOverseer)); // Disconnect from overseer, only called from FloatMenuMakerMap.<>c__DisplayClass10_19.<AddHumanlikeOrders>b__25
            SyncDelegate.Lambda(typeof(Designator_MechControlGroup), nameof(Designator_MechControlGroup.ProcessInput), 1).SetContext(SyncContext.MapSelected); // Assign to group
            SyncDelegate.Lambda(typeof(MechanitorControlGroupGizmo), nameof(MechanitorControlGroupGizmo.GetWorkModeOptions), 1); // Set work mode for group
            SyncDelegate.Lambda(typeof(PawnColumnWorker_ControlGroup), nameof(PawnColumnWorker_ControlGroup.Button_GenerateMenu), 0); // Assign to group
            SyncDelegate.Lambda(typeof(MainTabWindow_Mechs), nameof(MainTabWindow_Mechs.DoWindowContents), 0); // Change mech color

            // Glower
            SyncMethod.Register(typeof(CompGlower), nameof(CompGlower.SetGlowColorInternal)); // Set color gizmo - will send a separate command per selected glower. Could be fixed with a transpiler for Dialog_GlowerColorPicker
            // Both of those could be handled by SetGlowColorInternal, but we sync them separately to limit the amount of commands sent.
            // With those synced, it'll only send number of commands equal to the selected glowers. Without it, it'll do the same, but repeat it for every single selected glower.
            SyncDelegate.Lambda(typeof(CompGlower), nameof(CompGlower.CompGetGizmosExtra), 2).SetContext(SyncContext.MapSelected); // Paste color
            SyncDelegate.Lambda(typeof(CompGlower), nameof(CompGlower.CompGetGizmosExtra), 3).SetContext(SyncContext.MapSelected); // Toggle darklight

            // Storage groups
            SyncDelegate.Lambda(typeof(StorageGroupUtility), nameof(StorageGroupUtility.StorageGroupMemberGizmos), 2); // Unlink
            SyncDelegate.Lambda(typeof(StorageGroupUtility), nameof(StorageGroupUtility.StorageGroupMemberGizmos), 0)  // Link
                .TransformField("member", Serializer.New(
                    (IStorageGroupMember member) => (member, StorageGroupUtility.tmpMembers),
                    (data) =>
                    {
                        StorageGroupUtility.tmpMembers.Clear();
                        StorageGroupUtility.tmpMembers.AddRange(data.tmpMembers);
                        return data.member;
                    }));

            SyncMethod.Lambda(typeof(HumanEmbryo), nameof(HumanEmbryo.GetGizmos), 2); // Cancel
            SyncDelegate.Lambda(typeof(HumanEmbryo), nameof(HumanEmbryo.CanImplantFloatOption), 1); // Order operation and set the implant target field
            SyncMethod.Lambda(typeof(HumanOvum), nameof(HumanOvum.GetGizmos), 1); // Cancel
            SyncDelegate.LocalFunc(typeof(HumanOvum), nameof(HumanOvum.CanFertilizeFloatOption), "TakeJob"); // Order job and set the fertilizing pawn field

            SyncDelegate.Lambda(typeof(Xenogerm), nameof(Xenogerm.SetTargetPawn), 1); // Select the target - sets up operation (which was synced) and some extra data (which wasn't)
            SyncMethod.Lambda(typeof(Xenogerm), nameof(Xenogerm.GetGizmos), 2);

            // Used by Gene Extractor, Growth Vat, Subcore Scanner, possibly others
            SyncMethod.Register(typeof(Building_Enterable), nameof(Building_Enterable.SelectPawn));
            // GeneExtractor can create a confirmation. Either sync through the call to base class, or by syncing the delegate from confirmation.
            SyncDelegate.Lambda(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.SelectPawn), 0);

            // Genepack Container
            SyncMethod.Register(typeof(ITab_ContentsBase), nameof(ITab_ContentsBase.OnDropThing)).SetContext(SyncContext.MapSelected); // Used by ITab_ContentsGenepackHolder
            SyncDelegate.Lambda(typeof(Dialog_CreateXenogerm), nameof(Dialog_CreateXenogerm.DrawGenepack), 7); // Eject from container

            InitRituals();
            InitChoiceLetters();
            InitDevTools();
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
            CloseDialogsForExpiredLetters.RegisterDefaultLetterChoice(
                SyncMethod.LambdaInGetter(typeof(ChoiceLetter_AcceptJoiner), nameof(ChoiceLetter_AcceptJoiner.Choices), 1)
                    .method); // Reject joiner

            SyncMethod.LambdaInGetter(typeof(ChoiceLetter_AcceptVisitors), nameof(ChoiceLetter_AcceptVisitors.Option_Accept), 0); // Accept visitors join offer
            CloseDialogsForExpiredLetters.RegisterDefaultLetterChoice(
                SyncMethod.LambdaInGetter(typeof(ChoiceLetter_AcceptVisitors), nameof(ChoiceLetter_AcceptVisitors.Option_RejectWithCharityConfirmation), 1)
                    .method); // Reject visitors join offer

            SyncMethod.LambdaInGetter(typeof(ChoiceLetter_RansomDemand), nameof(ChoiceLetter_RansomDemand.Choices), 0); // Accept ransom demand
            CloseDialogsForExpiredLetters.RegisterDefaultLetterChoice(
                SyncMethod.LambdaInGetter(typeof(ChoiceLetter), nameof(ChoiceLetter.Option_Reject), 0)
                    .method, typeof(ChoiceLetter_RansomDemand)); // Generic reject (currently only used by ransom demand)

            // Special case - we could decide to treat making the baby as a colonist the default option, however I've added code to keep the current state
            CloseDialogsForExpiredLetters.RegisterDefaultLetterChoice(AccessTools.Method(typeof(SyncDelegates), nameof(SyncBabyToChildLetter)), typeof(ChoiceLetter_BabyToChild));
            SyncMethod.Register(typeof(ChoiceLetter_BabyToChild), nameof(ChoiceLetter_BabyToChild.ChoseColonist));
            SyncMethod.Register(typeof(ChoiceLetter_BabyToChild), nameof(ChoiceLetter_BabyToChild.ChoseSlave));

            // Naming the baby - use default name generation for them
            CloseDialogsForExpiredLetters.RegisterDefaultLetterChoice(AccessTools.Method(typeof(SyncDelegates), nameof(SetBabyName)), typeof(ChoiceLetter_BabyBirth));

            // Growth moment for a child
            CloseDialogsForExpiredLetters.RegisterDefaultLetterChoice(AccessTools.Method(typeof(SyncDelegates), nameof(PickRandomTraitAndPassions)), typeof(ChoiceLetter_GrowthMoment));
            SyncMethod.Register(typeof(ChoiceLetter_GrowthMoment), nameof(ChoiceLetter_GrowthMoment.MakeChoices)).ExposeParameter(1);
        }

        static void SyncBabyToChildLetter(ChoiceLetter_BabyToChild letter)
        {
            if (letter.bornSlave)
                letter.ChoseSlave();
            else
                letter.ChoseColonist();
        }

        static void SetBabyName(ChoiceLetter_BabyBirth letter)
        {
            var pawn = letter.pawn;

            if (pawn.Name is not NameTriple name || name.First != "Baby".Translate().CapitalizeFirst())
                return;

            // Basically a copy using the rename option from the letter, except that instead of opening the dialog we just apply the auto-generated name
            Rand.PushState();
            Name nameOverride;
            try
            {
                Rand.Seed = pawn.thingIDNumber;
                nameOverride = PawnBioAndNameGenerator.GeneratePawnName(pawn, NameStyle.Full, xenotype: pawn.genes?.Xenotype);
            }
            finally
            {
                Rand.PopState();
            }

            // Includes parent names for name consideration
            var dialog = PawnNamingUtility.NamePawnDialog(pawn, nameOverride is NameTriple tripleOverride ? tripleOverride.First : ((NameSingle)nameOverride).Name);
            var generatedName = dialog.BuildName();
            pawn.Name = generatedName;
            pawn.story.Title ??= dialog.CurPawnTitle;
            pawn.babyNamingDeadline = -1;

            var message = (generatedName is NameTriple generatedTriple)
                ? "PawnGainsName".Translate(generatedTriple.Nick, pawn.story.Title, pawn.Named("PAWN")).AdjustedFor(pawn)
                : "PawnGainsName".Translate(dialog.CurPawnNick, pawn.story.Title, pawn.Named("PAWN")).AdjustedFor(pawn);
            Messages.Message(message, pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        // If the baby ended up being stillborn, the timer to name them is 1 tick. This patch is here to allow players in MP to actually change their name.
        [MpPostfix(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome))]
        static void GiveTimeToNameStillborn(Thing __result)
        {
            if (Multiplayer.Client != null && __result is Pawn pawn && pawn.health.hediffSet.HasHediff(HediffDefOf.Stillborn))
                pawn.babyNamingDeadline = Find.TickManager.TicksGame + 60000;
        }

        static void PickRandomTraitAndPassions(ChoiceLetter_GrowthMoment letter)
        {
            letter.TrySetChoices();

            List<SkillDef> passions = null;
            Trait trait = null;

            if (letter.passionChoiceCount > 0 && letter.passionChoices != null)
                passions = letter.passionChoices.InRandomOrder().Take(letter.passionGainsCount).ToList();

            if (letter.traitChoiceCount > 0 && letter.traitChoices != null)
                trait = letter.traitChoices.RandomElement();

            letter.MakeChoices(passions, trait);
            // Close the letter, or it may auto-open for all players.
            Find.LetterStack.RemoveLetter(letter);
        }

        // The letter tries to generate the options when opened. Make the picks seeded, so all players will get the same ones.
        [MpPrefix(typeof(ChoiceLetter_GrowthMoment), nameof(ChoiceLetter_GrowthMoment.TrySetChoices))]
        static void PreLetterChoices(ChoiceLetter_GrowthMoment __instance)
            => Rand.PushState(Gen.HashCombineInt(__instance.pawn.thingIDNumber, __instance.arrivalTick));

        [MpPostfix(typeof(ChoiceLetter_GrowthMoment), nameof(ChoiceLetter_GrowthMoment.TrySetChoices))]
        static void PostLetterChoices()
            => Rand.PopState();

        static void InitDevTools()
        {
            // GeneUIUtility dev tools (used from multiple places)

            // This won't be compatible with mods that use have no target and use gene overrides.
            // Vanilla Races Expanded - Saurid is one such example.
            var geneSetSerializer = Serializer.New(
                (GeneSet _) =>
                {
                    // Delegates generally use sourcePawn instead of geneSet if it's provided
                    if (geneUIUtilityTarget is Pawn)
                        return (null, true);
                    // Most likely GeneHolderBase
                    if (geneUIUtilityTarget != null)
                        return (geneUIUtilityTarget, true);

                    return (ITab_Genes.PawnForGenes(Find.Selector.SingleSelectedThing), false);
                },
                ((Thing target, bool isDirectOwner) data) =>
                {
                    var (target, isDirectOwner) = data;

                    if (isDirectOwner)
                        return (target as GeneSetHolderBase)?.geneSet;

                    if (target is Pawn pawn)
                        return pawn.health.hediffSet.hediffs.OfType<HediffWithParents>().FirstOrDefault()?.geneSet;

                    return null;
                });
            // Most of the methods don't use the geneSet at all, so let's not bother syncing those
            var geneSetDontSync = Serializer.SimpleReader<GeneSet>(() => null);

            SyncDelegate.Lambda(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton), 4)
                .TransformField("geneSet", geneSetDontSync).SetDebugOnly(); // Add all genes (xenogene)
            SyncDelegate.Lambda(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton), 5)
                .TransformField("geneSet", geneSetDontSync).SetDebugOnly(); // Add all genes (endogene)
            SyncDelegate.Lambda(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton), 7)
                .TransformField("geneSet", geneSetDontSync).SetDebugOnly(); // Reset genes to base xenotype
            SyncDelegate.LocalFunc(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton), "AddGene")
                .TransformField("geneSet", geneSetSerializer).SetDebugOnly(); // Add specified gene/xenogene/endogene
            SyncDelegate.Lambda(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton), 11)
                .TransformField("CS$<>8__locals1/geneSet", geneSetDontSync).SetDebugOnly(); // Remove specified gene
            SyncDelegate.Lambda(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton), 12)
                .TransformField("CS$<>8__locals2/geneSet", geneSetDontSync).SetDebugOnly(); // Apply specified xenotype
            SyncDelegate.Lambda(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton), 13)
                .TransformField("CS$<>8__locals3/geneSet", geneSetSerializer).SetDebugOnly(); // Also remove specified gene

            // ITab_Pawn_Gear debug tools
            SyncDelegate.Lambda(typeof(DebugToolsPawns), nameof(DebugToolsPawns.Options_SetPrimary), 0).SetDebugOnly(); // Remove primary weapon/eqiupment
            SyncDelegate.Lambda(typeof(DebugToolsPawns), nameof(DebugToolsPawns.Options_SetPrimary), 3).SetDebugOnly(); // Set primary weapon/eqiupment
            SyncDelegate.Lambda(typeof(DebugToolsPawns), nameof(DebugToolsPawns.Options_Wear), 0).SetDebugOnly(); // Remove all apparel
            SyncDelegate.Lambda(typeof(DebugToolsPawns), nameof(DebugToolsPawns.Options_Wear), 3).SetDebugOnly(); // Wear specified apparel
            SyncDelegate.Lambda(typeof(DebugToolsPawns), nameof(DebugToolsPawns.Options_GiveToInventory), 0).SetDebugOnly(); // Clear inventory
            SyncDelegate.Lambda(typeof(DebugToolsPawns), nameof(DebugToolsPawns.Options_GiveToInventory), 3).SetDebugOnly(); // Give specified thing
            SyncDelegate.Lambda(typeof(DebugToolsPawns), nameof(DebugToolsPawns.PawnGearDevOptions), 3).SetDebugOnly(); // Damage random apparel

            // ITab_Pawn_Health debug tools
            SyncDelegate.Lambda(typeof(DebugTools_Health), nameof(DebugTools_Health.Options_Hediff_BodyParts), 0).SetDebugOnly(); // Add specified hediff (no body part)
            SyncDelegate.Lambda(typeof(DebugTools_Health), nameof(DebugTools_Health.Options_Hediff_BodyParts), 2).SetDebugOnly(); // Add specified hediff to specified body part
        }

        private static Thing geneUIUtilityTarget;

        [MpPrefix(typeof(GeneUIUtility), nameof(GeneUIUtility.DoDebugButton))]
        static void GeneUIUtilityTarget(Thing target)
        {
            geneUIUtilityTarget = target;
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

        // Target method can either open a confirmation dialog, or just create the bill outright and then set the implant target field.
        // Transpiler replaces the part where it creates the operation and sets the target with our synced method.
        [MpTranspiler(typeof(HumanEmbryo), nameof(HumanEmbryo.CanImplantFloatOption), 0)]
        static IEnumerable<CodeInstruction> HumanEmbryoTranspiler(IEnumerable<CodeInstruction> insts, ILGenerator gen, MethodBase original)
        {
            OpCode? prevCode = null;

            foreach (var ci in insts)
            {
                if (prevCode == OpCodes.Ret)
                {
                    // After encountering the first return - insert our method and return early, ignoring the original code we don't care for
                    var pawnField = AccessTools.Field(original.DeclaringType, "pawn");
                    var embryoField = AccessTools.Field(original.DeclaringType, SyncDelegate.DELEGATE_THIS);
                    var ourMethod = AccessTools.Method(typeof(SyncDelegates), nameof(SyncedCreateImplantEmbryoBill));

                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(ci);
                    yield return new CodeInstruction(OpCodes.Ldfld, pawnField);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, embryoField);
                    yield return new CodeInstruction(OpCodes.Call, ourMethod);
                    yield return new CodeInstruction(OpCodes.Ret);

                    yield break;
                }

                prevCode = ci.opcode;
                yield return ci;

            }
        }

        [SyncMethod]
        static void SyncedCreateImplantEmbryoBill(Pawn pawn, HumanEmbryo embryo)
        {
            HealthCardUtility.CreateSurgeryBill(pawn, RecipeDefOf.ImplantEmbryo, null, new List<Thing> { embryo });
            embryo.implantTarget = pawn;
        }

        // Simply syncing SetPregnancyApproach method, or the delegate that calls it, would cause issues
        // as the pawn we'll need to sync may be on a different map (causing map mismatch error) or not be spawned
        // (causing an error about them being inaccessible).
        [MpTranspiler(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPregnancyApproach), 0)]
        static IEnumerable<CodeInstruction> SocialCardSetPregnancyApproachTranspiler(IEnumerable<CodeInstruction> insts)
        {
            var target = AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.SetPregnancyApproach));
            var replacement = AccessTools.Method(typeof(SyncDelegates), nameof(ReplacedSetPregnancyApproach));

            foreach (var ci in insts)
            {
                if (ci.opcode == OpCodes.Callvirt && ci.operand is MethodInfo method && method == target)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = replacement;
                }

                yield return ci;
            }
        }

        static void ReplacedSetPregnancyApproach(Pawn_RelationsTracker tracker, Pawn partner, PregnancyApproach mode)
        {
            if (Multiplayer.Client == null)
            {
                tracker.SetPregnancyApproach(partner, mode);
                return;
            }

            // Work around pawn being potentially inaccessible/on a different map by getting the index of the direct relation to the pawn
            for (var index = 0; index < tracker.DirectRelations.Count; index++)
            {
                var relation = tracker.DirectRelations[index];

                if (relation.otherPawn == partner)
                {
                    SyncedSetPregnancyApproach(tracker.pawn, index, mode);
                    return;
                }
            }

            Log.Error($"Failed syncing {nameof(Pawn_RelationsTracker.SetPregnancyApproach)} for {tracker.pawn} and {partner}, no direct pawn relations found");
        }

        [SyncMethod]
        static void SyncedSetPregnancyApproach(Pawn target, int index, PregnancyApproach mode)
            => target.relations.SetPregnancyApproach(target.relations.DirectRelations[index].otherPawn, mode);
    }

}
