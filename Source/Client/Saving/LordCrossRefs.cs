using HarmonyLib;
using Verse.AI.Group;

namespace Multiplayer.Client;

// Lords loaded with the game register into sharedCrossRefs automatically (the
// loader directory is swapped to it), but lords created at runtime never did -
// so synced blobs referencing them (e.g. ceremony delivery jobs) failed to
// resolve and ran lordless (live find: GiveToPawn job losing Lord_7).
[HarmonyPatch(typeof(LordManager), nameof(LordManager.AddLord))]
static class RegisterLordCrossRef
{
    static void Postfix(Lord newLord)
    {
        if (Multiplayer.game != null)
            ScribeUtil.sharedCrossRefs.RegisterLoaded(newLord);
    }
}

[HarmonyPatch(typeof(LordManager), nameof(LordManager.RemoveLord))]
static class UnregisterLordCrossRef
{
    static void Postfix(Lord oldLord)
    {
        if (Multiplayer.game != null)
            ScribeUtil.sharedCrossRefs.Unregister(oldLord);
    }
}
