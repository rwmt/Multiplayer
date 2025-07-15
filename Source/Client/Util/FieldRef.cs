using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;

namespace Multiplayer.Client.Util;

public static class FieldRefs
{
    public static AccessTools.FieldRef<WorldSelector, List<WorldObject>> worldSelected = AccessTools.FieldRefAccess<WorldSelector, List<WorldObject>>(nameof(WorldSelector.selected));
}
