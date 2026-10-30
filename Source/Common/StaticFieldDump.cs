using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Multiplayer.Common
{
    /// <summary>
    /// The reflection behind the "Print static fields" debug dump, which lists mutable static state across
    /// loaded assemblies. Unsynchronized static state is a common source of desyncs, so the dump is one of
    /// the first things reached for when a session diverges.
    ///
    /// Kept here rather than beside the debug action so it can be tested. The test project cannot
    /// reference the client, which needs the game's own assemblies to load.
    /// </summary>
    public static class StaticFieldDump
    {
        /// <summary>
        /// Reads a static field's current value.
        ///
        /// Contract: never propagate. Reading a static field runs its declaring type's initializer, which
        /// is arbitrary code and can fail for reasons that have nothing to do with the dump. A caller
        /// wants the other few thousand fields even when one is unreadable.
        ///
        /// MonoMod is the case that forced this: it ships every platform's interop types in one assembly,
        /// so the ones belonging to another operating system throw DllNotFoundException the moment they
        /// are touched. That is true on every platform -- only the type names change.
        /// </summary>
        /// <param name="field">The static field to read.</param>
        /// <param name="value">The value read, or null when it could not be read.</param>
        /// <param name="failure">A short description of why it could not be read, or null on success.</param>
        /// <returns>Whether the value was read.</returns>
        public static bool TryReadStaticValue(FieldInfo field, out object value, out string failure)
        {
            try
            {
                value = field.GetValue(null);
                failure = null;
                return true;
            }
            catch (Exception e)
            {
                // The base exception, because the interesting part is what the initializer actually hit.
                // Reporting TypeInitializationException would name the mechanism and hide the cause.
                value = null;
                failure = e.GetBaseException().GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// The types in <paramref name="assembly"/>.
        ///
        /// Contract: report whatever loaded. An assembly referencing something absent cannot enumerate
        /// all of its types, but it still resolves most of them, and those are worth dumping.
        /// </summary>
        public static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // Types is padded with nulls for the entries that failed to load.
                return e.Types.Where(t => t != null);
            }
        }
    }
}
