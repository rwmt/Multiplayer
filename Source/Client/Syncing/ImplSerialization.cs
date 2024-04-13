using RimWorld;
using RimWorld.Utility;
using Verse;

namespace Multiplayer.Client;

public static class ImplSerialization
{
    public static void Init()
    {
        Multiplayer.serialization.AddExplicitImplType(typeof(IStoreSettingsParent));
        Multiplayer.serialization.AddExplicitImplType(typeof(IStorageGroupMember));
        Multiplayer.serialization.AddExplicitImplType(typeof(IPlantToGrowSettable));
        Multiplayer.serialization.AddExplicitImplType(typeof(ISlotGroup));
        Multiplayer.serialization.AddExplicitImplType(typeof(ISlotGroupParent));
        Multiplayer.serialization.AddExplicitImplType(typeof(Designator));
        Multiplayer.serialization.AddExplicitImplType(typeof(ISelectable));
        Multiplayer.serialization.AddExplicitImplType(typeof(IVerbOwner));
        Multiplayer.serialization.AddExplicitImplType(typeof(IThingHolder));
        Multiplayer.serialization.AddExplicitImplType(typeof(IReloadableComp));
        Multiplayer.serialization.AddExplicitImplType(typeof(Policy));
        Multiplayer.serialization.AddExplicitImplType(typeof(PawnRoleSelectionWidgetBase<ILordJobRole>));
    }
}
