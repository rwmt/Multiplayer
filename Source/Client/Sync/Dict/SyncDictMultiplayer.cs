using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Persistent;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using static Multiplayer.Client.SyncSerialization;

namespace Multiplayer.Client
{
    public static class SyncDictMultiplayer
    {
        internal static SyncWorkerDictionaryTree syncWorkers = new SyncWorkerDictionaryTree()
        {
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
                    return session?.sessionId == id ? session : null;
                }
            },
            {
                (ByteWriter data, RitualSession session) => {
                    data.MpContext().map = session.map;
                    data.WriteInt32(session.SessionId);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    var session = data.MpContext().map.MpComp().ritualSession;
                    return session?.SessionId == id ? session : null;
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
                    tr.things.AddRange(things.AllNotNull());

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
        };
    }
}
