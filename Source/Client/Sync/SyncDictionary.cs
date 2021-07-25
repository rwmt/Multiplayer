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
            {
                // We assume that the currently open tab holds the table, as it seems to only be used together with MainTabWindow_PawnTable and its subclasses
                (ByteWriter data, PawnTable table) => WriteSync(data, Find.MainTabsRoot.OpenTab),
                (ByteReader data) =>
                {
                    var tab = (MainTabWindow_PawnTable)ReadSync<MainButtonDef>(data).TabWindow;
                    // By callinng MainButonDef.TabWindow we could end up initializing the tab window, so we should make sure the table is created as well if needed
                    if (tab.table == null) tab.table = tab.CreateTable();
                    return tab.table;
                }, true
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

            #region Abilities
            {
                (ByteWriter data, Ability ability) => {
                    WriteSync(data, ability.pawn);
                    WriteSync(data, ability.UniqueVerbOwnerID());
                },
                (ByteReader data) => {
                    var pawn = ReadSync<Pawn>(data);
                    var uniqueVerbOwnerID = data.ReadString();

                    var ability = pawn.abilities.abilities.Find(ab => ab.UniqueVerbOwnerID() == uniqueVerbOwnerID);
                    // effectComps is required for some abilities but comps can be null too
                    ability.effectComps = ability.CompsOfType<CompAbilityEffect>()?.ToList();

                    return ability;
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
                        else if (verb.DirectOwner is CompEquippable compEquipable) {
                            sync.Write(VerbOwnerType.CompEquippable);
                            sync.Write(compEquipable);
                        }
                        else if (verb.DirectOwner is CompReloadable compReloadable) {
                            sync.Write(VerbOwnerType.CompReloadable);
                            sync.Write(compReloadable);
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
                        else if (ownerType == VerbOwnerType.CompEquippable) {
                            verbOwner = sync.Read<CompEquippable>();
                        }
                        else if (ownerType == VerbOwnerType.CompReloadable) {
                            verbOwner = sync.Read<CompReloadable>();
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
                (ByteWriter data, Faction quest) => {
                    data.WriteInt32(quest.loadID);
                },
                (ByteReader data) => {
                    int loadID = data.ReadInt32();
                    return Find.FactionManager.AllFactions.FirstOrDefault(possibleFaction => possibleFaction.loadID == loadID);
                },
                true
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
                // We can't take part of the implicit tree because Designator_Build has an argument
                // So we need to implement placingRot here too, until we separate instancing from decorating.
                (SyncWorker sync, ref Designator_Build build) => {
                    if (sync.isWriting) {
                        sync.Write(build.PlacingDef);
                        sync.Write(build.placingRot);
                        if (build.PlacingDef.MadeFromStuff) {
                            sync.Write(build.stuffDef);
                        }
                    } else {
                        var def = sync.Read<BuildableDef>();
                        build = new Designator_Build(def);
                        build.placingRot = sync.Read<Rot4>();
                        if (build.PlacingDef.MadeFromStuff) {
                            build.stuffDef = sync.Read<ThingDef>();
                        }
                    }
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
                        comp = parent.AllComps.Find(c => c.props.compClass == compType);
                    }
                }, true // implicit
            },
            #endregion

            #region Royalty Permits
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

            #region Color
            {
                (SyncWorker worker, ref Color color) => {
                    worker.Bind(ref color.r);
                    worker.Bind(ref color.g);
                    worker.Bind(ref color.b);
                    worker.Bind(ref color.a);
                }
            },
            #endregion

            #region Transport pod arrival actions
            {
                (ByteWriter data, TransportPodsArrivalAction_AttackSettlement arrivalAction) =>
                {
                    WriteSync(data, arrivalAction.settlement);
                    WriteSync(data, arrivalAction.arrivalMode);
                },
                (ByteReader data) =>
                {
                    return new TransportPodsArrivalAction_AttackSettlement(ReadSync<Settlement>(data), ReadSync<PawnsArrivalModeDef>(data));
                }
            },
            {
                (ByteWriter data, TransportPodsArrivalAction_FormCaravan arrivalAction) => WriteSync(data, arrivalAction.arrivalMessageKey),
                (ByteReader data) => new TransportPodsArrivalAction_FormCaravan(ReadSync<string>(data))
            },
            {
                (ByteWriter data, TransportPodsArrivalAction_GiveGift arrivalAction) => WriteSync(data, arrivalAction.settlement),
                (ByteReader data) => new TransportPodsArrivalAction_GiveGift(ReadSync<Settlement>(data))
            },
            {
                (ByteWriter data, TransportPodsArrivalAction_GiveToCaravan arrivalAction) => WriteSync(data, arrivalAction.caravan),
                (ByteReader data) => new TransportPodsArrivalAction_GiveToCaravan(ReadSync<Caravan>(data))
            },
            {
                (ByteWriter data, TransportPodsArrivalAction_LandInSpecificCell arrivalAction) =>
                {
                    WriteSync(data, arrivalAction.mapParent);
                    WriteSync(data, arrivalAction.cell);
                    WriteSync(data, arrivalAction.landInShuttle);
                },
                (ByteReader data) => new TransportPodsArrivalAction_LandInSpecificCell(ReadSync<MapParent>(data), ReadSync<IntVec3>(data), ReadSync<bool>(data))
            },
            {
                (ByteWriter data, TransportPodsArrivalAction_VisitSettlement arrivalAction) => WriteSync(data, arrivalAction.settlement),
                (ByteReader data) => new TransportPodsArrivalAction_VisitSettlement(ReadSync<Settlement>(data))
            },
            {
                (ByteWriter data, TransportPodsArrivalAction_VisitSite arrivalAction) =>
                {
                    WriteSync(data, arrivalAction.site);
                    WriteSync(data, arrivalAction.arrivalMode);
                },
                (ByteReader data) => new TransportPodsArrivalAction_VisitSite(ReadSync<Site>(data), ReadSync<PawnsArrivalModeDef>(data))
            },
            // todo 1.3: was removed?
            /*{
                (ByteWriter data, TransportPodsArrivalAction_Shuttle arrivalAction) =>
                {
                    WriteSync(data, arrivalAction.mapParent);
                    WriteSync(data, arrivalAction.missionShuttleHome);
                    WriteSync(data, arrivalAction.missionShuttleTarget);
                    WriteSync(data, arrivalAction.sendAwayIfQuestFinished);
                    WriteSync(data, arrivalAction.questTags);
                },
                (ByteReader data) =>
                {
                    var arrivalAction =  new TransportPodsArrivalAction_Shuttle(ReadSync<MapParent>(data))
                    {
                        missionShuttleHome = ReadSync<WorldObject>(data),
                        missionShuttleTarget = ReadSync<WorldObject>(data),
                        sendAwayIfQuestFinished = ReadSync<Quest>(data),
                        questTags = ReadSync<List<string>>(data)
                    };

                    return arrivalAction;
                }
            },*/
            #endregion
        };
    }
}
