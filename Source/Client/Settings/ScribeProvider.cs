using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client;

public class ScribeProvider : ScribeLike.Provider
{
    public override void Look<T>(ref T value, string label, T defaultValue, bool forceSave)
    {
        Scribe_Values.Look(ref value, label, defaultValue, forceSave);
    }
}
