using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Saving
{
    public static class Scribe_Custom
    {
        public static void LookValue<T>(T t, string label, bool force = false)
        {
            Scribe_Values.Look(ref t, label, forceSave: force);
        }

        public static void LookDeep<T>(T t, string label)
        {
            Scribe_Deep.Look(ref t, label);
        }

        public static void LookULong(ref ulong value, string label, ulong defaultValue = 0)
        {
            string valueStr = value.ToString();
            Scribe_Values.Look(ref valueStr, label, defaultValue.ToString());
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                ulong.TryParse(valueStr, out value);
        }

        public static void LookRect(ref Rect rect, string label)
        {
            Vector4 value = new Vector4(rect.x, rect.y, rect.width, rect.height);
            Scribe_Values.Look(ref value, label);
            rect = new Rect(value.x, value.y, value.z, value.w);
        }

        // Copy of RimWorld's method but with ctor args
        public static void LookValueDeep<K, V>(ref SortedDictionary<K, V> dict, string label, params object[] valueCtorArgs)
        {
            if (Scribe.EnterNode(label))
            {
                try
                {
                    List<K> keysWorkingList = null;
                    List<V> valuesWorkingList = null;

                    if (Scribe.mode == LoadSaveMode.Saving)
                    {
                        keysWorkingList = dict.Keys.ToList();
                        valuesWorkingList = dict.Values.ToList();
                    }

                    Scribe_Collections.Look(ref keysWorkingList, "keys", LookMode.Value);
                    Scribe_Collections.Look(ref valuesWorkingList, "values", LookMode.Deep, valueCtorArgs);

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        dict.Clear();

                        if (keysWorkingList == null)
                        {
                            Log.Error("Cannot fill dictionary because there are no keys.");
                            return;
                        }

                        if (valuesWorkingList == null)
                        {
                            Log.Error("Cannot fill dictionary because there are no values.");
                            return;
                        }

                        if (keysWorkingList.Count != valuesWorkingList.Count)
                        {
                            Log.Error($"Keys count does not match the values count while loading a dictionary. Some elements will be skipped. keys={keysWorkingList.Count}, values={valuesWorkingList.Count}");
                        }

                        int num = Math.Min(keysWorkingList.Count, valuesWorkingList.Count);
                        for (int i = 0; i < num; i++)
                        {
                            if (keysWorkingList[i] == null)
                            {
                                Log.Error($"Null key while loading dictionary of {typeof(K)} and {typeof(V)}");
                                continue;
                            }

                            try
                            {
                                dict.Add(keysWorkingList[i], valuesWorkingList[i]);
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Exception in LookDictionary(node={label}): {ex}");
                            }
                        }
                    }
                }
                finally
                {
                    Scribe.ExitNode();
                }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                dict = null;
            }
        }
    }
}
