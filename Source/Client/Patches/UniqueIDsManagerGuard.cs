using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Patches
{
    /// <summary>
    /// Guards <see cref="UniqueIDsManager"/> counters against ID drift caused by UI rendering passes.
    /// <para>
    /// <b>Problem:</b>
    /// Various UI mods (colonist bars, apparel/equipment renderers, inspection panels) and vanilla interface
    /// dialogs (quest previews, trade dialogs) instantiate temporary <see cref="Thing"/>s, pawns, or jobs during UI
    /// rendering (<see cref="UIRoot_Play.UIRootOnGUI"/>). Off-tick instantiation advances <see cref="UniqueIDsManager"/>
    /// counters on the local client only (e.g. wanderer join quest pawn ID divergence).
    /// Furthermore, assigning negative IDs during UI passes breaks mods and vanilla code that rely on
    /// non-negative IDs for indexing graphic variant arrays (<c>thingIDNumber % array.Length</c>).
    /// </para>
    /// <para>
    /// <b>Solution:</b>
    /// Snapshot all <c>next*</c> integer fields in <see cref="UniqueIDsManager"/> at the start of <see cref="UIRoot_Play.UIRootOnGUI"/>,
    /// allow UI elements to receive normal valid positive IDs during rendering, and roll back the counters
    /// via a finalizer when the GUI pass completes.
    /// </para>
    /// <para>
    /// <b>Safety:</b>
    /// The guard is strictly bypassed during map generation, save/load passes, and long events
    /// (<c>LongEventHandler.currentEvent != null</c>, <c>LongEventHandler.AnyEventNowOrWaiting</c>,
    /// <c>Scribe.mode != LoadSaveMode.Inactive</c>) to prevent interfering with actual world/map generation.
    /// Precompiled JIT expression delegates guarantee zero heap allocations per frame.
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class UniqueIDsManagerGuard
    {
        private delegate int IdGetter(UniqueIDsManager manager);
        private delegate void IdSetter(UniqueIDsManager manager, int value);

        private static readonly IdGetter[] getters;
        private static readonly IdSetter[] setters;
        private static readonly int[] snapshot;
        private static bool hasSnapshot;
        private static int guiDepth;

        static UniqueIDsManagerGuard()
        {
            try
            {
                var getterList = new List<IdGetter>();
                var setterList = new List<IdSetter>();

                var paramManager = Expression.Parameter(typeof(UniqueIDsManager), "manager");
                var paramValue = Expression.Parameter(typeof(int), "value");

                foreach (var field in typeof(UniqueIDsManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.FieldType == typeof(int) && field.Name.StartsWith("next", StringComparison.OrdinalIgnoreCase))
                    {
                        var fieldExpr = Expression.Field(paramManager, field);
                        var getter = Expression.Lambda<IdGetter>(fieldExpr, paramManager).Compile();

                        var assignExpr = Expression.Assign(fieldExpr, paramValue);
                        var setter = Expression.Lambda<IdSetter>(assignExpr, paramManager, paramValue).Compile();

                        getterList.Add(getter);
                        setterList.Add(setter);
                    }
                }

                getters = getterList.ToArray();
                setters = setterList.ToArray();
                snapshot = new int[getters.Length];
            }
            catch (Exception ex)
            {
                Log.Warning($"[Multiplayer] UniqueIDsManagerGuard: Failed to compile ID accessor delegates: {ex}");
                getters = new IdGetter[0];
                setters = new IdSetter[0];
                snapshot = new int[0];
            }
        }

        private static bool ShouldGuard()
        {
            return Multiplayer.Client != null
                && Current.ProgramState == ProgramState.Playing
                && !Multiplayer.Ticking
                && !Multiplayer.ExecutingCmds
                && !Multiplayer.reloading
                && LongEventHandler.currentEvent == null
                && !LongEventHandler.AnyEventNowOrWaiting
                && Scribe.mode == LoadSaveMode.Inactive;
        }

        private static void TakeSnapshot(UniqueIDsManager manager)
        {
            for (int i = 0; i < getters.Length; i++)
            {
                snapshot[i] = getters[i](manager);
            }
            hasSnapshot = true;
        }

        private static void RestoreSnapshot(UniqueIDsManager manager)
        {
            if (!hasSnapshot)
                return;

            for (int i = 0; i < setters.Length; i++)
            {
                setters[i](manager, snapshot[i]);
            }
            hasSnapshot = false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            if (!ShouldGuard())
                return;

            var manager = Find.UniqueIDsManager;
            if (manager == null)
                return;

            if (guiDepth++ == 0)
            {
                TakeSnapshot(manager);
            }
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception)
        {
            if (guiDepth <= 0)
                return __exception;

            if (--guiDepth == 0)
            {
                if (ShouldGuard())
                {
                    var manager = Find.UniqueIDsManager;
                    if (manager != null)
                    {
                        RestoreSnapshot(manager);
                    }
                }
                else
                {
                    hasSnapshot = false;
                }
            }

            return __exception;
        }
    }
}
