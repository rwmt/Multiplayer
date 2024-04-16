using RimWorld;
using RimWorld.Utility;
using Verse;

namespace Multiplayer.Client;

public static class ImplSerialization
{
    public static void Init()
    {
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(IStoreSettingsParent));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(IStorageGroupMember));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(IPlantToGrowSettable));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(ISlotGroup));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(ISlotGroupParent));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(Designator));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(ISelectable));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(IVerbOwner));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(IThingHolder));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(IReloadableComp));
        Multiplayer.serialization.RegisterForSyncWithImpl(typeof(Policy));

        // todo for 1.5
        // Multiplayer.serialization.RegisterForSyncWithImpl(typeof(PawnRoleSelectionWidgetBase<ILordJobRole>));
    }
}
