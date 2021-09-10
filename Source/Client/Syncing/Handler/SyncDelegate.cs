using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client
{
    public class SyncDelegate : SyncMethod, ISyncDelegate
    {
        public const string DELEGATE_THIS = "<>4__this";

        private Type[] fieldTypes;
        private string[] fieldPaths;
        private string[] fieldPathsNoTypes;
        private SyncTransformer[] fieldTransformers;

        private string[] allowedNull;
        private string[] cancelIfNull;
        private string[] removeNullsFromLists;

        public SyncDelegate(Type delegateType, MethodInfo method, string[] inPaths) :
            base(delegateType, null, method, null)
        {
            if (inPaths == null)
            {
                List<string> fieldList = new List<string>();
                AllDelegateFieldsRecursive(delegateType, path => { fieldList.Add(path); return false; });
                fieldPaths = fieldList.ToArray();
            }
            else
            {
                var temp = new UniqueList<string>();
                foreach (string path in inPaths.Select(p => MpReflection.AppendType(p, delegateType)))
                {
                    string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    string increment = parts[0] + "/" + parts[1];
                    for (int i = 2; i < parts.Length; i++)
                    {
                        if (!MpReflection.PathType(increment).IsCompilerGenerated())
                            break;
                        temp.Add(increment);
                        increment += "/" + parts[i];
                    }

                    temp.Add(path);
                }

                fieldPaths = temp.ToArray();
            }

            fieldTypes = fieldPaths.Select(path => MpReflection.PathType(path)).ToArray();
            fieldPathsNoTypes = fieldPaths.Select(path => MpReflection.RemoveType(path)).ToArray();
            fieldTransformers = new SyncTransformer[fieldTypes.Length];
        }

        protected override void WriteTarget(object target, object[] args, SyncMethodWriter writer)
        {
            for (int i = 0; i < fieldPaths.Length; i++)
            {
                var val = target.GetPropertyOrField(fieldPaths[i]);
                var type = fieldTypes[i];
                var path = fieldPaths[i];

                if (fieldTransformers[i] is SyncTransformer tr)
                    writer(tr.Writer.DynamicInvoke(val, target, args), tr.NetworkType, path);
                else if (!fieldTypes[i].IsCompilerGenerated())
                    writer(val, type, path);
            }
        }

        protected override object ReadTarget(ByteReader data)
        {
            object target = Activator.CreateInstance(targetType);

            for (int i = 0; i < fieldPaths.Length; i++)
            {
                string path = fieldPaths[i];
                string noTypePath = fieldPathsNoTypes[i];
                Type fieldType = fieldTypes[i];
                object value;

                if (fieldTransformers[i] is SyncTransformer tr)
                    value = tr.Reader.DynamicInvoke(SyncSerialization.ReadSyncObject(data, tr.NetworkType));
                else if (fieldType.IsCompilerGenerated())
                    value = Activator.CreateInstance(fieldType);
                else
                    value = SyncSerialization.ReadSyncObject(data, fieldType);

                if (value == null)
                {
                    if (allowedNull != null && !allowedNull.Contains(noTypePath)) return null;
                    if (noTypePath.EndsWith(DELEGATE_THIS)) return null;
                    if (cancelIfNull != null && cancelIfNull.Contains(noTypePath)) return null;
                }

                if (removeNullsFromLists != null && removeNullsFromLists.Contains(noTypePath) && value is IList list)
                    list.RemoveNulls();

                MpReflection.SetValue(target, path, value);
            }

            return target;
        }

        public ISyncDelegate CancelIfAnyFieldNull(params string[] allowed)
        {
            CheckFieldsExist(allowed);
            allowedNull = allowed;
            return this;
        }

        public ISyncDelegate CancelIfFieldsNull(params string[] fields)
        {
            CheckFieldsExist(fields);
            cancelIfNull = fields;
            return this;
        }

        public ISyncDelegate RemoveNullsFromLists(params string[] listFields)
        {
            CheckFieldsExist(listFields);
            removeNullsFromLists = listFields;
            return this;
        }

        public ISyncMethod TransformField<Live, Networked>(string field, Serializer<Live, Networked> serializer)
        {
            CheckFieldsExist(field);

            var index = fieldPathsNoTypes.FindIndex(field);

            if (fieldTypes[index] != typeof(Live))
                throw new Exception($"Arg transformer param mismatch for {this}: {fieldTypes[index]} != {typeof(Live)}");

            fieldTransformers[index] = new(typeof(Live), typeof(Networked), serializer.Writer, serializer.Reader);
            return this;
        }

        private void CheckFieldsExist(params string[] fields)
        {
            foreach (var f in fields)
                if (!fieldPathsNoTypes.Contains(f))
                    throw new Exception($"Field with path {f} not found");
        }

        public static SyncDelegate Lambda(Type parentType, string parentMethod, int lambdaOrdinal, Type[] parentArgs = null, MethodType parentMethodType = MethodType.Normal)
        {
            return Sync.RegisterSyncDelegate(
                MpMethodUtil.GetLambda(parentType, parentMethod, parentMethodType, parentArgs, lambdaOrdinal),
                null
            );
        }

        public static SyncDelegate LocalFunc(Type parentType, string parentMethod, string name, Type[] parentArgs = null)
        {
            return Sync.RegisterSyncDelegate(
                MpMethodUtil.GetLocalFunc(parentType, parentMethod, MethodType.Normal, parentArgs, name),
                null
            );
        }

        public override void Validate()
        {
            for (int i = 0; i < fieldTypes.Length; i++)
                if (fieldTransformers[i] is SyncTransformer tr)
                    ValidateType($"Field {fieldPaths[i]} type", tr.NetworkType);
                else if (!fieldTypes[i].IsCompilerGenerated())
                    ValidateType($"Field {fieldPaths[i]} type", fieldTypes[i]);

            for (int i = 0; i < argTypes.Length; i++)
                ValidateType($"Arg {i} type", argTransformers[i]?.NetworkType ?? argTypes[i]);
        }

        public override string ToString()
        {
            return $"SyncDelegate {method.MethodDesc()}";
        }

        public static bool AllDelegateFieldsRecursive(Type type, Func<string, bool> getter, string path = "", bool allowDelegates = false)
        {
            if (path.NullOrEmpty())
                path = type.ToString();

            foreach (FieldInfo field in type.GetDeclaredInstanceFields())
            {
                string curPath = path + "/" + field.Name;

                if (!allowDelegates && typeof(Delegate).IsAssignableFrom(field.FieldType))
                    continue;

                if (getter(curPath))
                    return true;

                if (!field.FieldType.IsCompilerGenerated())
                    continue;

                if (AllDelegateFieldsRecursive(field.FieldType, getter, curPath))
                    return true;
            }

            return false;
        }

        public ISyncDelegate CancelIfNoSelectedObjects()
        {
            CancelIfNoSelectedMapObjects();
            return this;
        }

        ISyncDelegate ISyncDelegate.SetContext(SyncContext context)
        {
            SetContext(context);
            return this;
        }

        ISyncDelegate ISyncDelegate.SetDebugOnly()
        {
            SetDebugOnly();
            return this;
        }
    }

}
