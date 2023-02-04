using Multiplayer.API;
using Multiplayer.Common;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncDictFast
    {
        internal static SyncWorkerDictionary syncWorkers = new SyncWorkerDictionary()
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
    }
}
