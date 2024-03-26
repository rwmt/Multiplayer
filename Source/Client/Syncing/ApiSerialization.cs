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
        syncSimples = TypeUtil.AllImplementationsOrdered(typeof(ISyncSimple));
        sessions = TypeUtil.AllImplementationsOrdered(typeof(Session));
    }
}
