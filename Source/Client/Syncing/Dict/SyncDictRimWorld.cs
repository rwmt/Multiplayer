using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using static Multiplayer.Client.SyncSerialization;
using static Multiplayer.Client.ImplSerialization;
// ReSharper disable RedundantLambdaParameterType

namespace Multiplayer.Client
{
    public static class SyncDictRimWorld
    {
        internal static SyncWorkerDictionaryTree syncWorkers = new SyncWorkerDictionaryTree()
        {
            #region Pawns
            {
                (ByteWriter data, PriorityWork work) => WriteSync(data, work.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.mindState?.priorityWork
            },
            {
                (ByteWriter data, Pawn_PlayerSettings comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.playerSettings
            },
            {
                (ByteWriter data, Pawn_TimetableTracker comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.timetable
            },
            {
                (ByteWriter data, Pawn_DraftController comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.drafter
            },
            {
                (ByteWriter data, Pawn_WorkSettings comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.workSettings
            },
            {
                (ByteWriter data, Pawn_JobTracker comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.jobs
            },
            {
                (ByteWriter data, Pawn_OutfitTracker comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.outfits
            },
            {
                (ByteWriter data, Pawn_DrugPolicyTracker comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.drugs
            },
            {
                (ByteWriter data, Pawn_FoodRestrictionTracker comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.foodRestriction
            },
            {
                (ByteWriter data, Pawn_TrainingTracker comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.training
            },
            {
                (ByteWriter data, Pawn_StoryTracker comp) => WriteSync(data, comp.pawn),
                (ByteReader data) => ReadSync<Pawn>(data)?.story
            },
            {
                (ByteWriter data, OutfitForcedHandler comp) => WriteSync(data, comp.forcedAps.Select(a => a.Wearer).FirstOrDefault()),
                (ByteReader data) => ReadSync<Pawn>(data)?.outfits?.forcedHandler
            },
            {
                // We assume that the currently open tab holds the table, as it seems to only be used together with MainTabWindow_PawnTable and its subclasses
                (ByteWriter data, PawnTable table) => WriteSync(data, Find.MainTabsRoot.OpenTab),
                (ByteReader data) =>
                {
                    var tab = (MainTabWindow_PawnTable)ReadSync<MainButtonDef>(data).TabWindow;
                    return tab.CreateTable();
                }, true
            },
            {
                (ByteWriter data, Pawn_InventoryStockTracker inventoryTracker) => WriteSync(data, inventoryTracker.pawn),
                (ByteReader data) => ReadSync<Pawn>(data).inventoryStock
            },
            {
                (ByteWriter data, Pawn_ConnectionsTracker connectionTracker) => WriteSync(data, connectionTracker.pawn),
                (ByteReader data) => ReadSync<Pawn>(data).connections
            },
            {
                (SyncWorker sync, ref Hediff hediff) =>
                {
                    if (sync.isWriting)
                    {
                        if (hediff != null)
                        {
                            sync.Write(hediff.loadID);
                            sync.Write(hediff.pawn);
                        }
                        else
                            sync.Write(int.MaxValue);
                    }
                    else
                    {
                        var id = sync.Read<int>();

                        if (id == int.MaxValue)
                            return;

                        var pawn = sync.Read<Pawn>();

                        if (pawn == null)
                        {
                            Log.Error($"Multiplayer :: SyncDictionary.Hediff: pawn is null");
                            return;
                        }

                        hediff = pawn.health.hediffSet.hediffs.First(x => x.loadID == id);

                        if (hediff == null)
                        {
                            Log.Error($"Multiplayer :: SyncDictionary.Hediff: Unknown hediff {id}");
                        }
                    }
                }, true // implicit
            },
            {
                (ByteWriter data, Need need) =>
                {
                    WriteSync(data, need.pawn);
                    WriteSync(data, need.def);
                },
                (ByteReader data) =>
                {
                    var pawn = ReadSync<Pawn>(data);
                    return pawn.needs.TryGetNeed(ReadSync<NeedDef>(data));
                }, true // implicit
            },
            {
                (ByteWriter data, Pawn_MindState mindState) => WriteSync(data, mindState.pawn),
                (ByteReader data) => ReadSync<Pawn>(data).mindState
            },
            #endregion

            #region Policies
            {
                (ByteWriter data, Outfit policy) => {
                    data.WriteInt32(policy.uniqueId);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    return Current.Game.outfitDatabase.AllOutfits.Find(o => o.uniqueId == id);
                }
            },
            {
                (ByteWriter data, DrugPolicy policy) => {
                    data.WriteInt32(policy.uniqueId);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    return Current.Game.drugPolicyDatabase.AllPolicies.Find(o => o.uniqueId == id);
                }
            },
            {
                (ByteWriter data, FoodRestriction policy) => {
                    data.WriteInt32(policy.id);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    return Current.Game.foodRestrictionDatabase.AllFoodRestrictions.Find(o => o.id == id);
                }
            },
            #endregion

            #region Jobs
            {
                (ByteWriter data, WorkGiver workGiver) => {
                    WriteSync(data, workGiver.def);
                },
                (ByteReader data) => {
                    WorkGiverDef def = ReadSync<WorkGiverDef>(data);
                    return def?.Worker;
                }, true
            },
            {
                (ByteWriter data, BillStack obj) => {
                    Thing billGiver = obj?.billGiver as Thing;
                    WriteSync(data, billGiver);
                },
                (ByteReader data) => {
                    Thing thing = ReadSync<Thing>(data);
                    if (thing is IBillGiver billGiver)
                        return billGiver.BillStack;
                    return null;
                }
            },
            {
                (ByteWriter data, Bill bill) => {
                    WriteSync(data, bill.billStack);
                    data.WriteInt32(bill.loadID);
                },
                (ByteReader data) => {
                    BillStack billStack = ReadSync<BillStack>(data);

                    if (billStack == null)
                        return null;

                    int id = data.ReadInt32();

                    return billStack.Bills.Find(bill => bill.loadID == id);
                }, true
            },
            #endregion

            #region Abilities
            {
                (ByteWriter data, Ability ability) => {
                    WriteSync(data, ability.pawn);
                    WriteSync(data, ability.Id);
                },
                (ByteReader data) => {
                    var pawn = ReadSync<Pawn>(data);
                    var abilityId = data.ReadInt32();

                    // Note there exist temporary abilities which might get removed by the time this data is read
                    // The returned ability can be null
                    return pawn.abilities.allAbilitiesCached.Find(ab => ab.Id == abilityId);
                }, true
            },
            {
                (SyncWorker data, ref AbilityComp comp) => {
                    if (data.isWriting) {
                        if (comp != null) {
                            ushort index = (ushort)Array.IndexOf(abilityCompTypes, comp.GetType());
                            data.Write(index);
                            data.Write(comp.parent);
                        } else {
                            data.Write(ushort.MaxValue);
                        }
                    } else {
                        ushort index = data.Read<ushort>();
                        if (index == ushort.MaxValue) {
                            return;
                        }
                        Ability parent = data.Read<Ability>();
                        if (parent == null) {
                            return;
                        }
                        Type compType = abilityCompTypes[index];
                        comp = parent.comps.Find(c => c.props.compClass == compType);
                    }
                }, true // implicit
            },
            #endregion

            #region Verb
            {
                (SyncWorker sync, ref Verb verb)  => {
                    if (sync.isWriting) {

                        if (verb.DirectOwner is Pawn pawn) {
                            sync.Write(VerbOwnerType.Pawn);
                            sync.Write(pawn);
                        }
                        else if (verb.DirectOwner is Ability ability) {
                            sync.Write(VerbOwnerType.Ability);
                            sync.Write(ability);
                        }
                        else if (verb.DirectOwner is ThingComp thingComp) {
                            sync.Write(VerbOwnerType.ThingComp);
                            sync.Write(thingComp);
                        }
                        else {
                            Log.Error($"Multiplayer :: SyncDictionary.Verb: Unknown DirectOwner {verb.loadID} {verb.DirectOwner}");
                            sync.Write(VerbOwnerType.None);
                            return;
                        }

                        sync.Write(verb.loadID);
                    }
                    else {

                        var ownerType = sync.Read<VerbOwnerType>();
                        if (ownerType == VerbOwnerType.None) {
                            return;
                        }

                        IVerbOwner verbOwner = null;
                        if (ownerType == VerbOwnerType.Pawn) {
                            verbOwner = sync.Read<Pawn>();
                        }
                        else if (ownerType == VerbOwnerType.Ability) {
                            verbOwner = sync.Read<Ability>();
                        }
                        else if (ownerType == VerbOwnerType.ThingComp) {
                            verbOwner = sync.Read<ThingComp>() as IVerbOwner;
                        }

                        if (verbOwner == null) {
                            Log.Error($"Multiplayer :: SyncDictionary.Verb: Unknown VerbOwnerType {ownerType}");
                            return;
                        }

                        var loadID = sync.Read<string>();

                        verb = verbOwner.VerbTracker.AllVerbs.Find(ve => ve.loadID == loadID);

                        if (verb == null) {
                            Log.Error($"Multiplayer :: SyncDictionary.Verb: Unknown verb {loadID}");
                        }
                    }
                }, true // implicit
            },
            #endregion

            #region AI
            {
                (ByteWriter data, Lord lord) => {
                    MpContext context = data.MpContext();
                    context.map = lord.Map;
                    data.WriteInt32(lord.loadID);
                },
                (ByteReader data) => {
                    var map = data.MpContext().map;
                    int lordId = data.ReadInt32();
                    return map.lordManager.lords.Find(l => l.loadID == lordId);
                }
            },
            #endregion

            #region Records
            {
                (ByteWriter data, BodyPartRecord part) => {
                    if (part == null) {
                        data.WriteUShort(ushort.MaxValue);
                        return;
                    }

                    BodyDef body = part.body;

                    data.WriteUShort((ushort)body.GetIndexOfPart(part));
                    WriteSync(data, body);
                },
                (ByteReader data) => {
                    ushort partIndex = data.ReadUShort();
                    if (partIndex == ushort.MaxValue) return null;

                    BodyDef body = ReadSync<BodyDef>(data);
                    return body.GetPartAtIndex(partIndex);
                }
            },
            #endregion

            #region Caravans
            {
                (ByteWriter data, Caravan_PathFollower follower) => WriteSync(data, follower.caravan),
                (ByteReader data) => ReadSync<Caravan>(data)?.pather
            },
            {
                (ByteWriter data, WITab_Caravan_Gear tab) => {
                    data.WriteBool(tab.draggedItem != null);
                    if (tab.draggedItem != null) {
                        WriteSync(data, tab.draggedItem);
                    }},
                (ByteReader data) => {
                    bool hasThing = data.ReadBool();
                    Thing thing = null;
                    if (hasThing) {
                        thing = ReadSync<Thing>(data);
                        if (thing == null)
                            return null;
                    }
                    var tab = new WITab_Caravan_Gear{
                        draggedItem = thing
                    };
                    return tab;
                 }
            },
            {
                (ByteWriter data, PawnColumnWorker worker) => WriteSync(data, worker.def),
                (ByteReader data) => {
                    PawnColumnDef def = ReadSync<PawnColumnDef>(data);
                    return def.Worker;
                }, true
            },
            {
                (ByteWriter data, TradeRequestComp trade) => WriteSync(data, trade.parent),
                (ByteReader data) => ReadSync<WorldObject>(data).GetComponent<TradeRequestComp>()
            },
            #endregion

            #region Quests
            {
                (ByteWriter data, Quest quest) => {
                    data.WriteInt32(quest.id);
                },
                (ByteReader data) => {
                    int questId = data.ReadInt32();
                    return Find.QuestManager.QuestsListForReading.FirstOrDefault(possibleQuest => possibleQuest.id == questId);
                },
                true
            },
            {
                (ByteWriter data, QuestPart part) => {
                    WriteSync(data, part.quest);
                    WriteSync(data, part.Index);
                },
                (ByteReader data) => {
                    var quest = ReadSync<Quest>(data);
                    int index = ReadSync<int>(data);

                    return quest.parts[index];
                },
                true
            },
            #endregion

            #region Factions
            {
                (ByteWriter data, Faction faction) => {
                    data.WriteInt32(faction?.loadID ?? -1);
                },
                (ByteReader data) => {
                    int loadID = data.ReadInt32();
                    return Find.FactionManager.AllFactions.FirstOrDefault(possibleFaction => possibleFaction.loadID == loadID);
                },
                true
            },
            #endregion

            #region Tabs
            {
                (ByteWriter data, ITab_Bills tab) => { },
                (ByteReader data) => new ITab_Bills()
            },
            {
                (ByteWriter data, ITab_Pawn_Gear tab) => { },
                (ByteReader data) => new ITab_Pawn_Gear()
            },
            {
                (ByteWriter data, ITab_ContentsBase tab) => WriteSync(data, tab.GetType()),
                (ByteReader data) => (ITab_ContentsBase)Activator.CreateInstance(ReadSync<Type>(data)),
                true // Implicit
            },
            {
                (ByteWriter data, ITab_Pawn_Guest tab) => { },
                (ByteReader data) => new ITab_Pawn_Guest()
            },
            {
                (ByteWriter data, ITab_Pawn_Prisoner tab) => { },
                (ByteReader data) => new ITab_Pawn_Prisoner()
            },
            {
                (ByteWriter data, ITab_Pawn_Slave tab) => { },
                (ByteReader data) => new ITab_Pawn_Slave()
            },
            {
                (ByteWriter data, ITab_Pawn_Visitor tab) => { },
                (ByteReader data) => new Dummy_ITab_Pawn_Visitor()
            },
            #endregion

            #region Commands
            {
                (ByteWriter data, Command_SetPlantToGrow command) => {
                    WriteSync(data, command.settable);
                    WriteSync(data, command.settables);
                },
                (ByteReader data) => {
                    var settable = ReadSync<IPlantToGrowSettable>(data);

                    if (settable == null)
                        return null;

                    var settables = ReadSync<List<IPlantToGrowSettable>>(data);
                    settables.RemoveAll(s => s == null);

                    var command = MpUtil.NewObjectNoCtor<Command_SetPlantToGrow>();
                    command.settable = settable;
                    command.settables = settables;

                    return command;
                }
            },
            {
                (ByteWriter data, Command_SetTargetFuelLevel command) => {
                    WriteSync(data, command.refuelables);
                },
                (ByteReader data) => {
                    List<CompRefuelable> refuelables = ReadSync<List<CompRefuelable>>(data);
                    refuelables.RemoveAll(r => r == null);

                    Command_SetTargetFuelLevel command = new Command_SetTargetFuelLevel();
                    command.refuelables = refuelables;

                    return command;
                }
            },
            {
                (ByteWriter data, Command_LoadToTransporter command) => {
                    WriteSync(data, command.transComp);
                    WriteSync(data, command.transporters ?? new List<CompTransporter>());
                },
                (ByteReader data) => {
                    CompTransporter transporter = ReadSync<CompTransporter>(data);
                    if (transporter == null)
                       return null;

                    List<CompTransporter> transporters = ReadSync<List<CompTransporter>>(data);
                    transporters.RemoveAll(r => r == null);

                    Command_LoadToTransporter command = new Command_LoadToTransporter{
                        transComp = transporter,
                        transporters = transporters
                    };

                    return command;
                }
            },
            {
                (ByteWriter data, Command_Ability command) => {
                    WriteSync(data, command.ability);
                },
                (ByteReader data) => {
                    Ability ability = ReadSync<Ability>(data);

                    return new Command_Ability(ability);
                }
            },
            #endregion

            #region Designators
            {
                // Catch all for all Designators, merely signals to construct them
                // We can't construct them here because we need to signal ReadSyncObject
                // to change the type, which is not possible from a SyncWorker.
                (SyncWorker sync, ref Designator designator) => {

                }, true, true // <- Implicit ShouldConstruct
            },
            {
                (SyncWorker sync, ref Designator_Place place) => {
                    if (sync.isWriting) {
                        sync.Write(place.placingRot);
                    } else {
                        place.placingRot = sync.Read<Rot4>();
                    }
                }, true, true // <- Implicit ShouldConstruct
            },
            {
                // Designator_Build is a Designator_Place but we aren't using Implicit
                // We can't take part of the implicit tree because Designator_Build ctor has an argument
                // So we need to implement placingRot here too, until we separate instancing from decorating.
                (SyncWorker sync, ref Designator_Build build) => {
                    if (sync.isWriting) {
                        sync.Write(build.PlacingDef);
                        sync.Write(build.placingRot);
                        if (build.PlacingDef.MadeFromStuff) {
                            sync.Write(build.stuffDef);
                        }
                        sync.Write(build.sourcePrecept);
                    } else {
                        var def = sync.Read<BuildableDef>();
                        build = new Designator_Build(def);
                        build.placingRot = sync.Read<Rot4>();
                        if (build.PlacingDef.MadeFromStuff) {
                            build.stuffDef = sync.Read<ThingDef>();
                        }
                        build.sourcePrecept = sync.Read<Precept_Building>();
                    }
                }
            },
            {
                (SyncWorker sync, ref Designator_Paint paint) => {
                    if (sync.isWriting) {
                        sync.Write(paint.colorDef);
                    } else {
                        paint.colorDef = sync.Read<ColorDef>();
                    }
                }, true, true // <- Implicit ShouldConstruct
            },
            #endregion

            #region ThingComps
            {
                (ByteWriter data, CompChangeableProjectile comp) => {
                    if (comp == null)
                    {
                        WriteSync<Thing>(data, null);
                        return;
                    }

                    CompEquippable compEquippable = comp.parent.TryGetComp<CompEquippable>();

                    if (compEquippable.AllVerbs.Any())
                    {
                        Building_TurretGun turretGun = compEquippable.AllVerbs.Select(v => v.caster).OfType<Building_TurretGun>().FirstOrDefault();
                        if (turretGun != null)
                        {
                            WriteSync<Thing>(data, turretGun);
                            return;
                        }
                    }

                    throw new SerializationException("Couldn't save CompChangeableProjectile for thing " + comp.parent);
                },
                (ByteReader data) => {
                    if (ReadSync<Thing>(data) is not Building_TurretGun parent)
                        return null;

                    return (parent.gun as ThingWithComps).TryGetComp<CompChangeableProjectile>();
                }
            },
            #endregion

            #region Things
            {
                (ByteWriter data, Thing thing) => {
                    if (thing == null)
                    {
                        data.WriteInt32(-1);
                        return;
                    }

                    MpContext context = data.MpContext();

                    if (thing.Spawned)
                        context.map = thing.Map;

                    data.WriteInt32(thing.thingIDNumber);

                    if (!context.syncingThingParent)
                    {
                        object holder = null;

                        if (thing.Spawned)
                            holder = thing.Map;
                        else if (thing.ParentHolder is ThingComp thingComp)
                            holder = thingComp;
                        else if (ThingOwnerUtility.GetFirstSpawnedParentThing(thing) is Thing parentThing)
                            holder = parentThing;
                        else if (GetAnyParent<WorldObject>(thing) is WorldObject worldObj)
                            holder = worldObj;
                        else if (GetAnyParent<WorldObjectComp>(thing) is WorldObjectComp worldObjComp)
                            holder = worldObjComp;

                        GetImpl(holder, supportedThingHolders, out Type implType, out int index);
                        if (index == -1)
                        {
                            data.WriteByte(byte.MaxValue);
                            Log.Error($"Thing {ThingHolderString(thing)} is inaccessible");
                            return;
                        }

                        data.WriteByte((byte)index);

                        if (implType != typeof(Map))
                        {
                            context.syncingThingParent = true;
                            WriteSyncObject(data, holder, implType);
                            context.syncingThingParent = false;
                            return;
                        }
                    }
                },
                (ByteReader data) => {
                    int thingId = data.ReadInt32();
                    if (thingId == -1)
                        return null;

                    var context = data.MpContext();

                    if (!context.syncingThingParent)
                    {
                        byte implIndex = data.ReadByte();
                        if (implIndex == byte.MaxValue)
                            return null;

                        Type implType = supportedThingHolders[implIndex];

                        if (implType != typeof(Map))
                        {
                            context.syncingThingParent = true;
                            IThingHolder parent = (IThingHolder)ReadSyncObject(data, implType);
                            context.syncingThingParent = false;

                            if (parent != null)
                                return ThingOwnerUtility.GetAllThingsRecursively(parent).Find(t => t.thingIDNumber == thingId);
                            else
                                return null;
                        }
                    }

                    return ThingsById.thingsById.GetValueSafe(thingId);
                }
            },
            {
                (SyncWorker data, ref ThingComp comp) => {
                    if (data.isWriting) {
                        if (comp != null) {
                            ushort index = (ushort)Array.IndexOf(thingCompTypes, comp.GetType());
                            data.Write(index);
                            data.Write(comp.parent);
                            var tempComp = comp;
                            var compIndex = comp.parent.AllComps.Where(x => x.props.compClass == tempComp.props.compClass).FirstIndexOf(x => x == tempComp);
                            data.Write((ushort)compIndex);
                        } else {
                            data.Write(ushort.MaxValue);
                        }
                    } else {
                        ushort index = data.Read<ushort>();
                        if (index == ushort.MaxValue) {
                            return;
                        }
                        ThingWithComps parent = data.Read<ThingWithComps>();
                        if (parent == null) {
                            return;
                        }
                        Type compType = thingCompTypes[index];
                        var compIndex = data.Read<ushort>();
                        if (compIndex <= 0)
                            comp = parent.AllComps.Find(c => c.props.compClass == compType);
                        else
                            comp = parent.AllComps.Where(c => c.props.compClass == compType).ElementAt(compIndex);
                    }
                }, true // implicit
            },
            #endregion

            #region Databases

            { (SyncWorker data, ref OutfitDatabase db) => db = Current.Game.outfitDatabase },
            { (SyncWorker data, ref DrugPolicyDatabase db) => db = Current.Game.drugPolicyDatabase },
            { (SyncWorker data, ref FoodRestrictionDatabase db) => db = Current.Game.foodRestrictionDatabase },

            #endregion

            #region Maps
            {
                (ByteWriter data, Map map) => data.MpContext().map = map,
                (ByteReader data) => (data.MpContext().map)
            },
            {
                (ByteWriter data, AreaManager areas) => data.MpContext().map = areas.map,
                (ByteReader data) => (data.MpContext().map).areaManager
            },
            {
                (ByteWriter data, AutoSlaughterManager autoSlaughter) => data.MpContext().map = autoSlaughter.map,
                (ByteReader data) => (data.MpContext().map).autoSlaughterManager
            },
            {
                (ByteWriter data, MultiplayerMapComp comp) => data.MpContext().map = comp.map,
                (ByteReader data) => (data.MpContext().map).MpComp()
            },
            {
                (SyncWorker data, ref MapComponent comp) => {
                    if (data.isWriting) {
                        if (comp != null) {
                            ushort index = (ushort)Array.IndexOf(mapCompTypes, comp.GetType());
                            data.Write(index);
                            data.Write(comp.map);
                        } else {
                            data.Write(ushort.MaxValue);
                        }
                    } else {
                        ushort index = data.Read<ushort>();
                        if (index == ushort.MaxValue) {
                            return;
                        }
                        Map map = data.Read<Map>();
                        if (map == null) {
                            return;
                        }
                        Type compType = mapCompTypes[index];
                        comp = map.GetComponent(compType);
                    }
                }, true  // implicit
            },
            #endregion

            #region World
            {
                (ByteWriter data, World world) => { },
                (ByteReader data) => Find.World
            },
            {
                (ByteWriter data, WorldObject worldObj) => {
                    data.WriteInt32(worldObj?.ID ?? -1);
                },
                (ByteReader data) => {
                    int objId = data.ReadInt32();
                    if (objId == -1)
                        return null;

                    return Find.World.worldObjects.AllWorldObjects.Find(w => w.ID == objId);
                }
            },
            {
                (SyncWorker data, ref WorldObjectComp comp) => {
                    if (data.isWriting) {
                        if (comp != null) {
                            ushort index = (ushort)Array.IndexOf(worldObjectCompTypes, comp.GetType());
                            data.Write(index);
                            data.Write(comp.parent);
                        } else {
                            data.Write(ushort.MaxValue);
                        }
                    } else {
                        ushort index = data.Read<ushort>();
                        if (index == ushort.MaxValue) {
                            return;
                        }
                        WorldObject parent = data.Read<WorldObject>();
                        if (parent == null) {
                            return;
                        }
                        Type compType = worldObjectCompTypes[index];
                        comp = parent.GetComponent(compType);
                    }
                }, true // implicit
            },
            {
                (SyncWorker data, ref WorldComponent comp) => {
                    if (data.isWriting) {
                        if (comp != null) {
                            ushort index = (ushort)Array.IndexOf(worldCompTypes, comp.GetType());
                            data.Write(index);
                            data.Write(comp.world);
                        } else {
                            data.Write(ushort.MaxValue);
                        }
                    } else {
                        ushort index = data.Read<ushort>();
                        if (index == ushort.MaxValue) {
                            return;
                        }
                        Type compType = worldCompTypes[index];
                        World world = data.Read<World>();
                        if (world == null) {
                            return;
                        }
                        comp = world.GetComponent(compType);
                    }
                }, true // implicit
            },
            #endregion

            #region Game
            {
                (SyncWorker data, ref GameComponent comp) => {
                    if (data.isWriting) {
                        if (comp != null) {
                            ushort index = (ushort)Array.IndexOf(gameCompTypes, comp.GetType());
                            data.Write(index);
                        } else {
                            data.Write(ushort.MaxValue);
                        }
                    } else {
                        ushort index = data.Read<ushort>();
                        if (index == ushort.MaxValue) {
                            return;
                        }
                        Type compType = worldCompTypes[index];
                        comp = Current.Game.GetComponent(compType);
                    }
                }, true // implicit
            },
            #endregion

            #region Areas
            {
                (ByteWriter data, Area area) => {
                    if (area == null) {
                        data.WriteInt32(-1);
                    } else {
                        data.MpContext().map = area.Map;
                        data.WriteInt32(area.ID);
                    }
                },
                (ByteReader data) => {
                    int areaId = data.ReadInt32();
                    if (areaId == -1)
                        return null;
                    return data.MpContext().map.areaManager.AllAreas.Find(a => a.ID == areaId);
                }, true
            },
            {
                (ByteWriter data, Zone zone) => {
                    if (zone == null) {
                        data.WriteInt32(-1);
                    } else {
                        data.MpContext().map = zone.Map;
                        data.WriteInt32(zone.ID);
                    }

                },
                (ByteReader data) => {
                    int zoneId = data.ReadInt32();
                    if (zoneId == -1)
                        return null;
                    return data.MpContext().map.zoneManager.AllZones.Find(zone => zone.ID == zoneId);
                }, true
            },
            #endregion

            #region Globals

            { (SyncWorker data, ref WorldSelector selector) => selector = Find.WorldSelector },
            { (SyncWorker data, ref Storyteller storyteller) => storyteller = Find.Storyteller },

            #endregion

            #region Targets
            {
                (ByteWriter data, LocalTargetInfo info) => {
                    data.WriteBool(info.HasThing);
                    if (info.HasThing)
                        WriteSync(data, info.Thing);
                    else
                        WriteSync(data, info.Cell);
                },
                (ByteReader data) => {
                    bool hasThing = data.ReadBool();
                    if (hasThing)
                        return new LocalTargetInfo(ReadSync<Thing>(data));
                    else
                        return new LocalTargetInfo(ReadSync<IntVec3>(data));
                }
            },
            {
                (ByteWriter data, TargetInfo info) => {
                    data.WriteBool(info.HasThing);
                    if (info.HasThing) {
                        WriteSync(data, info.Thing);
                    }
                    else {
                        WriteSync(data, info.Cell);
                        WriteSync(data, info.Map);
                    }
                },
                (ByteReader data) => {
                    bool hasThing = data.ReadBool();
                    if (hasThing)
                        return new TargetInfo(ReadSync<Thing>(data));
                    else
                        return new TargetInfo(ReadSync<IntVec3>(data), ReadSync<Map>(data), true); // True to prevent errors/warnings if synced map was null
                }
            },
            {
                (ByteWriter data, GlobalTargetInfo info) => {
                    if (info.HasThing) {
                        data.WriteByte(0);
                        WriteSync(data, info.Thing);
                    }
                    else if (info.Cell.IsValid) {
                        data.WriteByte(1);
                        WriteSync(data, info.Cell);
                        WriteSync(data, info.Map);
                    }
                    else if (info.HasWorldObject) {
                        data.WriteByte(2);
                        WriteSync(data, info.WorldObject);
                    }
                    else {
                        data.WriteByte(3);
                        WriteSync(data, info.Tile);
                    }
                },
                (ByteReader data) => {
                    switch (data.ReadByte()) {
                        case 0:
                            return new GlobalTargetInfo(ReadSync<Thing>(data));
                        case 1:
                            return new GlobalTargetInfo(ReadSync<IntVec3>(data), ReadSync<Map>(data), true); // True to prevent errors/warnings if synced map was null
                        case 2:
                            return new GlobalTargetInfo(ReadSync<WorldObject>(data));
                        case 3:
                            return new GlobalTargetInfo(data.ReadInt32());
                        default:
                            return GlobalTargetInfo.Invalid;
                    }
                }
            },
            #endregion

            #region Interfaces
            {
                (ByteWriter data, ISelectable obj) => {
                    if (obj == null)
                    {
                        WriteSync(data, ISelectableImpl.None);
                    }
                    else if (obj is Thing thing)
                    {
                        WriteSync(data, ISelectableImpl.Thing);
                        WriteSync(data, thing);
                    }
                    else if (obj is Zone zone)
                    {
                        WriteSync(data, ISelectableImpl.Zone);
                        WriteSync(data, zone);
                    }
                    else if (obj is WorldObject worldObj)
                    {
                        WriteSync(data, ISelectableImpl.WorldObject);
                        WriteSync(data, worldObj);
                    }
                    else
                    {
                        throw new SerializationException($"Unknown ISelectable type: {obj.GetType()}");
                    }
                },
                (ByteReader data) => {
                    ISelectableImpl impl = ReadSync<ISelectableImpl>(data);

                    return impl switch
                    {
                        ISelectableImpl.None => null,
                        ISelectableImpl.Thing => ReadSync<Thing>(data),
                        ISelectableImpl.Zone => ReadSync<Zone>(data),
                        ISelectableImpl.WorldObject => ReadSync<WorldObject>(data),
                        _ => throw new Exception($"Unknown ISelectable {impl}")
                    };
                }, true
            },
            {
                (ByteWriter data, IStoreSettingsParent obj) => {
                    WriteWithImpl<IStoreSettingsParent>(data, obj, storageParents);
                },
                (ByteReader data) => {
                    return ReadWithImpl<IStoreSettingsParent>(data, storageParents);
                }
            },
            {
                (ByteWriter data, IPlantToGrowSettable obj) => {
                    WriteWithImpl<IPlantToGrowSettable>(data, obj, plantToGrowSettables);
                },
                (ByteReader data) => {
                    return ReadWithImpl<IPlantToGrowSettable>(data, plantToGrowSettables);
                }
            },
            {
                (ByteWriter data, IThingHolder obj) => {
                    WriteWithImpl<IThingHolder>(data, obj, supportedThingHolders);
                },
                (ByteReader data) => {
                    return ReadWithImpl<IThingHolder>(data, supportedThingHolders);
                }
            },
            {
                (ByteWriter data, IStorageGroupMember obj) =>
                {
                    if (obj is Thing thing)
                        WriteSync(data, thing);
                    else
                        throw new SerializationException($"Unknown IStorageGroupMember type: {obj.GetType()}");
                },
                (ByteReader data) => (IStorageGroupMember)ReadSync<Thing>(data)
            },

            #endregion

            #region Storage
            {
                (ByteWriter data, StorageSettings storage) => {
                    WriteSync(data, storage.owner);
                },
                (ByteReader data) => {
                    IStoreSettingsParent parent = ReadSync<IStoreSettingsParent>(data);
                    return parent?.GetStoreSettings();
                }
            },
            {
                (ByteWriter data, StorageGroup group) => WriteSync(data, group.members.First()),
                (ByteReader data) => ReadSync<IStorageGroupMember>(data).Group
            },
            #endregion

            #region Letters
            {
                (ByteWriter data, Letter letter) => {
                    WriteSync(data, letter.ID);
                },
                (ByteReader data) =>
                {
                    // Note that this only returns live (not archived) letters
                    var id = data.ReadInt32();
                    return Find.LetterStack.LettersListForReading.Find(l => l.ID == id);
                }, true
            },
            #endregion

            #region PassingShip
            {
                (ByteWriter data, PassingShip ship) =>
                {
                    WriteSync(data, ship.Map);
                    if (ship.Map != null) data.WriteInt32(ship.loadID);
                },
                (ByteReader data) =>
                {
                    var map = ReadSync<Map>(data);
                    if (map == null) return null;

                    var id = data.ReadInt32();
                    return map.passingShipManager.passingShips.FirstOrDefault(s => s.loadID == id);
                }, true // Implicit
            },
            #endregion
        };

        class Dummy_ITab_Pawn_Visitor : ITab_Pawn_Visitor { }
    }
}
