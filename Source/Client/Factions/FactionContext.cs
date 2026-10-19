using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public static class FactionContext
    {
        public static Stack<Faction> stack = new();

        // Parallel to stack: optional state of the entry's push, restored by its pop (see FactionExtensions.PushFaction)
        private static Stack<object> stateStack = new();

        public static Faction Push(Faction newFaction, bool force = false)
        {
            if (newFaction == null || !force && Find.FactionManager.ofPlayer == newFaction || !newFaction.def.isPlayer)
            {
                stack.Push(null);
                stateStack.Push(null);
                return null;
            }

            stack.Push(Find.FactionManager.OfPlayer);
            stateStack.Push(null);
            Set(newFaction);

            return newFaction;
        }

        // Attaches state to the entry pushed last
        public static void SetTopState(object state)
        {
            stateStack.Pop();
            stateStack.Push(state);
        }

        public static Faction Pop() => Pop(out _);

        public static Faction Pop(out object state)
        {
            state = stateStack.Count > 0 ? stateStack.Pop() : null;
            Faction f = stack.Pop();
            if (f != null)
                Set(f);
            return f;
        }

        public static void Set(Faction newFaction)
        {
            Find.FactionManager.ofPlayer = newFaction;
        }

        // Like AsyncTimeComp.RestoreRandStack: a push left unpopped by an exception would leave the wrong
        // Faction.OfPlayer and faction data applied after the context ends and keep the stack too deep. Pops (and
        // restores what each entry applied) down to the depth the context had after its own pushes.
        public static void RestoreStack(int depth, object context)
        {
            int count = stack.Count;
            if (count == depth) return;

            // Too shallow (an extra pop) can't be repaired, only reported
            Log.ErrorOnce($"Multiplayer: unbalanced faction push/pop in {context}: stack depth {count}, expected {depth}", 0x2B71C4 ^ depth);

            while (stack.Count > depth)
                Factions.FactionExtensions.PopFaction();
        }

        public static void Clear()
        {
            stack.Clear();
            stateStack.Clear();
        }
    }

}
