using System;
using Multiplayer.API;
using Multiplayer.Client.Util;

namespace Multiplayer.Client;

public static class ApiSerialization
{
    public static Type[] syncSimples;
    public static Type[] sessions;

    public static void Init()
    {
        syncSimples = TypeCache.AllInterfaceImplementationsOrdered(typeof(ISyncSimple));
        sessions = TypeCache.AllSubclassesNonAbstractOrdered(typeof(Session));
    }
}
