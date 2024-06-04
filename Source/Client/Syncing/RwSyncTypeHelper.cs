using System;
using System.Collections.Generic;
using Multiplayer.Client.Util;
using Multiplayer.Common;

namespace Multiplayer.Client;

internal class RwSyncTypeHelper : SyncTypeHelper
{
    public override List<Type> GetImplementations(Type baseType)
    {
        return baseType.IsInterface
            ? TypeCache.interfaceImplementationsOrdered[baseType]
            : TypeCache.subClassesOrdered[baseType];
    }
}
