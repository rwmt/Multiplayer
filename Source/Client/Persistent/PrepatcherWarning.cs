using Multiplayer.Client.Util;
using MultiplayerLoader;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent;

/// <summary>
/// Shown in the host/join windows and the ritual dialogs when Prepatcher didn't patch the ritual role selection
/// widget (see SyncRituals.PrepatchesApplied). The texts fall back to English as the keys may not be translated yet.
/// </summary>
public static class PrepatcherWarning
{
    public static bool Show => !SyncRituals.PrepatchesApplied;

    public static string Label =>
        "MpPrepatcherMissing".TryTranslate(out var text) ? text.ToString() : "Prepatcher isn't active";

    public static string Desc =>
        "MpPrepatcherMissingDesc".TryTranslate(out var text)
            ? text.ToString()
            : "Multiplayer needs the Prepatcher mod to share the role assignments of ritual dialogs between players. " +
              "Without it, each player only sees their own changes there and a ritual starts with the roles " +
              "chosen by the player who starts it.\n\nMake sure Prepatcher is installed and enabled. If it is, " +
              "check the log for why it failed.";

    public static float LabelWidth(GameFont font)
    {
        using (MpStyle.Set(font))
            return Text.CalcSize(Label).x;
    }

    // The label in the warning color with the description as its tooltip
    public static void DrawLabel(Rect rect, GameFont font = GameFont.Small)
    {
        using (MpStyle.Set(font).Set(ColorLibrary.RedReadable))
            Widgets.Label(rect, Label);

        Widgets.DrawHighlightIfMouseover(rect);
        TooltipHandler.TipRegion(rect, Desc);
    }
}
