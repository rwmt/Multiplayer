using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(ThingWithComps))]
    [HarmonyPatch(nameof(ThingWithComps.InitializeComps))]
    public static class ForbiddableCompPatch
    {
        static void Postfix(ThingWithComps __instance)
        {
            if (!__instance.def.HasComp(typeof(CompForbiddable))) return;

            MultiplayerForbiddenComp comp = new MultiplayerForbiddenComp() { parent = __instance };
            __instance.AllComps.Add(comp);
            comp.Initialize(null);
        }
    }

    [HarmonyPatch(typeof(CompForbiddable))]
    [HarmonyPatch(nameof(CompForbiddable.PostSplitOff))]
    public static class ForbiddableSplitPatch
    {
        static void Prefix()
        {
            ForbidSetPatch.ignore = true;
        }

        static void Postfix()
        {
            ForbidSetPatch.ignore = false;
        }
    }

    [HarmonyPatch(typeof(ForbidUtility))]
    [HarmonyPatch(nameof(ForbidUtility.IsForbidden))]
    [HarmonyPatch(new Type[] { typeof(Thing), typeof(Faction) })]
    public static class IsForbiddenPatch
    {
        static void Postfix(Thing t, ref bool __result)
        {
            if (Multiplayer.client == null || Current.ProgramState != ProgramState.Playing) return;

            ThingWithComps thing = t as ThingWithComps;
            if (thing == null) return;

            MultiplayerForbiddenComp comp = thing.GetComp<MultiplayerForbiddenComp>();
            if (comp == null) return;

            if (comp.factionForbidden.TryGetValue(Faction.OfPlayer.loadID, out bool forbidden))
                __result = forbidden;
            else
                __result = false; // default Forbidden
        }
    }

    [HarmonyPatch(typeof(CompForbiddable))]
    [HarmonyPatch(nameof(CompForbiddable.Forbidden), PropertyMethod.Setter)]
    public static class ForbidSetPatch
    {
        public static bool ignore;

        static bool Prefix(CompForbiddable __instance, bool value)
        {
            if (ignore) return true;

            ThingWithComps thing = __instance.parent;
            MultiplayerForbiddenComp comp = thing.GetComp<MultiplayerForbiddenComp>();

            int factionId = Faction.OfPlayer.loadID;
            if (comp.factionForbidden.TryGetValue(factionId, out bool forbidden) && forbidden == value) return false;

            if (Multiplayer.ShouldSync)
            {
                Multiplayer.client.SendCommand(CommandType.FORBID, thing.Map.uniqueID, thing.thingIDNumber, value);
                return false;
            }

            comp.Set(Faction.OfPlayer, value);

            if (thing.Spawned)
            {
                if (value)
                    thing.Map.listerHaulables.Notify_Forbidden(thing);
                else
                    thing.Map.listerHaulables.Notify_Unforbidden(thing);
            }

            return false;
        }
    }

    public class MultiplayerForbiddenCompProps : CompProperties
    {
        public MultiplayerForbiddenCompProps() : base(typeof(MultiplayerForbiddenComp))
        {
        }
    }

    public class MultiplayerForbiddenComp : ThingComp
    {
        public Dictionary<int, bool> factionForbidden = new Dictionary<int, bool>();

        public MultiplayerForbiddenComp()
        {
            props = new MultiplayerForbiddenCompProps();
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned) return null;

            string forbidden = "";
            foreach (KeyValuePair<int, bool> p in factionForbidden)
                forbidden += p.Key + ":" + p.Value + ";";

            return ("Forbidden: " + forbidden).Trim();
        }

        public override void PostSplitOff(Thing piece)
        {
            piece.TryGetComp<MultiplayerForbiddenComp>().factionForbidden = new Dictionary<int, bool>(factionForbidden);
        }

        public void Set(Faction faction, bool value)
        {
            factionForbidden[faction.loadID] = value;

            // Visual
            if (faction == Multiplayer.RealPlayerFaction)
                parent.GetComp<CompForbiddable>().forbiddenInt = value;
        }

        public override void PostExposeData()
        {
            ScribeUtil.Look(ref factionForbidden, "factionForbidden", LookMode.Value);

            // Should only happen for maps transitioning singleplayer -> multiplayer
            if (Scribe.mode == LoadSaveMode.LoadingVars && factionForbidden == null)
                factionForbidden = new Dictionary<int, bool>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && factionForbidden.Count == 0)
                factionForbidden[Find.FactionManager.OfPlayer.loadID] = parent.GetComp<CompForbiddable>().Forbidden;
        }

    }

}
