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

    // Custom ideology from a saved .rid file, loaded as a detached object
    // (TryLoadIdeo registers nothing) and serialized into the creation command
    private Ideo customIdeo;

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

        var loadRect = new Rect(inRect.xMax - 260f, inRect.y, 250f, 32f);
        var loadLabel = customIdeo == null
            ? "Load custom ideoligion..."
            : $"Custom: {customIdeo.name} (click to change)";
        if (Widgets.ButtonText(loadRect, loadLabel))
            OpenCustomIdeoMenu();

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

    private void OpenCustomIdeoMenu()
    {
        var ideosDir = GenFilePaths.FolderUnderSaveData("Ideos");
        var files = new System.IO.DirectoryInfo(ideosDir).GetFiles("*.rid");

        if (files.Length == 0)
        {
            Messages.Message(
                "No saved ideoligions found. Create one in singleplayer's ideoligion editor and save it, then load it here.",
                MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        var options = files
            .Select(f => new FloatMenuOption(System.IO.Path.GetFileNameWithoutExtension(f.Name), () =>
            {
                if (GameDataSaveLoader.TryLoadIdeo(f.FullName, out var loaded))
                    customIdeo = loaded;
                else
                    Messages.Message($"Failed to load {f.Name} - see log.",
                        MessageTypeDefOf.RejectInput, historical: false);
            }))
            .Append(new FloatMenuOption("Clear custom ideoligion", () => customIdeo = null))
            .ToList();

        Find.WindowStack.Add(new FloatMenu(options));
    }

    public override bool CanDoNext()
    {
        if (customIdeo == null && pageChooseIdeo.selectedIdeo == null)
        {
            Messages.Message("Please select a preset or load a custom ideoligion.", MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }

        return base.CanDoNext();
    }

    public IdeologyData GetIdeologyData()
    {
        return new IdeologyData(
            pageChooseIdeo.selectedIdeo,
            pageChooseIdeo.selectedStructure,
            pageChooseIdeo.selectedStyles,
            customIdeo != null ? ScribeUtil.WriteExposable(customIdeo) : null);
    }
}

public record IdeologyData(
    IdeoPresetDef SelectedIdeo = null,
    MemeDef SelectedStructure = null,
    List<StyleCategoryDef> SelectedStyles = null,
    byte[] CustomIdeoData = null
) : ISyncSimple;
