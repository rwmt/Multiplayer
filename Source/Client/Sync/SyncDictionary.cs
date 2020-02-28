using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Multiplayer.Client
{
    public static partial class Sync
    {
        // These syncWorkers need very fast access, keep it small.
        private static SyncWorkerDictionary syncWorkersEarly = new SyncWorkerDictionary()
        {
            // missing decimal and char, good?
            #region Built-in

            { (SyncWorker sync, ref bool b   ) =>  sync.Bind(ref b) },
            { (SyncWorker sync, ref byte b   ) =>  sync.Bind(ref b) },
            { (SyncWorker sync, ref sbyte b  ) =>  sync.Bind(ref b) },
            { (SyncWorker sync, ref double d ) =>  sync.Bind(ref d) },
            { (SyncWorker sync, ref float f  ) =>  sync.Bind(ref f) },
            { (SyncWorker sync, ref int i    ) =>  sync.Bind(ref i) },
            { (SyncWorker sync, ref uint i   ) =>  sync.Bind(ref i) },
            { (SyncWorker sync, ref long l   ) =>  sync.Bind(ref l) },
            { (SyncWorker sync, ref ulong l  ) =>  sync.Bind(ref l) },
            { (SyncWorker sync, ref short s  ) =>  sync.Bind(ref s) },
            { (SyncWorker sync, ref ushort s ) =>  sync.Bind(ref s) },
            { (SyncWorker sync, ref string t ) =>  sync.Bind(ref t) },

            #endregion

            #region Structs
            {
                (ByteWriter data, Rot4 rot) => data.WriteByte(rot.AsByte),
                (ByteReader data) => new Rot4(data.ReadByte())
            },
            {
                (ByteWriter data, IntVec3 vec) => {
                    if (vec.y < 0) {
                        data.WriteShort(-1);
                    }
                    else {
                        data.WriteShort((short)vec.y);
                        data.WriteShort((short)vec.x);
                        data.WriteShort((short)vec.z);
                    }
                },
                (ByteReader data) => {
                    short y = data.ReadShort();
                    if (y < 0)
                      return IntVec3.Invalid;

                    short x = data.ReadShort();
                    short z = data.ReadShort();

                    return new IntVec3(x, y, z);
                }
            },
            {
                (SyncWorker sync, ref Vector2 vec)  => {
                    sync.Bind(ref vec.x);
                    sync.Bind(ref vec.y);
                }
            },
            {
                (SyncWorker sync, ref Vector3 vec)  => {
                    sync.Bind(ref vec.x);
                    sync.Bind(ref vec.y);
                    sync.Bind(ref vec.z);
                }
            },
            #endregion

            #region Templates
            /*
            { (SyncWorker sync, ref object obj)  => { } },
            {
                (ByteWriter data, object obj) => {
            
                },
                (ByteReader data) => {
                    return null;
                }
            },
            */
            #endregion
        };

        // Here you can stuff anything, you break it down and you build it on the other side
        // Append a true to the entry to make it work implicitly to all its children Types
        internal static SyncWorkerDictionaryTree syncWorkers = new SyncWorkerDictionaryTree()
        {
            #region Ignored

            { (SyncWorker sync, ref Event s)  => { } },

            #endregion

            #region System
            {
                (ByteWriter data, Type type) => data.WriteString(type.FullName),
                (ByteReader data) => AccessTools.TypeByName(data.ReadString())
            },
            #endregion

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
                    Thing billGiver = (obj as BillStack)?.billGiver as Thing;
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

            #region Ranges
            {
                (ByteWriter data, FloatRange range) => {
                    data.WriteFloat(range.min);
                    data.WriteFloat(range.max);
                },
                (ByteReader data) => new FloatRange(data.ReadFloat(), data.ReadFloat())
            },
            {
                (ByteWriter data, IntRange range) => {
                    data.WriteInt32(range.min);
                    data.WriteInt32(range.max);
                },
                (ByteReader data) => new IntRange(data.ReadInt32(), data.ReadInt32())
            },
            {
                (ByteWriter data, QualityRange range) => {
                    WriteSync(data, range.min);
                    WriteSync(data, range.max);
                },
                (ByteReader data) => new QualityRange(ReadSync<QualityCategory>(data), ReadSync<QualityCategory>(data))
            },
            #endregion

            #region Names
            {
                (ByteWriter data, NameSingle name) => {
                    data.WriteString(name.nameInt);
                    data.WriteBool(name.numerical);
                },
                (ByteReader data) => new NameSingle(data.ReadString(), data.ReadBool())
            },
            {
                (ByteWriter data, NameTriple name) => {
                    data.WriteString(name.firstInt);
                    data.WriteString(name.nickInt);
                    data.WriteString(name.lastInt);
                },
                (ByteReader data) => new NameTriple(data.ReadString(), data.ReadString(), data.ReadString())
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
                (ByteWriter data, ITab_ContentsTransporter tab) => { },
                (ByteReader data) => new ITab_ContentsTransporter()
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

                    var command = MpUtil.UninitializedObject<Command_SetPlantToGrow>();
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
            #endregion

            #region Designators
            {
                // This is here to handle every other Designator that isn't implemented,
                // by default they only need the type and that's being handled outside.
                // Returning null is enough.
                (ByteWriter data, Designator des) => {
                    // These are handled in DesignatorPatches.WriteData
                },
                (ByteReader data) => {
                    // These are handled in MapAsyncTimeComp.HandleDesignator
                    return null;
                }, true // <- isImplicit: true
            },
            {
                // The only special case in vanilla that has arguments, mods can add their own as explicit types.
                (ByteWriter data, Designator_Build build) => {
                    WriteSync(data, build.entDef);
                },
                (ByteReader data) => {
                    BuildableDef def = ReadSync<BuildableDef>(data);
                    return new Designator_Build(def);
                }
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
                    Building_TurretGun parent = ReadSync<Thing>(data) as Building_TurretGun;

                    if (parent == null)
                        return null;

                    return (parent.gun as ThingWithComps).TryGetComp<CompChangeableProjectile>();
                }
            },
            {
                (ByteWriter data, ThingComp comp) => {
                    if (comp != null) {
                        data.WriteUShort((ushort)Array.IndexOf(thingCompTypes, comp.GetType()));
                        WriteSync(data, comp.parent);
                    } else {
                        data.WriteUShort(ushort.MaxValue);
                    }
                },
                (ByteReader data) => {
                    ushort compTypeId = data.ReadUShort();
                    if (compTypeId == ushort.MaxValue)
                        return null;

                    ThingWithComps parent = ReadSync<ThingWithComps>(data);
                    if (parent == null)
                        return null;

                    Type compType = thingCompTypes[compTypeId];
                    return parent.AllComps.Find(comp => comp.props.compClass == compType);
                }, true
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
                (ByteWriter data, MultiplayerMapComp comp) => data.MpContext().map = comp.map,
                (ByteReader data) => (data.MpContext().map).MpComp()
            },
            #endregion

            #region World
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
                (ByteWriter data, WorldObjectComp comp) => {
                    if (comp != null) {
                        ushort index = (ushort)Array.IndexOf(worldObjectCompTypes, comp.GetType());
                        data.WriteUShort(index);
                        WriteSync(data, comp.parent);
                    } else {
                        data.WriteInt32(ushort.MaxValue);
                    }
                },
                (ByteReader data) => {
                    ushort compTypeId = data.ReadUShort();
                    if (compTypeId == ushort.MaxValue)
                        return null;

                    WorldObject parent = ReadSync<WorldObject>(data);
                    if (parent == null)
                        return null;

                    Type compType = worldObjectCompTypes[compTypeId];
                    return parent.AllComps.Find(comp => comp.props.compClass == compType);
                  }
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

            #region Multiplayer Sessions
            {
                (ByteWriter data, PersistentDialog session) => {
                    data.MpContext().map = session.map;
                    data.WriteInt32(session.id);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    return data.MpContext().map.MpComp().mapDialogs.FirstOrDefault(s => s.id == id);
                }
            },
            {
                (ByteWriter data, MpTradeSession session) => data.WriteInt32(session.sessionId),
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    return Multiplayer.WorldComp.trading.FirstOrDefault(s => s.sessionId == id);
                }
            },
            {
                (ByteWriter data, CaravanFormingSession session) => {
                    data.MpContext().map = session.map;
                    data.WriteInt32(session.sessionId);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    var session = data.MpContext().map.MpComp().caravanForming;
                    return session?.sessionId == id ? session : null;
                }
            },
            {
                (ByteWriter data, TransporterLoading session) => {
                    data.MpContext().map = session.map;
                    data.WriteInt32(session.sessionId);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    var session = data.MpContext().map.MpComp().transporterLoading;
                    return session?.sessionId == id? session : null;
                }
            },
            #endregion

            #region Multiplayer Transferables
            {
                // todo find a better way
                (ByteWriter data, TransferableImmutable tr) => {
                    WriteSync(data, tr.things);
                },
                (ByteReader data) => {
                    List<Thing> things = ReadSync<List<Thing>>(data);

                    TransferableImmutable tr = new TransferableImmutable();
                    tr.things.AddRange(things.NotNull());

                    return tr;
                }
            },
            {
                (ByteWriter data, MpTransferableReference reference) => {
                    data.WriteInt32(reference.session.SessionId);

                    Transferable tr = reference.transferable;

                    if (tr == null) {
                        data.WriteInt32(-1);
                        return;
                    }

                    Thing thing;
                    if (tr is Tradeable trad)
                        thing = trad.FirstThingTrader ?? trad.FirstThingColony;
                    else if (tr is TransferableOneWay oneWay)
                        thing = oneWay.AnyThing;
                    else
                        throw new Exception($"Syncing unsupported transferable type {reference?.GetType()}");

                    MpContext context = data.MpContext();

                    if (thing.Spawned)
                        context.map = thing.Map;

                    data.WriteInt32(thing.thingIDNumber);
                },
                (ByteReader data) => {
                    var map = data.MpContext().map;

                    int sessionId = data.ReadInt32();
                    var session = GetSessions(map).FirstOrDefault(s => s.SessionId == sessionId);

                    int thingId = data.ReadInt32();
                    if (thingId == -1) return null;

                    var transferable = session.GetTransferableByThingId(thingId);

                    return new MpTransferableReference(session, transferable);
                }
            },
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

                    if (impl == ISelectableImpl.None)
                        return null;
                    if (impl == ISelectableImpl.Thing)
                        return ReadSync<Thing>(data);
                    if (impl == ISelectableImpl.Zone)
                        return ReadSync<Zone>(data);
                    if (impl == ISelectableImpl.WorldObject)
                        return ReadSync<WorldObject>(data);

                    throw new Exception($"Unknown ISelectable");
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

            #endregion

            #region Storage

            {
                (ByteWriter data, StorageSettings storage) => {
                    WriteSync(data, storage.owner);
                },
                (ByteReader data) => {
                    IStoreSettingsParent parent = ReadSync<IStoreSettingsParent>(data);

                    if (parent == null)
                        return null;

                    return parent.GetStoreSettings();
                }
            },

            #endregion
        };
    }
}
