using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public static class FactionContext
    {
        public static Stack<Faction> stack = new();

        // Parallel to stack: whether the entry's push applied its faction to every map (see FactionExtensions.PushFaction)
        private static Stack<bool> allMapsStack = new();

        public static Faction Push(Faction newFaction, bool force = false) => Push(newFaction, force, false);

        public static Faction Push(Faction newFaction, bool force, bool allMaps)
        {
            if (newFaction == null || !force && Find.FactionManager.ofPlayer == newFaction || !newFaction.def.isPlayer)
            {
                stack.Push(null);
                allMapsStack.Push(false);
                return null;
            }

            stack.Push(Find.FactionManager.OfPlayer);
            allMapsStack.Push(allMaps);
            Set(newFaction);

            return newFaction;
        }

        public static Faction Pop() => Pop(out _);

        public static Faction Pop(out bool allMaps)
        {
            allMaps = allMapsStack.Count > 0 && allMapsStack.Pop();
            Faction f = stack.Pop();
            if (f != null)
                Set(f);
            return f;
        }

        public static void Set(Faction newFaction)
        {
            Find.FactionManager.ofPlayer = newFaction;
        }

        public static void Clear()
        {
            stack.Clear();
            allMapsStack.Clear();
        }
    }

}
