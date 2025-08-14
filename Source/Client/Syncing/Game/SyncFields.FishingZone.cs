using Multiplayer.API;
using RimWorld;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static partial class SyncFields
    {
        public static ISyncField SyncFishingZoneTargetPopulationPct;
        public static SyncField[] SyncFishingZoneValuesBufferedPostApply;
        public static SyncField[] SyncFishingZoneValues;

        public static void InitFishingZone()
        {
            SyncFishingZoneValuesBufferedPostApply = Sync.Fields(
                typeof(Zone_Fishing),
                null,
                nameof(Zone_Fishing.targetCount),
                nameof(Zone_Fishing.repeatCount),
                nameof(Zone_Fishing.unpauseAtCount)
            ).SetBufferChanges().PostApply((fishZone, _) => UpdateFishingTabTextfieldBuffers(fishZone, null));

            SyncFishingZoneValues = Sync.Fields(
                typeof(Zone_Fishing),
                null,
                nameof(Zone_Fishing.pauseWhenSatisfied),
                nameof(Zone_Fishing.repeatMode)
            );

            SyncFishingZoneTargetPopulationPct = Sync.Field(typeof(Zone_Fishing), nameof(Zone_Fishing.targetPopulationPct)).SetBufferChanges();
        }

        [MpPrefix(typeof(ITab_Fishing), nameof(ITab_Fishing.FillTab))]
        static void SyncFishingZoneValuesChanged(ITab_Fishing __instance)
        {
            if (__instance?.SelZone == null) return;

            WatchFishingFields(__instance.SelZone);
            UpdateFishingTabTextfieldBuffers(__instance.SelZone, __instance);
        }

        private static void UpdateFishingTabTextfieldBuffers(object fishingZoneObj, ITab_Fishing fishingTab)
        {
            Zone_Fishing fishingZone = fishingZoneObj as Zone_Fishing;

            if (fishingZone == null)
                return;

            fishingTab ??= fishingZone.ITabs.OfType<ITab_Fishing>().FirstOrDefault();

            if (fishingTab == null)
                return;

            fishingTab.targetCountEditBuffer = fishingZone.targetCount.ToString();
            fishingTab.repeatCountEditBuffer = fishingZone.repeatCount.ToString();
            fishingTab.unpauseAtCountEditBuffer = fishingZone.unpauseAtCount.ToString();
        }

        private static void WatchFishingFields(Zone_Fishing fishingZone)
        {
            SyncFishingZoneValues.Watch(fishingZone);
            SyncFishingZoneValuesBufferedPostApply.Watch(fishingZone);
            SyncFishingZoneTargetPopulationPct.Watch(fishingZone);
        }        
    }
}
