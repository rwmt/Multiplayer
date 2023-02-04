using System;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
using static Multiplayer.Client.SyncSerialization;

namespace Multiplayer.Client
{
    public static class SyncDictMisc
    {
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

            #region RW Misc
            {
                (ByteWriter data, TaggedString str) => {
                    data.WriteString(str.rawText);
                },
                (ByteReader data) => new TaggedString(data.ReadString())
            },
            {
                (SyncWorker worker, ref ColorInt color) =>
                {
                    worker.Bind(ref color.r);
                    worker.Bind(ref color.g);
                    worker.Bind(ref color.b);
                    worker.Bind(ref color.a);
                }
            },
            #endregion

            #region Unity
            {
                (SyncWorker worker, ref Color color) => {
                    worker.Bind(ref color.r);
                    worker.Bind(ref color.g);
                    worker.Bind(ref color.b);
                    worker.Bind(ref color.a);
                }
            },
            {
                (SyncWorker worker, ref Rect rect) => {
                    if (worker.isWriting)
                    {
                        worker.Write(rect.x);
                        worker.Write(rect.y);
                        worker.Write(rect.width);
                        worker.Write(rect.height);
                    }
                    else
                    {
                        rect.x = worker.Read<float>();
                        rect.y = worker.Read<float>();
                        rect.width = worker.Read<float>();
                        rect.height = worker.Read<float>();
                    }
                }
            },
            #endregion
        };
    }
}
