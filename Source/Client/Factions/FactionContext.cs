using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public static class FactionContext
    {
        public static Stack<Faction> stack = new();

        public static Faction Push(Faction newFaction, bool force = false)
        {
            if (newFaction == null || !force && Find.FactionManager.ofPlayer == newFaction || !newFaction.def.isPlayer)
            {
                stack.Push(null);
                return null;
            }

            stack.Push(Find.FactionManager.OfPlayer);
            Set(newFaction);

            return newFaction;
        }

        public static Faction Pop()
        {
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
        }
    }

}

