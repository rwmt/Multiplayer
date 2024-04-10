using System;
using System.Collections.Generic;

namespace Multiplayer.Common;

public abstract class SyncTypeHelper
{
    public Type GetImplementationByIndex(Type baseType, ushort index)
    {
        return GetImplementations(baseType)[index];
    }

    public ushort GetIndexFromImplementation(Type baseType, Type type)
    {
        return (ushort)GetImplementations(baseType).IndexOf(type);
    }

    public abstract List<Type> GetImplementations(Type baseType);
}
