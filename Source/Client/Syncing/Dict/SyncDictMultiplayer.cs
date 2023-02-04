using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Persistent;
using Multiplayer.Common;
using RimWorld;
using Verse;
using static Multiplayer.Client.SyncSerialization;
// ReSharper disable RedundantLambdaParameterType

namespace Multiplayer.Client
{
    public static class SyncDictMultiplayer
    {
        internal static SyncWorkerDictionaryTree syncWorkers = new()
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
                (ByteWriter data, ISession session) =>
                {
                    data.MpContext().map ??= session.Map;
                    data.WriteInt32(session.SessionId);
                },
                (ByteReader data) => {
                    int id = data.ReadInt32();
                    return Multiplayer.game.GetSessions(data.MpContext().map).FirstOrDefault(s => s.SessionId == id);
                }, true
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
                    WriteSync(data, reference.session);
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
                    var session = ReadSync<ISessionWithTransferables>(data);

                    int thingId = data.ReadInt32();
                    if (thingId == -1) return null;

                    var transferable = session.GetTransferableByThingId(thingId);

                    return new MpTransferableReference(session, transferable);
                }
            },
            #endregion

            #region Multiplayer Comps
            {
                (ByteWriter _, MultiplayerGameComp _) => {
                },
                (ByteReader _) => Multiplayer.GameComp
            },
            #endregion
        };
    }
}
