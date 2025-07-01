using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Client.Persistent;
using Multiplayer.Common;
using RimWorld;
using Verse;
using Verse.AI.Group;
using static Multiplayer.Client.SyncSerialization;
// ReSharper disable RedundantLambdaParameterType

namespace Multiplayer.Client
{
    public static class SyncDictDlc
    {
		internal static SyncWorkerDictionaryTree syncWorkers = new()
        {
            #region Royalty
            {
                (SyncWorker sync, ref Pawn_RoyaltyTracker royalty) => {
                    if (sync.isWriting) {
                        sync.Write(royalty.pawn);
                    }
                    else {
                        royalty = sync.Read<Pawn>().royalty;
                    }
                }
            },
            {
                (SyncWorker sync, ref RoyalTitlePermitWorker worker) => {
                    if (sync.isWriting) {
                        sync.Write(worker.def);
                    }
                    else {
                        worker = sync.Read<RoyalTitlePermitDef>().Worker;
                    }
                }, true // Implicit
            },
            {
                // Parent: RoyalTitlePermitWorker
                (SyncWorker sync, ref RoyalTitlePermitWorker_Targeted targeted) => {
                    if (sync.isWriting) {
                        sync.Write(targeted.free);
                        sync.Write(targeted.caller);
                        sync.Write(targeted.map);
                    }
                    else {
                        targeted.free = sync.Read<bool>();
                        targeted.caller = sync.Read<Pawn>();
                        targeted.map = sync.Read<Map>();
                    }
                }, true // Implicit
            },
            {
                // Parent: RoyalTitlePermitWorker_Targeted
                (SyncWorker sync, ref RoyalTitlePermitWorker_CallAid callAid) => {
                    if (sync.isWriting) {
                        sync.Write(callAid.calledFaction);
                        sync.Write(callAid.biocodeChance);
                    }
                    else {
                        callAid.calledFaction = sync.Read<Faction>();
                        callAid.biocodeChance = sync.Read<float>();
                    }
                }, true // Implicit
            },
            {
                // Parent: RoyalTitlePermitWorker_Targeted
                (SyncWorker sync, ref RoyalTitlePermitWorker_CallShuttle callShuttle) => {
                    if (sync.isWriting) {
                        sync.Write(callShuttle.calledFaction);
                    }
                    else {
                        callShuttle.calledFaction = sync.Read<Faction>();
                    }
                }, true // Implicit
            },
            {
                // Parent: RoyalTitlePermitWorker_Targeted
                (SyncWorker sync, ref RoyalTitlePermitWorker_OrbitalStrike orbitalStrike) => {
                    if (sync.isWriting) {
                        sync.Write(orbitalStrike.faction);
                    }
                    else {
                        orbitalStrike.faction = sync.Read<Faction>();
                    }
                }, true // Implicit
            },
            {
                // Parent: RoyalTitlePermitWorker_Targeted
                (SyncWorker sync, ref RoyalTitlePermitWorker_DropResources dropResources) => {
                    if (sync.isWriting) {
                        sync.Write(dropResources.faction);
                    }
                    else {
                        dropResources.faction = sync.Read<Faction>();
                    }
                }, true // Implicit
            },
            {
                // Parent: RoyalTitlePermitWorker_Targeted
                (SyncWorker sync, ref RoyalTitlePermitWorker_CallLaborers dropResources) => {
                    if (sync.isWriting) {
                        sync.Write(dropResources.calledFaction);
                    }
                    else {
                        dropResources.calledFaction = sync.Read<Faction>();
                    }
                }, true // Implicit
            },
            {
                (ByteWriter data, Command_BestowerCeremony cmd) => {
                    WriteSync(data, cmd.job.lord);
                    WriteSync(data, cmd.bestower);
                },
                (ByteReader data) => {
                    var lord = ReadSync<Lord>(data);
                    if (lord == null) return null;

                    var bestower = ReadSync<Pawn>(data);
                    if (bestower == null) return null;

                    return (Command_BestowerCeremony)(lord.curLordToil as LordToil_BestowingCeremony_Wait)?.
                        GetPawnGizmos(bestower).
                        FirstOrDefault();
                }
            },
            {
                (ByteWriter data, ShipJob job) => {
                    WriteSync(data, job.transportShip.ShuttleComp);
                    data.WriteInt32(job.loadID);
                },
                (ByteReader data) => {
                    var ship = ReadSync<CompShuttle>(data).shipParent;
                    var id = data.ReadInt32();
                    if (ship.curJob?.loadID == id) return ship.curJob;
                    return ship.shipJobs.FirstOrDefault(j => j.loadID == id);
                },
                true
            },
            #endregion

            #region Ideology
            {
                (ByteWriter data, Ideo ideo) => {
                    data.WriteInt32(ideo?.id ?? -1);
                },
                (ByteReader data) => {
                    var id = data.ReadInt32();
                    return Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => i.id == id);
                }
            },
            {
                (ByteWriter data, Precept precept) => {
                    WriteSync(data, precept?.ideo);
                    data.WriteInt32(precept?.Id ?? -1);
                },
                (ByteReader data) => {
                    var ideo = ReadSync<Ideo>(data);
                    var id = data.ReadInt32();
                    return ideo?.PreceptsListForReading.FirstOrDefault(p => p.Id == id);
                },
                true
            },
            {
                (ByteWriter data, RitualObligation obligation) => {
                    WriteSync(data, obligation?.precept);
                    data.WriteInt32(obligation?.ID ?? -1);
                },
                (ByteReader data) => {
                    var ritual = ReadSync<Precept_Ritual>(data);
                    var id = data.ReadInt32();
                    return ritual?.activeObligations.FirstOrDefault(r => r.ID == id);
                },
                true
            },
            {
                (ByteWriter data, RitualRoleAssignments assignments) => {
                    // In Multiplayer, RitualRoleAssignments should only be of the wrapper type MpRitualAssignments
                    var mpAssignments = (MpRitualAssignments)assignments;
                    data.MpContext().map = mpAssignments.session.map;
                    data.WriteInt32(mpAssignments.session.SessionId);
                },
                (ByteReader data) => {
                    var id = data.ReadInt32();
                    var ritual = data.MpContext().map.MpComp().sessionManager.GetFirstWithId<RitualSession>(id);
                    return ritual?.data.assignments;
                }
            },
            {
                // Currently only used for Dialog_BeginRitual delegate syncing
                (ByteWriter data, PawnRitualRoleSelectionWidget dialog) => {
                    WriteSync(data, dialog.ritualAssignments);
                    WriteSync(data, dialog.ritual);
                },
                (ByteReader data) => {
                    var assignments = (MpRitualAssignments)ReadSync<RitualRoleAssignments>(data);
                    if (assignments == null) return null;

                    var ritual = ReadSync<Precept_Ritual>(data); // todo handle ritual becoming null?
                    return new PawnRitualRoleSelectionWidget(assignments, ritual, assignments.session.data.target, assignments.session.data.outcome);
                }
            },
            {
                // Currently only used for Dialog_BeginRitual delegate syncing
                (ByteWriter data, Dialog_BeginRitual dialog) => {
                    WriteSync(data, dialog.assignments);
                    WriteSync(data, dialog.ritual);
                },
                (ByteReader data) => {
                    var assignment = (MpRitualAssignments)ReadSync<RitualRoleAssignments>(data);
                    if (assignment == null) return null;

                    var ritual = ReadSync<Precept_Ritual>(data); // todo handle ritual becoming null?
                    return new Dialog_BeginRitual(assignment, ritual, assignment.ritualTarget, assignment.session.data.outcome);
                }
            },
            {
                // This dialog has nothing of interest to us besides the methods which we need for syncing
                (ByteWriter _, Dialog_StyleSelection _) => { },
                (ByteReader _) => new Dialog_StyleSelection()
            },
            #endregion

