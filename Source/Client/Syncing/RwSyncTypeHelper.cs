using System;
using System.Collections.Generic;
using Multiplayer.Client.Util;
using Multiplayer.Common;

namespace Multiplayer.Client;

internal class RwSyncTypeHelper : SyncTypeHelper
{
    private Dictionary<Type, List<Type>> manualImplTypes = new();

    public void AddImplManually(Type baseType, Type implType)
    {
        manualImplTypes.GetOrAddNew(baseType).Add(implType);
    }

    public override List<Type> GetImplementations(Type baseType)
    {
        if (manualImplTypes.TryGetValue(baseType, out var manualImplType))
            return manualImplType;

        return baseType.IsInterface
            ? TypeCache.interfaceImplementationsOrdered[baseType]
            : TypeCache.subClassesOrdered[baseType];
    }
}
