using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// Tools for working with the Harmony library.
    /// </summary>
    public static class HarmonyUtil
    {
        private const int DefaultPatchPriority = 400;

        /// <summary>
        /// Produces a human-readable list of Harmony patches on a given set of methods.
        /// </summary>
        public static string DescribePatchedMethodsList(List<(MethodBase method, HarmonyLib.Patches patches)> patchedMethods)
        {
            try
            {
                // generate method name strings so we can sort the patches alphabetically
                var namedMethodList = patchedMethods.Select(x =>
                {
                    var nestedName = GetNestedMemberName(x.method);
                    return (methodName: nestedName, x.patches);
                }).ToList();

                if (namedMethodList.Count == 0)
                {
                    return "No patches have been reported.";
                }

                // sort patches by patched method name
                namedMethodList.Sort((m1, m2) =>
                    string.Compare(m1.methodName, m2.methodName, StringComparison.Ordinal));

                var builder = new StringBuilder(namedMethodList.Count * 100);
                var workList = new List<Patch>(6);
                foreach (var (methodName, patches) in namedMethodList)
                {
                    // write patched method
                    builder.Append(methodName);
                    builder.Append(": ");
                    // write patches
                    bool anyPatches = false;
                    // write prefixes
                    if (patches.Prefixes is { Count: > 0 })
                    {
                        anyPatches = true;
                        builder.Append("PRE: ");
                        patches.Prefixes.CopyToList(workList);
                        AppendPatchList(workList, builder);
                    }

                    // write postfixes
                    if (patches.Postfixes is { Count: > 0 })
                    {
                        anyPatches = true;
                        builder.EnsureEndsWithSpace();
                        builder.Append("post: ");
                        patches.Postfixes.CopyToList(workList);
                        AppendPatchList(workList, builder);
                    }

                    // write transpilers
                    if (patches.Transpilers is { Count: > 0 })
                    {
                        anyPatches = true;
                        builder.EnsureEndsWithSpace();
                        builder.Append("TRANS: ");
                        patches.Transpilers.CopyToList(workList);
                        AppendPatchList(workList, builder);
                    }
                    if (!anyPatches) builder.Append("(no patches)");

                    builder.AppendLine();
                }

                return builder.ToString();
            }
            catch (Exception e)
            {
                return "An exception occurred while collating patch data:\n" + e;
            }
        }

        /// <summary>
        /// Produces a human-readable list of all Harmony versions present and their respective owners.
        /// </summary>
        public static string DescribeHarmonyVersions(List<(MethodBase, HarmonyLib.Patches)> patchMethods)
        {
            try
            {
                var modVersionPairs = GetHarmonyVersions(patchMethods);
                return "Harmony versions present: " +
                       modVersionPairs.GroupBy(kv => kv.Value, kv => kv.Key).OrderByDescending(grp => grp.Key)
                           .Select(grp => $"{grp.Key}: {grp.Join(", ")}").Join("; ");
            }
            catch (Exception e)
            {
                return "An exception occurred while collating Harmony version data:\n" + e;
            }
        }

        private static Dictionary<string, Version> GetHarmonyVersions(List<(MethodBase, HarmonyLib.Patches patches)> patchMethods)
        {
            var result = new Dictionary<string, Version>();
            var assemblies = patchMethods
                .Select(x => x.patches)
                .SelectMany(x => x.Prefixes.Concat(x.Postfixes).Concat(x.Transpilers).Concat(x.Finalizers))
                .ToDictionaryConsistent(fix => fix.PatchMethod.DeclaringType.Assembly, fix => fix.owner);
            assemblies.Do(info =>
            {
                AssemblyName assemblyName = info.Key.GetReferencedAssemblies().FirstOrDefault(a => a.Name.Equals("0Harmony", StringComparison.Ordinal));
                if (assemblyName == null) return;
                result[info.Value] = assemblyName.Version;
            });
            return result;
        }

        internal static string GetNestedMemberName(MemberInfo member, int maxParentTypes = 10)
        {
            var sb = new StringBuilder(member.Name);
            var currentDepth = 0;
            var currentType = member.DeclaringType;
            while (currentType != null && currentDepth < maxParentTypes)
            {
                sb.Insert(0, '.');
                sb.Insert(0, currentType.Name);
                currentType = currentType.DeclaringType;
                currentDepth++;
            }

            return sb.ToString();
        }

        private static void AppendPatchList(List<Patch> patchList, StringBuilder builder)
        {
            // ensure that patches appear in the same order they execute
            patchList.Sort((left, right) => left.priority != right.priority
                ? -left.priority.CompareTo(right.priority)
                : left.index.CompareTo(right.index));

            var isFirstEntry = true;
            foreach (var patch in patchList)
            {
                if (!isFirstEntry)
                {
                    builder.Append(", ");
                }

                isFirstEntry = false;
                // write priority if set
                if (patch.priority != DefaultPatchPriority)
                {
                    builder.Append('[').Append(patch.priority).Append(']');
                }

                // write full destination method name
                builder.Append(patch.PatchMethod.FullName());
            }
        }

        private static void EnsureEndsWithSpace(this StringBuilder builder)
        {
            if (builder[builder.Length - 1] != ' ')
            {
                builder.Append(" ");
            }
        }

        private static string FullName(this MethodBase methodInfo)
        {
            if (methodInfo == null) return "[null reference]";
            if (methodInfo.DeclaringType == null) return methodInfo.Name;
            return methodInfo.DeclaringType.FullName + "." + methodInfo.Name;
        }
    }
}
