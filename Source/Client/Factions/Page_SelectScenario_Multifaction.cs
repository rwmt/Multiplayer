using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client.Factions
{
    public class Page_SelectScenario_Multifaction : Page
    {
        public Scenario curScen;

        private Vector2 infoScrollPosition = Vector2.zero;
        private Vector2 scenariosScrollPosition = Vector2.zero;
        private float totalScenarioListHeight;

        public Action<Scenario> onScenChosen;

        private static readonly Dictionary<string, string> warnScenList = new Dictionary<string, string>
        {
           { "The Anomaly", "Only the starting colony will have the monolith. It will be located on the starting colony’s tile, so keep that in mind if you plan to play this scenario." },
        };
        public override string PageTitle
        {
            get
            {
                return "ChooseScenario".Translate();
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            infoScrollPosition = Vector2.zero;
            ScenarioLister.MarkDirty();
            EnsureValidSelection();
        }

        private void EnsureValidSelection()
        {
            if (curScen == null || !ScenarioLister.ScenarioIsListedAnywhere(curScen))
            {
                curScen = ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef).FirstOrDefault<Scenario>();
            }
        }

        public override void DoWindowContents(Rect rect)
        {
            DrawPageTitle(rect);
            Rect mainRect = GetMainRect(rect, 0f, false);
            Widgets.BeginGroup(mainRect);
            Rect rect2 = new Rect(0f, 0f, mainRect.width * 0.35f, mainRect.height).Rounded();
            DoScenarioSelectionList(rect2);
            ScenarioUI.DrawScenarioInfo(new Rect(rect2.xMax + 17f, 0f, mainRect.width - rect2.width - 17f, mainRect.height).Rounded(), curScen, ref infoScrollPosition);
            Widgets.EndGroup();
            DoBottomButtons(rect);
        }

        public void DoBottomButtons(Rect rect)
        {
            float y = rect.y + rect.height - 38f;
            Text.Font = GameFont.Small;
            if ((Widgets.ButtonText(new Rect(rect.x, y, Page.BottomButSize.x, Page.BottomButSize.y), "Cancel".Translate(), true, true, true, null) || KeyBindingDefOf.Cancel.KeyDownEvent) && CanDoBack())
            {
                Close(true);
            }
            Rect rect2 = new Rect(rect.x + rect.width - Page.BottomButSize.x, y, Page.BottomButSize.x, Page.BottomButSize.y);
            if ((Widgets.ButtonText(rect2, "WorldChooseButton".Translate(), true, true, true, null) || KeyBindingDefOf.Accept.KeyDownEvent) && CanDoNext())
            {
                onScenChosen?.Invoke(curScen);
                Close(true);
            }
        }

        private void DoScenarioSelectionList(Rect rect)
        {
            rect.xMax += 2f;
            Rect rect2 = new Rect(0f, 0f, rect.width - 16f - 2f, totalScenarioListHeight + 50f);
            Widgets.BeginScrollView(rect, ref scenariosScrollPosition, rect2, true);
            Rect rect3 = rect2.AtZero();
            rect3.height = 999999f;
            Listing_Standard listing_Standard = new Listing_Standard();
            listing_Standard.ColumnWidth = rect2.width;
            listing_Standard.Begin(rect3);
            Text.Font = GameFont.Small;
            ListScenariosOnListing(listing_Standard, ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef));
            listing_Standard.End();
            totalScenarioListHeight = listing_Standard.CurHeight;
            Widgets.EndScrollView();
        }

        private void DoScenarioListEntry(Rect rect, Scenario scen)
        {
            bool flag = curScen == scen;
            bool warn = warnScenList.ContainsKey(scen.name);
            DrawOptionBackgroundReplacement(rect, flag, warn, warn? warnScenList[scen.name]: null);   
            MouseoverSounds.DoRegion(rect);
            Rect rect2 = rect.ContractedBy(4f);
            Text.Font = GameFont.Small;
            Rect rect3 = rect2;
            rect3.height = Text.CalcHeight(scen.name, rect3.width);
            Widgets.Label(rect3, scen.name);
            Text.Font = GameFont.Tiny;
            Rect rect4 = rect2;
            rect4.yMin = rect3.yMax;
            if (!Text.TinyFontSupported)
            {
                rect4.yMin -= 6f;
                rect4.height += 6f;
            }
            Widgets.Label(rect4, scen.GetSummary());
            if (scen.enabled)
            {
                WidgetRow widgetRow = new WidgetRow(rect.xMax, rect.y, UIDirection.LeftThenDown, 99999f, 4f);
                if (!flag && Widgets.ButtonInvisible(rect, true))
                {
                    curScen = scen;
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                }
            }
        }
        private void ListScenariosOnListing(Listing_Standard listing, IEnumerable<Scenario> scenarios)
        {
            bool flag = false;
            foreach (Scenario scenario in scenarios)
            {
                if (scenario.showInUI)
                {
                    if (flag)
                    {
                        listing.Gap(6f);
                    }
                    Scenario scen = scenario;
                    Rect rect = listing.GetRect(68f, 1f).ContractedBy(4f);
                    DoScenarioListEntry(rect, scen);
                    flag = true;
                }
            }
            if (!flag)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                listing.Label("(" + "NoneLower".Translate() + ")", -1f, null);
                GUI.color = Color.white;
            }
        }

        public override bool CanDoNext()
        {
            if (!base.CanDoNext())
            {
                return false;
            }
            if (curScen == null)
            {
                return false;
            }
            return true;
        }

        public void DrawOptionUnselectedReplacement(Rect rect)
        {
            GUI.color = new ColorInt(144, 21, 28).ToColor;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color =  new ColorInt(187, 89, 95).ToColor;
            Widgets.DrawBox(rect, 1, null);
            GUI.color = Color.white;
        }
        public void DrawOptionBackgroundReplacement(Rect rect, bool selected, bool warning, string reason = null)
        {
            if (selected)
            {
                Widgets.DrawOptionSelected(rect);
            }
            else if (warning)
            {
                DrawOptionUnselectedReplacement(rect);
                if (!string.IsNullOrEmpty(reason))
                {
                    TooltipHandler.TipRegion(rect, reason);
                }
            }
            else
            {
                Widgets.DrawOptionUnselected(rect);
            }
            Widgets.DrawHighlightIfMouseover(rect);
        }
    }
}
