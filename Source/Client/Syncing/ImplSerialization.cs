using System;
using Multiplayer.Client.Experimental;
using Multiplayer.Client.Util;

namespace Multiplayer.Client;

public static class ImplSerialization
{
    public static Type[] syncSimples;

    public static void Init()
    {
        syncSimples = TypeUtil.AllImplementationsOrdered(typeof(ISyncSimple));
    }
}
