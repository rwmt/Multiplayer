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
        // Writing ushort.MaxValue (the null marker) followed by the object's data would misalign the reader
        int index = GetImplementations(baseType).IndexOf(type);
        if (index < 0 || index >= ushort.MaxValue)
            throw new Multiplayer.Client.SerializationException($"Type {type} isn't a known implementation of {baseType}");

        return (ushort)index;
    }

    public abstract List<Type> GetImplementations(Type baseType);
}
