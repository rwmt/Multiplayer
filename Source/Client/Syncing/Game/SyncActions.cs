using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Multiplayer.Client
{
    static class SyncActions
    {
        static SyncAction<FloatMenuOption, WorldObject, Caravan, object> SyncWorldObjCaravanMenus;
        static SyncAction<FloatMenuOption, WorldObject, IEnumerable<IThingHolder>, CompLaunchable> SyncTransportPodMenus;

        public static void Init()
        {
            SyncWorldObjCaravanMenus = RegisterActions((WorldObject obj, Caravan c) => obj.GetFloatMenuOptions(c), o => ref o.action);
            SyncWorldObjCaravanMenus.PatchAll(nameof(WorldObject.GetFloatMenuOptions));

            SyncTransportPodMenus = RegisterActions((WorldObject obj, IEnumerable<IThingHolder> p, CompLaunchable r) => obj.GetTransportPodsFloatMenuOptions(p, r), o => ref o.action);
            SyncTransportPodMenus.PatchAll(nameof(WorldObject.GetTransportPodsFloatMenuOptions));
        }

        static SyncAction<T, A, B, object> RegisterActions<T, A, B>(Func<A, B, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            return RegisterActions<T, A, B, object>((a, b, c) => func(a, b), actionGetter);
        }

        static SyncAction<T, A, B, C> RegisterActions<T, A, B, C>(Func<A, B, C, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            var sync = new SyncAction<T, A, B, C>(func, actionGetter);
            Sync.handlers.Add(sync);

            return sync;
        }

        public static Dictionary<MethodBase, ISyncAction> syncActions = new();
        public static bool wantOriginal;
        private static bool syncingActions; // Prevents from running on base methods

        public static void SyncAction_Prefix(ref bool __state)
        {
            __state = syncingActions;
            syncingActions = true;
        }

        public static void SyncAction1_Postfix(object __instance, object __0, ref object __result, MethodBase __originalMethod, bool __state)
        {
            SyncAction2_Postfix(__instance, __0, null, ref __result, __originalMethod, __state);
        }

        public static void SyncAction2_Postfix(object __instance, object __0, object __1, ref object __result, MethodBase __originalMethod, bool __state)
        {
            if (!__state)
            {
                syncingActions = false;
                if (Multiplayer.ShouldSync && !wantOriginal && !syncingActions)
                    __result = syncActions[__originalMethod].DoSync(__instance, __0, __1);
            }
        }
    }

}
