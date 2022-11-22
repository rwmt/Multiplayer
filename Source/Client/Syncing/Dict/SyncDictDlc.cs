using System;
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
		internal static SyncWorkerDictionaryTree syncWorkers = new SyncWorkerDictionaryTree()
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
                (ByteWriter data, LordJob_BestowingCeremony job) => {
                    WriteSync(data, job.lord);
                },
                (ByteReader data) => {
                    var lord = ReadSync<Lord>(data);
                    return lord?.LordJob as LordJob_BestowingCeremony;
                }
            },
            {
                (ByteWriter data, LordToil_BestowingCeremony_Wait toil) => {
                    WriteSync(data, toil.lord);
                },
                (ByteReader data) => {
                    var lord = ReadSync<Lord>(data);
                    return lord?.curLordToil as LordToil_BestowingCeremony_Wait;
                }
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
                (ByteWriter data, RitualRoleAssignments assgn) => {
                    // In Multiplayer, RitualRoleAssignments should only be of the wrapper type MpRitualAssignments
                    var mpAssgn = (MpRitualAssignments)assgn;
                    data.MpContext().map = mpAssgn.session.map;
                    data.WriteInt32(mpAssgn.session.SessionId);
                },
                (ByteReader data) => {
                    var id = data.ReadInt32();
                    var ritual = data.MpContext().map.MpComp().ritualSession;
                    return ritual?.SessionId == id ? ritual.data.assignments : null;
                }
            },
            {
                // Currently only used for Dialog_BeginRitual delegate syncing
                (ByteWriter data, Dialog_BeginRitual dialog) => {
                    WriteSync(data, dialog.assignments);
                    WriteSync(data, dialog.ritual);
                },
                (ByteReader data) => {
                    var assgn = ReadSync<RitualRoleAssignments>(data) as MpRitualAssignments;
                    if (assgn == null) return null;

                    var ritual = ReadSync<Precept_Ritual>(data); // todo handle ritual becoming null?
                    var dlog = MpUtil.NewObjectNoCtor<Dialog_BeginRitual>();
                    dlog.assignments = assgn;
                    dlog.ritual = ritual;
                    dlog.target = assgn.session.data.target;

                    // This is a cache set every frame at the top of Dialog_BeginRitual.DrawPawnList
                    dlog.rolesGroupedTmp = (from r in assgn.AllRolesForReading group r by r.mergeId ?? r.id).ToList();

                    return dlog;
                }
            },
            {
                (ByteWriter data, LordJob_Ritual job) => {
                    WriteSync(data, job.lord);
                },
                (ByteReader data) => {
                    var lord = ReadSync<Lord>(data);
                    return lord?.LordJob as LordJob_Ritual;
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
        };
	}
}
