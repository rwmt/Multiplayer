using System;
using System.Linq;
using Verse;

namespace Multiplayer.Client.Util
{
    public static class TypeUtil
    {
        public static Type[] AllImplementationsOrdered(Type type)
        {
            return type.AllImplementing()
                .OrderBy(t => t.IsInterface)
                .ThenBy(t => t.Name)
                .ToArray();
        }

        public static Type[] AllSubclassesNonAbstractOrdered(Type type) {
            return type
                .AllSubclassesNonAbstract()
                .OrderBy(t => t.Name)
                .ToArray();
        }
    }
}
