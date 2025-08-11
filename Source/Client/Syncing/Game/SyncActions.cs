using HarmonyLib;
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
        static Type CaravanActionConfirmationType;

        public static void Init()
        {
            void Error(string error)
            {
                Multiplayer.loadingErrors = true;
                Log.Error(error);
            }

            // TODO: Use MpMethodUtil instead if we decide to make it work with generic types/methods (already in MP Compat, so use it). Or remove this TODO if we decide not to.
            CaravanActionConfirmationType = AccessTools.Inner(typeof(CaravanArrivalActionUtility), "<>c__DisplayClass0_1`1");

            if (CaravanActionConfirmationType == null) Error($"Could not find type: {nameof(CaravanArrivalActionUtility)}.<>c__DisplayClass0_1<T>");

            SyncWorldObjCaravanMenus = RegisterActions((WorldObject obj, Caravan c) => obj.GetFloatMenuOptions(c), ActionGetter, WorldObjectCaravanMenuWrapper);
            SyncWorldObjCaravanMenus.PatchAll(nameof(WorldObject.GetFloatMenuOptions));
        }

        private static ref Action ActionGetter(FloatMenuOption o) => ref o.action;

        private static Action WorldObjectCaravanMenuWrapper(FloatMenuOption instance, WorldObject a, Caravan b, object c, Action original, Action sync)
        {
            if (instance.action.Method.DeclaringType is not { IsGenericType: true })
                return null;

            if (instance.action.Method.DeclaringType.GetGenericTypeDefinition() != CaravanActionConfirmationType)
                return null;

            return () =>
            {
                var field = AccessTools.DeclaredField(original.Target.GetType(), "action");
                if (!Multiplayer.ExecutingCmds)
                {
                    // If not in a synced call then replace the method that will be
                    // called by confirmation dialog with our synced method call.
                    field.SetValue(original.Target, sync);
                    original();
                    return;
                }

                // If we're in a synced call, just call the action itself (after confirmation dialog)
                ((Action)field.GetValue(original.Target))();
            };
        }

        static SyncAction<T, A, B, object> RegisterActions<T, A, B>(Func<A, B, IEnumerable<T>> func, ActionGetter<T> actionGetter, ActionWrapper<T, A, B, object> actionWrapper = null)
        {
            return RegisterActions<T, A, B, object>((a, b, c) => func(a, b), actionGetter, actionWrapper);
        }

        static SyncAction<T, A, B, C> RegisterActions<T, A, B, C>(Func<A, B, C, IEnumerable<T>> func, ActionGetter<T> actionGetter, ActionWrapper<T, A, B, C> actionWrapper = null)
        {
            var sync = new SyncAction<T, A, B, C>(func, actionGetter, actionWrapper);
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
