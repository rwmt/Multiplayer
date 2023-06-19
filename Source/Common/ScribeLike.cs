namespace Multiplayer.Common;

public static class ScribeLike
{
    public static Provider provider;

    /// <summary>
    /// Corresponds to Scribe_Values.Look
    /// </summary>
    public static void Look<T>(ref T? value, string label, T? defaultValue = default, bool forceSave = false)
    {
        provider.Look(ref value, label, defaultValue, forceSave);
    }

    public abstract class Provider
    {
        public abstract void Look<T>(ref T value, string label, T defaultValue, bool forceSave);
    }
}
