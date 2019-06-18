using System;
using System.Reflection;

using Multiplayer.API;
using Multiplayer.Client;

namespace Multiplayer.Common
{
    public class MultiplayerAPIBridge : IAPI
    {
        public static readonly IAPI Instance = new MultiplayerAPIBridge();

        public bool IsHosting => MultiplayerServer.instance != null;

        public bool IsInMultiplayer => Client.Multiplayer.session != null;

        public string PlayerName => Client.Multiplayer.username;

        public void WatchBegin()
        {
            Sync.FieldWatchPrefix();
        }

        public void Watch(Type targetType, string fieldName, object index = null)
        {
            var syncField = Sync.GetRegisteredSyncField(targetType, fieldName);

            if (syncField == null) {
                throw new ArgumentException($"{targetType}/{fieldName} not found");
            }

            syncField.Watch(null, index);
        }

        public void Watch(object target, string fieldName, object index = null)
        {
            Watch(fieldName, target, index);
        }

        public void Watch(string memberPath, object target = null, object index = null)
        {
            var syncField = Sync.GetRegisteredSyncField(target?.GetType(), memberPath);

            if (syncField == null) {
                throw new ArgumentException($"{memberPath} not found");
            }

            syncField.Watch(target, index);
        }

        public void WatchEnd()
        {
            Sync.FieldWatchPostfix();
        }

        public void RegisterAll()
        {
            var method = new System.Diagnostics.StackTrace().GetFrame(1).GetMethod();
            var assembly = method.ReflectedType.Assembly;
            RegisterAll(assembly);
        }

        public void RegisterAll(Assembly assembly)
        {
            Sync.RegisterAllAttributes(assembly);
            PersistentDialog.BindAll(assembly);
        }

        public ISyncField RegisterSyncField(Type targetType, string memberPath)
        {
            return Sync.RegisterSyncField(targetType, memberPath);
        }

        public ISyncField RegisterSyncField(FieldInfo field)
        {
            return Sync.RegisterSyncField(field);
        }

        public ISyncMethod RegisterSyncMethod(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            return Sync.RegisterSyncMethod(type, methodOrPropertyName, argTypes);
        }

        public ISyncMethod RegisterSyncMethod(MethodInfo method, SyncType[] argTypes)
        {
            return Sync.RegisterSyncMethod(method, argTypes);
        }

        public ISyncDelegate RegisterSyncDelegate(Type inType, string nestedType, string methodName, string[] fields, Type[] args = null)
        {
            return Sync.RegisterSyncDelegate(inType, nestedType, methodName, fields, args);
        }

        public ISyncDelegate RegisterSyncDelegate(Type type, string nestedType, string method)
        {
            return Sync.RegisterSyncDelegate(type, nestedType, method);
        }

        public void RegisterSyncWorker<T>(SyncWorkerDelegate<T> syncWorkerDelegate, Type targetType = null, bool isImplicit = false, bool shouldConstruct = false)
        {
            Sync.RegisterSyncWorker(syncWorkerDelegate, targetType, isImplicit: isImplicit, shouldConstruct: shouldConstruct);
        }
    }
}