            #region Biotech
            {
                (ByteWriter data, Gene gene) =>
                {
                    WriteSync(data, gene.def);
                    WriteSync(data, gene.pawn);
                },
                (ByteReader data) =>
                {
                    var geneDef = ReadSync<GeneDef>(data);
                    var pawn = ReadSync<Pawn>(data);

                    return pawn.genes.GetGene(geneDef);
                }, true // implicit
            },
            {
                (ByteWriter data, GeneGizmo_Resource gizmo) => WriteSync(data, gizmo.gene),
                (ByteReader data) =>
                {
                    var gene = ReadSync<Gene_Resource>(data);
                    // Normally created inside of Gene_Resource.GetGizmos
                    // Alternatively we could iterating through that enumerable to make it initialize - which should handle situations of mods (or updates)
                    // making custom gizmo initialization - this would end up with messier looking code.
                    gene.gizmo ??= (GeneGizmo_Resource)Activator.CreateInstance(gene.def.resourceGizmoType, gene, gene.DrainGenes, gene.BarColor, gene.BarHighlightColor);

                    return gene.gizmo;
                }
            },
            {
                (ByteWriter data, IGeneResourceDrain resourceDrain) =>
                {
                    if (resourceDrain is Gene gene)
                        WriteSync(data, gene);
                    else
                        throw new Exception($"Unsupported {nameof(IGeneResourceDrain)} type: {resourceDrain.GetType()}");
                },
                (ByteReader data) => ReadSync<Gene>(data) as IGeneResourceDrain
            },
            {
                (ByteWriter data, MechanitorControlGroup group) =>
                {
                    WriteSync(data, group.tracker.Pawn);
                    data.WriteInt32(group.tracker.controlGroups.IndexOf(group));
                },
                (ByteReader data) =>
                {
                    var mechanitor = ReadSync<Pawn>(data).mechanitor;
                    var index = data.ReadInt32();

                    return mechanitor.controlGroups[index];
                }
            },
            {
                (ByteWriter data, MechCarrierGizmo gizmo) =>
                {
                    WriteSync(data, gizmo.carrier);
                },
                (ByteReader data) =>
                {
                    var comp = ReadSync<CompMechCarrier>(data);
                    comp.gizmo ??= new MechCarrierGizmo(comp);
                    return comp.gizmo;
                }
            },
            {
                (ByteWriter data, Dialog_CreateXenogerm dialog) => WriteSync(data, dialog.geneAssembler),
                (ByteReader data) =>
                {
                    var assembler = ReadSync<Building_GeneAssembler>(data);
                    // Return the currently open dialog (if any) to refresh the data - else create a dummy dialog
                    return Find.WindowStack.Windows
                               .OfType<Dialog_CreateXenogerm>()
                               .FirstOrDefault(d => d.geneAssembler == assembler)
                           ?? new Dialog_CreateXenogerm(assembler);
                }
            },
            #endregion

