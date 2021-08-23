using HarmonyLib;

namespace Multiplayer.Client
{
    public static class MpPriority
    {
        public const int MpLast = Priority.Last - 2; // -1 is a special case in Harmony
        public const int MpFirst = Priority.First + 1;
    }

}
