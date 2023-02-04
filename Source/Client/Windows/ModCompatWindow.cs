using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{

    public class ModCompatWindow : Window
    {
        public override Vector2 InitialSize => popup ? new(600, 450) : new(900, 600);

        private List<ModMetaData> modsActive = new();
        private List<ModMetaData> modsInstalled = new();
        private List<ModMetaData> modsOutdated = new();
        private Window parent;
        private bool popup;
        private bool forceNameSort;
        private Func<string, string> nameProcessor;

        public ModCompatWindow(Window parent, bool popup, bool forceNameSort, Func<string, string> nameProcessor)
        {
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;

            this.popup = popup;
            this.parent = parent;
            this.forceNameSort = forceNameSort;
            this.nameProcessor = nameProcessor ?? (str => str);

            if (popup)
            {
                resizeable = true;
                draggable = true;
                layer = WindowLayer.SubSuper;
                resizer = new WindowResizer() { minWindowSize = InitialSize };
            }
            else
            {
                absorbInputAroundWindow = true;
            }
        }

        public override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();

            if (popup)
                windowRect.x += 150; // Shift right to not obscure the mod list
        }

        private static Vector2 scrollbar;
        private float height;
        private string nameFieldStr;
        private int? modsHash;
        private (SortType t, SortDirection d) sort;
        private bool nameFieldChanged;

        private Dictionary<string, string> modNameCache = new();
        private Dictionary<string, string> notesCache = new();

        const float CheckboxesHeight = 24f;
        const float RowHeight = 23f;
        const float NameWidth = 250f;
        const float ActiveWidth = 64f;
        const float ScoreWidth = 60f;
        const float Spacing = 10f;
        const float ScrollbarWidth = 20f;
        const float ListInset = 15f;

        public override void DoWindowContents(Rect inRect)
        {
            if (modsHash != ModLister.InstalledModsListHash(true))
            {
                RecacheMods();
                modsHash = ModLister.InstalledModsListHash(true);
            }

            if (resizer is { isResizing: true })
            {
                modNameCache.Clear();
                notesCache.Clear();
            }

            MpUI.TryUnfocusCurrentNamedControl(this);

            // Title
            const int titleHeight = 30;
            using (MpStyle.Set(GameFont.Medium))
                Widgets.Label(inRect.Height(titleHeight), "MpModCompatTitle".Translate());
            inRect.yMin += titleHeight;

            // Enable crowd-sourced info
            MpUI.CheckboxLabeledWithTip(
                inRect.Width(300).Height(CheckboxesHeight),
                "MpShowModCompat".Translate(),
                "MpShowModCompatDesc".Translate(),
                ref Multiplayer.settings.showModCompatibility
            );

            if (ModCompatibilityManager.fetchSuccess is not true)
                MpUI.Label(
                    inRect.Width(300).Height(CheckboxesHeight).Right(310),
                    ModCompatibilityManager.fetchSuccess is false ? "MpModCompatLoadingFailed".Translate() : "MpModCompatLoading".Translate() + MpUI.FixedEllipsis()
                );

            inRect.yMin += CheckboxesHeight;

            // Hide translation mods
            MpUI.CheckboxLabeledWithTip(
                inRect.Width(300).Height(CheckboxesHeight),
                "MpHideTranslationMods".Translate(),
                "MpHideTranslationModsDesc".Translate(),
                ref Multiplayer.settings.hideTranslationMods
            );
            inRect.yMin += CheckboxesHeight;

            // Mod search field
            var nameField = inRect.Width(300).Height(CheckboxesHeight);
            var prevNameField = nameFieldStr;
            GUI.SetNextControlName("mod_search");
            nameFieldStr = Widgets.TextField(nameField, nameFieldStr);
            nameFieldChanged = nameFieldStr != prevNameField;
            inRect.yMin += CheckboxesHeight + 10f;

            GUI.BeginGroup(inRect);
            using (MpStyle.Set(TextAnchor.MiddleLeft))
            {
                DoHeaders(inRect.width);

                var viewRect = new Rect(0, 0, inRect.width - ScrollbarWidth, height);
                var outRect = new Rect(0, 0, inRect.width, inRect.height);
                outRect.yMin += RowHeight;

                Widgets.BeginScrollView(outRect, ref scrollbar, viewRect);
                {
                    float y = 0;
                    float? newScrollY = null;

                    bool ListHeader(string header, ref bool expanded)
                    {
                        GUI.DrawTexture(new Rect(0, y, 18, 18), expanded ? TexButton.Collapse : TexButton.Reveal);
                        Widgets.Label(new Rect(20, y, viewRect.width, RowHeight), header);
                        if (Widgets.ButtonInvisible(new Rect(0, y, viewRect.width, RowHeight)))
                            expanded = !expanded;
                        y += RowHeight;
                        return expanded;
                    }

                    if (ListHeader("MpModCompatActiveMods".Translate(), ref activeExpanded))
                        DoModList(modsActive, outRect.height, viewRect.width, ref y, ref newScrollY);

                    if (ListHeader("MpModCompatInstalledMods".Translate(), ref installedExpanded))
                        DoModList(modsInstalled, outRect.height, viewRect.width, ref y, ref newScrollY);

                    if (ListHeader("MpModCompatOutdatedMods".Translate(), ref outdatedExpanded))
                        DoModList(modsOutdated, outRect.height, viewRect.width, ref y, ref newScrollY);

                    if (newScrollY != null)
                        scrollbar.y = newScrollY.Value;

                    if (Event.current.type == EventType.Layout)
                        height = y;
                }
                Widgets.EndScrollView();
            }
            GUI.EndGroup();
        }

        private static bool activeExpanded = true;
        private static bool installedExpanded = true;
        private static bool outdatedExpanded = true;

        private void DoModList(List<ModMetaData> list, float outHeight, float width, ref float y, ref float? newScrollY)
        {
            int i = 0;

            foreach (var mod in list)
            {
                if (Multiplayer.settings.hideTranslationMods && MultiplayerData.IsTranslationMod(mod))
                    continue;

                var modName = nameProcessor(mod.Name);
                var nameContainsSearch = modName.ToLowerInvariant().Contains(nameFieldStr.ToLowerInvariant());
                var isShown = y >= scrollbar.y - RowHeight && y < scrollbar.y + outHeight;

                if (nameContainsSearch && nameFieldChanged)
                {
                    // Scroll to the first one...
                    newScrollY ??= y;

                    // but if one is already on screen do nothing
                    if (isShown)
                    {
                        nameFieldChanged = false;
                        newScrollY = null;
                    }
                }

                if (isShown)
                    DoModRow(mod, i % 2 == 1, new Rect(ListInset, y, width - ListInset, RowHeight));

                y += RowHeight;
                i++;
            }
        }

        private void DoHeaders(float width)
        {
            using var _ = MpStyle.Set(Color.grey);

            var headerRow = new Rect(0, 0, width, RowHeight);

            // Name header
            var nameHeader = headerRow.Width(NameWidth);
            Widgets.Label(nameHeader, $"{"MpModCompatHeaderModName".Translate()}{(sort.t == SortType.Name && !forceNameSort ? SortChar(sort.d) : "")}");
            Widgets.DrawHighlightIfMouseover(nameHeader);

            if (!forceNameSort && Widgets.ButtonInvisible(nameHeader))
            {
                sort = (SortType.Name, sort.t == SortType.Name ? sort.d.Cycle() : SortDirection.Ascending);
                RecacheMods();
            }

            headerRow.x += NameWidth;

            // Score header
            using (MpStyle.Set(TextAnchor.MiddleCenter))
            {
                var statusHeader = headerRow.Width(ScoreWidth);
                Widgets.Label(statusHeader, $"{"MpModCompatHeaderStatus".Translate()}{(sort.t == SortType.Score ? SortChar(sort.d) : "")}");
                Widgets.DrawHighlightIfMouseover(statusHeader);

                if (Widgets.ButtonInvisible(statusHeader))
                {
                    sort = (SortType.Score, sort.t == SortType.Score ? sort.d.Cycle() : SortDirection.Ascending);
                    RecacheMods();
                }

                headerRow.x += ScoreWidth;
            }

            // Notes header
            var notesHeader = headerRow.MaxX(headerRow.width - ScrollbarWidth);
            Widgets.Label(notesHeader, $"MpModCompatHeaderNotes".Translate());
            Widgets.DrawHighlightIfMouseover(notesHeader);
        }

        private void DoModRow(ModMetaData mod, bool alt, Rect row)
        {
            var modName = nameProcessor(mod.Name);
            var nameContainsSearch = modName.ToLowerInvariant().Contains(nameFieldStr.ToLowerInvariant());
            var info = TryGetCompatInfo(mod);

            if (Mouse.IsOver(row))
                Widgets.DrawHighlight(row);
            else if (alt)
                Widgets.DrawAltRect(row);

            // Name
            {
                using (MpStyle.Set(nameContainsSearch ? Color.white : Color.grey))
                    MpUI.LabelTruncatedWithTip(row.Width(NameWidth - Spacing - ListInset), modName, modNameCache);
                row.xMin += NameWidth - ListInset;
            }

            // Score
            {
                bool xml = MultiplayerData.IsXmlMod(mod);

                var scoreColor = xml ? ColorLibrary.Green : info?.status switch
                {
                    1 => ColorLibrary.Red,
                    2 => ColorLibrary.Orange,
                    3 => ColorLibrary.Yellow,
                    4 => ColorLibrary.Green,
                    _ => ColorLibrary.Grey
                };

                var scoreDescKey = xml ? "MpModCompatXmlOnlyDesc" : info?.status switch
                {
                    1 => "MpModCompatScore1",
                    2 => "MpModCompatScore2",
                    3 => "MpModCompatScore3",
                    4 => "MpModCompatScore4",
                    _ => "MpModCompatScoreUnk"
                };

                var scoreText = xml
                    ? "XML"
                    : (info?.status ?? 0).ToString();

                using (MpStyle.Set(scoreColor))
                using (MpStyle.Set(TextAnchor.MiddleCenter))
                {
                    Widgets.Label(row.Width(ScoreWidth), scoreText);
                    TooltipHandler.TipRegion(row.Width(ScoreWidth), scoreDescKey.Translate());
                    row.xMin += ScoreWidth;
                }
            }

            // Notes
            using (MpStyle.Set(new Color(0.7f, 0.7f, 0.7f)))
            {
                MpUI.LabelTruncatedWithTip(row, info?.notes ?? "", notesCache);
            }
        }

        public override void WindowUpdate()
        {
            if (parent is { IsOpen: false })
                Close();
        }

        public override void PostClose()
        {
            base.PostClose();
            Multiplayer.WriteSettingsToDisk();
        }

        private void RecacheMods()
        {
            IEnumerable<ModMetaData> Order(IEnumerable<ModMetaData> e, (SortType t, SortDirection d) sort)
            {
                var ord = e.OrderByDescending(m => 0);

                int GetScoreForSorting(ModMetaData mod)
                {
                    if (MultiplayerData.IsXmlMod(mod))
                        return 5;

                    return TryGetCompatInfo(mod)?.status ?? 0;
                }

                if (sort.d != SortDirection.None)
                {
                    if (sort.t == SortType.Name)
                        ord = ord.CreateOrderedEnumerable(m => nameProcessor(m.Name), null, sort.d == SortDirection.Descending);
                    else if (sort.t == SortType.Score)
                        ord = ord.CreateOrderedEnumerable(GetScoreForSorting, null, sort.d == SortDirection.Descending);
                }

                return ord;
            }

            modsActive = Order(ModsConfig.ActiveModsInLoadOrder, sort).ToList();

            modsInstalled = Order(
                ModLister.AllInstalledMods.Where(m => !m.Active && m.VersionCompatible),
                forceNameSort && sort.d == SortDirection.None ? (SortType.Name, SortDirection.Ascending) : sort
            ).ToList();

            modsOutdated = Order(
                ModLister.AllInstalledMods.Where(m => !m.Active && !m.VersionCompatible),
                forceNameSort && sort.d == SortDirection.None ? (SortType.Name, SortDirection.Ascending) : sort
            ).ToList();
        }

        private static ModCompatibility TryGetCompatInfo(ModMetaData mod)
        {
            if (!Multiplayer.settings.showModCompatibility)
                return null;

            return ModCompatibilityManager.LookupByWorkshopId(mod.publishedFileIdInt) ??
                   ModCompatibilityManager.LookupByName(mod.Name);
        }

        private static string SortChar(SortDirection dir) => dir switch
        {
            SortDirection.Ascending => "▲",
            SortDirection.Descending => "▼",
            _ => ""
        };

        enum SortType
        {
            Name, Score
        }

        enum SortDirection
        {
            None, Ascending, Descending
        }
    }

    [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents))]
    static class PageModsConfigIncreaseBottomButtonsWidth
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var list = insts.ToList();
            list.First(i => i.operand is 508f).operand = 708F;

            return list;
        }
    }


    [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoBottomButtons))]
    static class PageModsConfigAddButton
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var list = insts.ToList();

            var endGroupMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.EndGroup));

            var saveLoadListString = list.First(i => i.operand == "SaveLoadList");
            // WidgetRow is not stored as a local, only staying on stack. We need to duplicate it before its last use so we can use it as well.
            var dupInst = new CodeInstruction(OpCodes.Dup);
            saveLoadListString.MoveLabelsTo(dupInst);
            list.Insert(list.IndexOf(saveLoadListString), dupInst);

            var endGroupCall = list.First(i => i.operand == endGroupMethod);
            var ldarg = new CodeInstruction(OpCodes.Ldarg_0);
            endGroupCall.MoveLabelsTo(ldarg);

            list.InsertRange(list.IndexOf(endGroupCall), new[]
            {
                ldarg,
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PageModsConfigAddButton), nameof(DrawBtn))),
            });

            return list;
        }

        static void DrawBtn(WidgetRow widgetRow, Page_ModsConfig page)
        {
            // We use fixed width so label is ignored, so we're using null
            // Must be slightly bigger than original buttons which have width of 150,
            // as our label would not fit otherwise.
            DoButton(widgetRow.ButtonRect(null, 175f), page);
        }

        public static void DoButton(Rect btnRect, Window parent, bool forceNameSort = false, Func<string, string> nameProcessor = null)
        {
            using (MpStyle.Set(Color.green))
            {
                Texture2D atlas = Widgets.ButtonBGAtlas;
                if (Mouse.IsOver(btnRect))
                {
                    atlas = Widgets.ButtonBGAtlasMouseover;
                    if (Input.GetMouseButton(0))
                        atlas = Widgets.ButtonBGAtlasClick;
                }

                Widgets.DrawAtlas(btnRect, atlas);
            }

            using (MpStyle.Set(TextAnchor.MiddleCenter))
                Widgets.Label(btnRect, "MpModCompatButton".Translate());

            if (Widgets.ButtonInvisible(btnRect))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Find.WindowStack.Add(new ModCompatWindow(parent, true, forceNameSort, nameProcessor));
            }
        }
    }

    [HarmonyPatch]
    static class ModManagerAddButton
    {
        private static Func<string, string> trimModName;

        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodInfo TargetMethod() => AccessTools.Method("ModManager.Page_BetterModConfig:DoWindowContents");

        static void Prefix(Window __instance, ref Rect canvas)
        {
            PageModsConfigAddButton.DoButton(
                new Rect(0, 0, 280, 30),
                __instance,
                true,
                trimModName ??= AccessTools.MethodDelegate<Func<string, string>>(
                    AccessTools.Method("ModManager.Utilities:TrimModName")
                )
            );

            canvas.yMin += 35f;
        }
    }
}
