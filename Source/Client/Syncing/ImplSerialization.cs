using System;
using System.Collections.Generic;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    public static class ImplSerialization
    {
        public static Type[] storageParents;
        public static Type[] plantToGrowSettables;

        public static Type[] thingCompTypes;
        public static Type[] abilityCompTypes;
        public static Type[] designatorTypes;
        public static Type[] worldObjectCompTypes;

        public static Type[] gameCompTypes;
        public static Type[] worldCompTypes;
        public static Type[] mapCompTypes;

        internal static Type[] supportedThingHolders =
        {
            typeof(Map),
            typeof(Thing),
            typeof(ThingComp),
            typeof(WorldObject),
            typeof(WorldObjectComp)
        };

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
            storageParents = TypeUtil.AllImplementationsOrdered(typeof(IStoreSettingsParent));
            plantToGrowSettables = TypeUtil.AllImplementationsOrdered(typeof(IPlantToGrowSettable));

            thingCompTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(ThingComp));
            abilityCompTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(AbilityComp));
            designatorTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(Designator));
            worldObjectCompTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(WorldObjectComp));

            gameCompTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(GameComponent));
            worldCompTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(WorldComponent));
            mapCompTypes = TypeUtil.AllSubclassesNonAbstractOrdered(typeof(MapComponent));
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
                if (impls[i].IsAssignableFrom(obj.GetType()))
                {
                    type = impls[i];
                    index = i;
                    break;
                }
            }
        }
    }
}
