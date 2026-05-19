using Verse;

namespace Multiplayer.Client.Util;

// For keys passed through vanilla code that hardcodes `.Translate()` (e.g. InspectPaneUtility's
// `tab.labelKey.Translate()`). MpTranslate.Fallback can't help - vanilla re-translates the resolved
// English string and dev-mode pseudo-localization mangles the result. Inject the value into the
// active language so the normal Translate() path resolves cleanly until the keys land in the
// Languages submodule.
public static class PingRuntimeTranslations
{
    public static void Register()
    {
        // MarkerInspectTab.labelKey - vanilla's InspectPaneUtility.DoTabs calls .Translate() on it.
        Add("MpMarkerInspectTab_Label", "Markers");
    }

    private static void Add(string key, string value)
    {
        var lang = LanguageDatabase.activeLanguage;
        if (lang == null) return;
        // Skip if Multiplayer-Locale already provides a translation - real localization always wins.
        if (lang.keyedReplacements.TryGetValue(key, out var existing) && !existing.isPlaceholder) return;
        lang.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement
        {
            key = key,
            value = value,
            isPlaceholder = false,
        };
    }
}
