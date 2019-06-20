using System;
using System.Reflection;

namespace Multiplayer.API
{
    /// <summary>
    /// An exception that is thrown if you try to use the API without avaiable host.
    /// </summary>
    public class UninitializedAPI : Exception
    {
    }

    class Dummy : IAPI
    {
        public bool IsHosting => false;

        public bool IsInMultiplayer => false;

        public string PlayerName => null;

        public void WatchBegin()
        {
            throw new UninitializedAPI();
        }

        public void Watch(Type targetType, string fieldName, object index = null)
        {
            throw new UninitializedAPI();
        }

        public void Watch(object target, string fieldName, object index = null)
        {
            throw new UninitializedAPI();
        }

        public void Watch(string memberPath, object target = null, object index = null)
        {
            throw new UninitializedAPI();
        }

        public void WatchEnd()
        {
            throw new UninitializedAPI();
        }

        public void RegisterAll()
        {
            throw new UninitializedAPI();
        }

        public void RegisterAll(Assembly assembly)
        {
            throw new UninitializedAPI();
        }

        public ISyncField RegisterSyncField(Type targetType, string memberPath)
        {
            throw new UninitializedAPI();
        }

        public ISyncField RegisterSyncField(FieldInfo field)
        {
            throw new UninitializedAPI();
        }

        public ISyncMethod RegisterSyncMethod(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            throw new UninitializedAPI();
        }

        public ISyncMethod RegisterSyncMethod(MethodInfo method, SyncType[] argTypes)
        {
            throw new UninitializedAPI();
        }

        public ISyncDelegate RegisterSyncDelegate(Type inType, string nestedType, string methodName, string[] fields, Type[] args = null)
        {
            throw new UninitializedAPI();
        }

        public ISyncDelegate RegisterSyncDelegate(Type type, string nestedType, string method)
        {
            throw new UninitializedAPI();
        }

        public void RegisterSyncWorker<T>(SyncWorkerDelegate<T> syncWorkerDelegate, Type targetType = null, bool isImplicit = false, bool shouldConstruct = false)
        {
            throw new UninitializedAPI();
        }
    }
}
