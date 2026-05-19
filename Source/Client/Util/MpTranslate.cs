using Verse;

namespace Multiplayer.Client.Util
{
    // English fallback for keys not yet shipped in rwmt/Multiplayer-Locale.
    public static class MpTranslate
    {
        public static string Fallback(string key, string fallback)
            => key.CanTranslate() ? key.Translate().ToString() : fallback;

        public static string Fallback(string key, string fallback, NamedArgument arg)
            => key.CanTranslate() ? key.Translate(arg).ToString() : fallback;

        public static string Fallback(string key, string fallback, NamedArgument arg1, NamedArgument arg2)
            => key.CanTranslate() ? key.Translate(arg1, arg2).ToString() : fallback;
    }
}
