using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Factions;

public class Page_ChooseIdeo_Multifaction : Page
{
    public override string PageTitle => "ChooseYourIdeoligion".Translate();

    public Page_ChooseIdeoPreset pageChooseIdeo = new();

    public override void DoWindowContents(Rect inRect)
    {
        DrawPageTitle(inRect);
        float totalHeight = 0f;
        Rect mainRect = GetMainRect(inRect);
        TaggedString descText = "ChooseYourIdeoligionDesc".Translate();
        float descHeight = Text.CalcHeight(descText, mainRect.width);
        Rect descRect = mainRect;
        descRect.yMin += totalHeight;
        Widgets.Label(descRect, descText);
        totalHeight += descHeight + 10f;

        pageChooseIdeo.DrawStructureAndStyleSelection(inRect);

        Rect outRect = mainRect;
        outRect.width = 954f;
        outRect.yMin += totalHeight;
        float num3 = (InitialSize.x - 937f) / 2f;

        Widgets.BeginScrollView(
            viewRect: new Rect(0f - num3, 0f, 921f, pageChooseIdeo.totalCategoryListHeight + 100f),
            outRect: outRect,
            scrollPosition: ref pageChooseIdeo.leftScrollPosition);

        totalHeight = 0f;
        pageChooseIdeo.lastCategoryGroupLabel = "";
        foreach (IdeoPresetCategoryDef item in DefDatabase<IdeoPresetCategoryDef>.AllDefsListForReading.Where(c => c != IdeoPresetCategoryDefOf.Classic && c != IdeoPresetCategoryDefOf.Custom && c != IdeoPresetCategoryDefOf.Fluid))
        {
            pageChooseIdeo.DrawCategory(item, ref totalHeight);
        }
        pageChooseIdeo.totalCategoryListHeight = totalHeight;
        Widgets.EndScrollView();

        DoBottomButtons(inRect);
    }

    public override bool CanDoNext()
    {
        if (pageChooseIdeo.selectedIdeo == null)
        {
            Messages.Message("Please select a preset.", MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }

        return base.CanDoNext();
    }   

    public IdeologyData GetIdeologyData()
    {
        return new IdeologyData(pageChooseIdeo.selectedIdeo, pageChooseIdeo.selectedStructure, pageChooseIdeo.selectedStyles);
    }
}

public record IdeologyData(
    IdeoPresetDef SelectedIdeo = null,
    MemeDef SelectedStructure = null,
    List<StyleCategoryDef> SelectedStyles = null
) : ISyncSimple;
