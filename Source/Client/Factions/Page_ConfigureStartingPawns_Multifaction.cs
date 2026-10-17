using System.Linq;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Factions
{
    internal class Page_ConfigureStartingPawns_Multifaction : Page_ConfigureStartingPawns
    {
        Faction _temporaryFactionForPawnCreation;
        Scenario _scenario;

        public Page_ConfigureStartingPawns_Multifaction(Scenario scenario)
        {
            _scenario = scenario;
        }

        // The page's code reads Find.Scenario, which is shared game state used by the simulation. The chosen scenario
        // is only applied while the page's own code runs (in the UI, between ticks).
        private MpScope UseScenario() => FactionSidebar.UseScenario(_scenario);

        public override void PreOpen()
        {
            using (UseScenario())
            {
                base.PreOpen();

                GenerateTemporaryFaction();
                GeneratePawnsForConfigurePage();
            }
        }

        public override void PostOpen()
        {
            using (UseScenario())
                base.PostOpen();
        }

        public override void DoWindowContents(Rect rect)
        {
            using (UseScenario())
                base.DoWindowContents(rect);
        }

        public override void OnAcceptKeyPressed()
        {
            using (UseScenario())
                base.OnAcceptKeyPressed();
        }

        public override void PostClose()
        {
            base.PostClose();

            CleanUpTemporaryFaction();
        }

        public override void DoNext()
        {
            using (UseScenario())
            {
                CleanUpTemporaryFaction();

                base.DoNext();
            }
        }

        private void CleanUpTemporaryFaction()
        {
            if (_temporaryFactionForPawnCreation == null) return;

            Current.Game.InitData.playerFaction = null;
            _temporaryFactionForPawnCreation = null;
        }

        private void GenerateTemporaryFaction()
        {
            // Generating a faction can register a new ideoligion, which would be live game state on this client only.
            // The temporary faction can keep using it unregistered.
            var ideos = Find.IdeoManager.ideos;
            var prevIdeos = ideos.ToList();

            _temporaryFactionForPawnCreation = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(_scenario.playerFaction.factionDef));
            _temporaryFactionForPawnCreation.temporary = true;

            if (!ideos.SequenceEqual(prevIdeos))
            {
                ideos.Clear();
                ideos.AddRange(prevIdeos);
            }

            // The faction isn't added to the FactionManager: it would be live game state on this client only (ticked,
            // iterated by the simulation and saved into join points). Pawn generation only needs it as Faction.OfPlayer,
            // which comes from GameInitData.playerFaction. NewGeneratedFaction also added relations with the temporary
            // faction to every other faction, remove those.
            foreach (var other in Find.FactionManager.AllFactionsListForReading)
                other.relations.RemoveAll(r => r.other == _temporaryFactionForPawnCreation);
        }

        private void GeneratePawnsForConfigurePage()
        {
            var prevProgramState = Current.programStateInt;
            SetEntryProgramStateForCorrectPawnCreation();

            Current.Game.InitData.playerFaction = _temporaryFactionForPawnCreation;

            try
            {
                GeneratingPawns();
            }
            finally
            {       
                Current.programStateInt = prevProgramState;
            }
        }

        private static void SetEntryProgramStateForCorrectPawnCreation()
        {
            // Set ProgramState.Entry to ensure InInterface is false.
            // (#508 Tick-git)  InInterface seems unrelated to PawnCreation to me,
            //                  but it's necessary because it makes Faction.OfPlayer return
            //                  the temporary generated faction — which is required for proper apparel generation.

            Current.programStateInt = ProgramState.Entry;
        }

        private void GeneratingPawns()
        {
            _scenario.PostIdeoChosen();
        }
    }
}
