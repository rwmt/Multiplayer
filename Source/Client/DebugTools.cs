using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    [MpPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems))]
    static class DebugToolsListing_Patch
    {
        static void Postfix(Dialog_DebugActionsMenu __instance)
        {
            if (Multiplayer.Client == null) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            var menu = __instance;

            menu.DoLabel("Local");

            menu.DebugAction("Save game", SaveGameLocal);
            menu.DebugAction("Print static fields", PrintStaticFields);

            if (!MpVersion.IsDebug) return;

            menu.DoLabel("Multiplayer");

            menu.DebugToolMap("T: Destroy thing", DestroyThing);
            menu.DebugToolMap("T: Mental state", DoMentalState);

            menu.DebugAction("Save game for everyone", SaveGameCmd);
            menu.DebugAction("Advance time", AdvanceTime);

            if (Find.CurrentMap != null)
                DoIncidentDebugAction(menu, Find.CurrentMap);

            if (Find.WorldSelector.SingleSelectedObject is IIncidentTarget target)
                DoIncidentDebugAction(menu, target);

            DoIncidentDebugAction(menu, Find.World);
        }

        [SyncMethod(SyncContext.MapMouseCell)]
        [SyncDebugOnly]
        static void DoMentalState()
        {
            foreach (var pawn in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()).OfType<Pawn>())
                pawn.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("TargetedTantrum"), forceWake: true);
        }

        private static void DoIncidentDebugAction(Dialog_DebugActionsMenu menu, IIncidentTarget target)
        {
            menu.DebugAction($"Incident on {target}", () =>
            {
                var list = new List<DebugMenuOption>();
                foreach (var localDef2 in DefDatabase<IncidentDef>.AllDefs.Where(d => d.TargetAllowed(target)).OrderBy(d => d.defName))
                {
                    IncidentDef localDef = localDef2;
                    string text = localDef.defName;
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(localDef.category, target);
                    if (!localDef.Worker.CanFireNow(parms, false))
                        text += " [NO]";

                    list.Add(new DebugMenuOption(text, DebugMenuOptionMode.Action, () =>
                    {
                        if (localDef.pointsScaleable)
                        {
                            StorytellerComp storytellerComp = Find.Storyteller.storytellerComps.First((StorytellerComp x) => x is StorytellerComp_OnOffCycle || x is StorytellerComp_RandomMain);
                            parms = storytellerComp.GenerateParms(localDef.category, parms.target);
                        }

                        ExecuteIncident(localDef, parms, target as Map);
                    }));
                }

                Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
            });
        }

        [SyncMethod]
        [SyncDebugOnly]
        private static void ExecuteIncident(IncidentDef def, [SyncExpose] IncidentParms parms, [SyncContextMap] Map map)
        {
            def.Worker.TryExecute(parms);
        }

        [SyncMethod(SyncContext.MapMouseCell)]
        [SyncDebugOnly]
        static void DestroyThing()
        {
            foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
                thing.Destroy(DestroyMode.Vanish);
        }

        [SyncMethod]
        [SyncDebugOnly]
        static void SaveGameCmd()
        {
            Map map = Find.Maps[0];
            byte[] mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_0_{Multiplayer.username}.xml", mapData);
        }

        [SyncMethod]
        [SyncDebugOnly]
        static void AdvanceTime()
        {
            int to = 148 * 1000;
            if (Find.TickManager.TicksGame < to)
            {
                Find.TickManager.ticksGameInt = to;
                Find.Maps[0].AsyncTime().mapTicks = to;
            }
        }

        static void SaveGameLocal()
        {
            byte[] data = ScribeUtil.WriteExposable(Current.Game, "game", true);
            File.WriteAllBytes($"game_0_{Multiplayer.username}.xml", data);
        }

        static void PrintStaticFields()
        {
            var builder = new StringBuilder();

            foreach (var type in typeof(Game).Assembly.GetTypes())
                if (!type.IsGenericTypeDefinition && type.Namespace != null && (type.Namespace.StartsWith("RimWorld") || type.Namespace.StartsWith("Verse")) && !type.HasAttribute<DefOf>() && !type.HasAttribute<CompilerGeneratedAttribute>())
                    foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        if (!field.IsLiteral && !field.IsInitOnly && !field.HasAttribute<CompilerGeneratedAttribute>())
                            builder.AppendLine($"{field.FieldType} {type}::{field.Name}: {field.GetValue(null)}");

            Log.Message(builder.ToString());
        }
    }

}
