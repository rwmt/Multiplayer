using System;
using System.Collections.Generic;
using Multiplayer.API;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    public static class RwImplSerialization
    {
        public static Type[] storageSettingsParent; // IStoreSettingsParent
        public static Type[] plantToGrowSettables; // IPlantToGrowSettable
        public static Type[] slotGroupTypes; // ISlotGroup
        public static Type[] slotGroupParents; // ISlotGroupParent
        public static Type[] designatorTypes; // Designator

        internal static Type[] supportedThingHolders = // IThingHolder
        {
            typeof(Map),
            typeof(Thing),
            typeof(ThingComp),
            typeof(WorldObject),
            typeof(WorldObjectComp)
        };

        // ReSharper disable once InconsistentNaming
        internal enum ISelectableImpl : byte
        {
            None, Thing, Zone, WorldObject
        }

        internal enum VerbOwnerType : byte
        {
            None, Pawn, Ability, ThingComp
        }

        public static void Init()
        {
            storageSettingsParent = TypeUtil.AllImplementationsOrdered(typeof(IStoreSettingsParent));
            plantToGrowSettables = TypeUtil.AllImplementationsOrdered(typeof(IPlantToGrowSettable));
            slotGroupTypes = TypeUtil.AllImplementationsOrdered(typeof(ISlotGroup));
            slotGroupParents = TypeUtil.AllImplementationsOrdered(typeof(ISlotGroupParent));
            designatorTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(Designator));
        }

        internal static T ReadWithImpl<T>(ByteReader data, IList<Type> impls) where T : class
        {
            ushort impl = data.ReadUShort();
            if (impl == ushort.MaxValue) return null;
            return (T)SyncSerialization.ReadSyncObject(data, impls[impl]);
        }

        internal static void WriteWithImpl<T>(ByteWriter data, object obj, IList<Type> impls) where T : class
        {
            if (obj == null)
            {
                data.WriteUShort(ushort.MaxValue);
                return;
            }

            GetImpl(obj, impls, out Type implType, out int impl);

            if (implType == null)
                throw new SerializationException($"Unknown {typeof(T)} implementation type {obj.GetType()}");

            data.WriteUShort((ushort)impl);
            SyncSerialization.WriteSyncObject(data, obj, implType);
        }

        internal static void GetImpl(object obj, IList<Type> impls, out Type type, out int index)
        {
            type = null;
            index = -1;

            if (obj == null) return;

            for (int i = 0; i < impls.Count; i++)
            {
                if (impls[i].IsInstanceOfType(obj))
                {
                    type = impls[i];
                    index = i;
                    break;
                }
            }
        }
    }
}
