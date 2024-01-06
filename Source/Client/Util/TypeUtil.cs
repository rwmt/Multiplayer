using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Attempts to construct generic types (by calling <see cref="Type.MakeGenericType"/>) on the
        /// provided types (as long as they're generic and not already constructed). It'll attempt to find
        /// a type which satisfies all generic type constraints, if a type matching them exists. All
        /// unsuccessfully created generic types will be omitted from the result.
        /// </summary>
        /// <param name="types">The list of types which should be made generic (if needed and possible).</param>
        /// <param name="ignoreErrors">Determines if errors caused by no types found matching constraints should be shown.</param>
        /// <returns>
        /// <see cref="IEnumerable{T}"/> where each provided type is:
        /// <list type="bullet">
        ///     <item>the same as it was if it's not generic type or was already constructed</item>
        ///     <item>omitted if the method fails (likely due to no type matching the constraints)</item>
        ///     <item>a successful result from calling <see cref="Type.MakeGenericType"/> (with all constraints matched)</item>
        /// </list>
        /// </returns>
        public static IEnumerable<Type> TryMakeGenericTypes(this IEnumerable<Type> types, bool ignoreErrors = false)
            => types.Select(t => t.TryMakeGenericType(ignoreErrors)).Where(t => t != null);

        /// <summary>
        /// Attempts to construct a generic type (by calling <see cref="Type.MakeGenericType"/>) on the
        /// provided type (as long as it's generic and not already constructed). It'll attempt to find
        /// a type which satisfies all generic type constraints, if a type matching them all exists.
        /// </summary>
        /// <param name="type">The type which should be made generic (if needed and possible).</param>
        /// <param name="ignoreErrors">Determines if errors caused by no types found matching constraints should be shown.</param>
        /// <returns>
        /// <list type="bullet">
        ///     <item>provided argument <paramref name="type"/> itself if it's not generic type or was already constructed</item>
        ///     <item><see langword="null"/> if the method fails (likely due to no type matching the constraints)</item>
        ///     <item>successful result from calling <see cref="Type.MakeGenericType"/> (with all constraints matched)</item>
        /// </list>
        /// </returns>
        public static Type TryMakeGenericType(this Type type, bool ignoreErrors = false)
        {
            // Non-generic type or already constructed generic types
            // don't need to be constructed, return as-is.
            if (!type.IsGenericType || type.IsConstructedGenericType)
                return type;

            var genericArgs = type.GetGenericArguments();
            if (genericArgs.TryGetMatchingConstraints(out var targetArgs, out var errorMessage))
                return type.MakeGenericType(targetArgs);

            // Only log errors if the type is non-abstract or has no subclasses,
            // assuming abstract classes with no subclasses are unused.
            if (!ignoreErrors && (!type.IsAbstract || type.AllSubclassesNonAbstract().Any()))
                Log.Error($"Failed making generic type for type {type} with message: {errorMessage}");

            return null;
        }

        /// <summary>
        /// Attempts to find types matching restraints of generic arguments passed to this method.
        /// </summary>
        /// <param name="genericArgs">Array of generic arguments for which the constraints should be matched and returned as <paramref name="args"/>.</param>
        /// <param name="args">Array of types matching the provided generic argument constraints, or empty array if method failed.</param>
        /// <param name="error">Explains why the method failed, or an empty string if successful.</param>
        /// <returns><see langword="true" /> if types matching constraints were found, otherwise <see langword="false" />.</returns>
        private static bool TryGetMatchingConstraints(this IReadOnlyList<Type> genericArgs, out Type[] args, out string error)
        {
            if (genericArgs.EnumerableNullOrEmpty())
            {
                args = Type.EmptyTypes;
                error = "Trying to find constraints for generic arguments failed - the list of arguments was null or empty.";
                return false;
            }

            args = new Type[genericArgs.Count];

            for (var genericIndex = 0; genericIndex < genericArgs.Count; genericIndex++)
            {
                var constraints = genericArgs[genericIndex].GetGenericParameterConstraints();
                if (constraints.NullOrEmpty())
                {
                    // No constraints, just use object as argument and skip to next generic type
                    args[genericIndex] = typeof(object);
                    continue;
                }

                if (constraints.Length == 1)
                {
                    // Only one constraint, just use it and skip to next generic type
                    args[genericIndex] = constraints[0];
                    continue;
                }

                // Start off with all subtypes/implementations (including self) of our first constraint
                IEnumerable<Type> possibleMatches = constraints[0]
                    .IsInterface
                    ? constraints[0].AllImplementing().Concat(constraints[0])
                    : constraints[0].AllSubtypesAndSelf();

                // Go through each constraint (besides the first)
                // and limit the possible matches based on it.
                for (int constraintIndex = 1; constraintIndex < constraints.Length; constraintIndex++)
                {
                    var current = constraints[constraintIndex];
                    possibleMatches = possibleMatches.Where(t => current.IsAssignableFrom(t));
                }

                // As long as we have any result, grab and use it.
                // We don't really care which one we use here.
                var result = possibleMatches.FirstOrDefault();
                if (result == null)
                {
                    error = $"Could not find type matching specific constraint: {constraints.ToStringSafeEnumerable()}";
                    args = Type.EmptyTypes;
                    return false;
                }

                // Set the result for specific argument
                args[genericIndex] = result;
            }

            error = string.Empty;
            return true;
        }
    }
}