            #region Anomaly

            {
                (ByteWriter data, ActivityGizmo gizmo) => WriteSync(data, gizmo.Comp),
                (ByteReader data) =>
                {
                    var comp = ReadSync<CompActivity>(data);

                    // The gizmo may not yet be initialized for the comp.
                    comp.gizmo ??= new ActivityGizmo(comp.parent);

                    return (ActivityGizmo)comp.gizmo;
                }
            },
            {
                (ByteWriter data, PsychicRitualRoleAssignments assignments) =>
                {
                    // In Multiplayer, PsychicRitualRoleAssignments should only be of the wrapper type MpRitualAssignments
                    var mpAssignments = (MpPsychicRitualAssignments)assignments;
                    data.MpContext().map = mpAssignments.session.map;
                    data.WriteInt32(mpAssignments.session.SessionId);
                },
                (ByteReader data) =>
                {
                    var id = data.ReadInt32();
                    var ritual = data.MpContext().map.MpComp().sessionManager.GetFirstWithId<PsychicRitualSession>(id);
                    return ritual?.assignments;
                }
            },
            {
                (ByteWriter data, PsychicRitualCandidatePool candidatePool) =>
                {
                    WriteSync(data, candidatePool.AllCandidatePawns);
                    WriteSync(data, candidatePool.NonAssignablePawns);
                },
                (ByteReader data) =>
                {
                    var allCandidates = ReadSync<List<Pawn>>(data);
                    var nonAssignable = ReadSync<List<Pawn>>(data);

                    return new PsychicRitualCandidatePool(allCandidates, nonAssignable);
                }
            },
            {
                (ByteWriter data, PsychicRitual ritual) =>
                {
                    var lordToil = ritual?.lord?.CurLordToil as LordToil_PsychicRitual;
                    WriteSync(data, lordToil);

                    // We could skip syncing the ID, I've decided to include it for additional error checking
                    if (lordToil != null)
                        data.WriteInt32(ritual.loadID);
                },
                (ByteReader data) =>
                {
                    var lordToil = ReadSync<LordToil_PsychicRitual>(data);
                    if (lordToil == null)
                        return null;

                    var ritualId = data.ReadInt32();
                    var ritual = lordToil.RitualData.psychicRitual;

                    if (ritual == null)
                    {
                        Log.Error("Psychic ritual was null after syncing");
                        return null;
                    }
                    if (ritual.loadID != ritualId)
                    {
                        Log.Error($"Synced psychic ritual ID did not match after syncing, expected: {ritualId}, current: {ritual.loadID}");
                        return null;
                    }

                    return ritual;
                }, true // implicit
            },
            {
                // Currently only used for Dialog_BeginPsychicRitual delegate syncing
                (ByteWriter data, PawnPsychicRitualRoleSelectionWidget dialog) =>
                {
                    // psychicRitualAssignments and assignments fields store the same object.
                    // If we used assignments field we would have to cast it before syncing.
                    WriteSync(data, dialog.psychicRitualAssignments);
                    WriteSync(data, dialog.ritualDef);
                },
                (ByteReader data) =>
                {
                    var assignments = (MpPsychicRitualAssignments)ReadSync<PsychicRitualRoleAssignments>(data);
                    if (assignments == null) return null;

                    var ritual = ReadSync<PsychicRitualDef>(data); // todo handle ritual becoming null?
                    return new PawnPsychicRitualRoleSelectionWidget(ritual, assignments.session.candidatePool, assignments);
                }
            },

            #endregion
        };
	}
}
