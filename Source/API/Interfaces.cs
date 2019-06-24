using System;
using System.Reflection;

namespace Multiplayer.API
{
    /// <summary>
    /// SyncField interface.
    /// </summary>
    /// <example>
    /// <para>Creates and registers a SyncField that points to <c>myField</c> in object of type <c>MyType</c> and enables its change buffer.</para>
    /// <code>
    /// MPApi.SyncField(typeof(MyType), "myField").SetBufferChanges();
    /// </code>
    /// <para>Creates and registers a SyncField that points to <c>myField</c> which resides in <c>MyStaticClass</c>.</para>
    /// <code>
    /// MPApi.SyncField(null, "MyAssemblyNamespace.MyStaticClass.myField");
    /// </code>
    /// <para>Creates and registers a SyncField that points to <c>myField</c> that resides in an object stored by myEnumberable defined in an object of type <c>MyType</c>.</para>
    /// <para>To watch this one you have to supply an index in <see cref="Watch(object, object)"/>.</para>
    /// <code>
    /// MPApi.SyncField(typeof(MyType), "myEnumerable/[]/myField");
    /// </code>
    /// </example>
    public interface ISyncField
    {
        /// <summary>
        /// Instructs SyncField to cancel synchronization if the value of the member it's pointing at is null.
        /// </summary>
        /// <returns>self</returns>
        ISyncField CancelIfValueNull();

        /// <summary>
        /// Instructs SyncField to sync in game loop.
        /// </summary>
        /// <returns>self</returns>
        ISyncField InGameLoop();

        /// <summary>
        /// Adds an Action that runs after a field is synchronized.
        /// </summary>
        /// <param name="action">An action ran after a field is synchronized. Called with target and value.</param>
        /// <returns>self</returns>
        ISyncField PostApply(Action<object, object> action);

        /// <summary>
        /// Adds an Action that runs before a field is synchronized.
        /// </summary>
        /// <param name="action">An action ran before a field is synchronized. Called with target and value.</param>
        /// <returns>self</returns>
        ISyncField PreApply(Action<object, object> action);

        /// <summary>
        /// Instructs SyncField to use a buffer instead of syncing instantly (when <see cref="MP.WatchEnd"/> is called).
        /// </summary>
        /// <returns>self</returns>
        ISyncField SetBufferChanges();

        /// <summary>
        /// Instructs SyncField to synchronize only in debug mode.
        /// </summary>
        /// <returns>self</returns>
        ISyncField SetDebugOnly();

        /// <summary>
        /// Instructs SyncField to synchronize only if it's invoked by the host.
        /// </summary>
        /// <returns>self</returns>
        ISyncField SetHostOnly();

        /// <summary>
        /// 
        /// </summary>
        /// <returns>self</returns>
        ISyncField SetVersion(int version);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="target">An object of type set in the <see cref="ISyncField"/>. Set to null if you're watching a static field.</param>
        /// <param name="index">Index in the field path set in <see cref="ISyncField"/>.</param>
        /// <returns>self</returns>
        void Watch(object target = null, object index = null);

        /// <summary>
        /// Manually syncs a field.
        /// </summary>
        /// <param name="target">An object of type set in the <see cref="ISyncField"/>. Set to null if you're watching a static field.</param>
        /// <param name="value">Value to apply to the synced field.</param>
        /// <param name="index">Index in the field path set in <see cref="ISyncField"/></param>
        /// <returns><see langword="true"/> if the change should be canceled.</returns>
        bool DoSync(object target, object value, object index = null);

        string ToString();
    }

    /// <summary>
    /// ISyncCall interface.
    /// </summary>
    /// <remarks>Used internally</remarks>
    public interface ISyncCall
    {
        /// <summary>
        /// Manually calls the synced method.
        /// </summary>
        /// <param name="target">Object currently bound to that method. Null if the method is static.</param>
        /// <param name="args">Parameters to call the method with.</param>
        /// <returns><see langword="true"/> if the original call should be canceled.</returns>
        bool DoSync(object target, params object[] args);
    }

    /// <summary>
    /// SyncMethod interface.
    /// </summary>
    /// <remarks>See <see cref="SyncMethodAttribute"/>, <see cref="MP.RegisterSyncMethod(MethodInfo, SyncType[])"/> and <see cref="MP.RegisterSyncMethod(Type, string, SyncType[])"/> to see how to use it.</remarks>
    public interface ISyncMethod : ISyncCall
    {
        /// <summary>
        /// Instructs SyncMethod to cancel synchronization if any arg is null.
        /// </summary>
        /// <returns>self</returns>
        ISyncMethod CancelIfAnyArgNull();

        /// <summary>
        /// Instructs SyncMethod to cancel synchronization if no map objects were selected during call replication.
        /// </summary>
        /// <returns>self</returns>
        ISyncMethod CancelIfNoSelectedMapObjects();

        /// <summary>
        /// Instructs SyncMethod to cancel synchronization if no world objects were selected during call replication.
        /// </summary>
        /// <returns>self</returns>
        ISyncMethod CancelIfNoSelectedWorldObjects();

        /// <summary>
        /// Use parameter's type's IExposable interface to transfer its data to other clients.
        /// </summary>
        /// <remarks>IExposable is the interface used for saving data to the save which means it utilizes IExposable.ExposeData() method.</remarks>
        /// <param name="index">Index at which parameter is to be marked to expose</param>
        /// <returns>self</returns>
        ISyncMethod ExposeParameter(int index);

        /// <summary>
        /// Currently unused in the Multiplayer mod.
        /// </summary>
        /// <param name="time">Milliseconds between resends</param>
        /// <returns>self</returns>
        ISyncMethod MinTime(int time);

        /// <summary>
        /// Instructs method to send context along with the call.
        /// </summary>
        /// <remarks>Context is restored after method is called.</remarks>
        /// <param name="context">One or more context flags</param>
        /// <returns>self</returns>
        ISyncMethod SetContext(SyncContext context);

        /// <summary>
        /// Instructs SyncMethod to synchronize only in debug mode.
        /// </summary>
        /// <returns>self</returns>
        ISyncMethod SetDebugOnly();

        /// <summary>
        /// Adds an Action that runs before a call is replicated on client.
        /// </summary>
        /// <param name="action">An action ran before a call is replicated on client. Called with target and value.</param>
        /// <returns>self</returns>
        ISyncMethod SetPreInvoke(Action<object, object[]> action);

        /// <summary>
        /// Adds an Action that runs after a call is replicated on client.
        /// </summary>
        /// <param name="action">An action ran after a call is replicated on client. Called with target and value.</param>
        /// <returns>self</returns>
        ISyncMethod SetPostInvoke(Action<object, object[]> action);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="version">Handler version</param>
        /// <returns>self</returns>
        ISyncMethod SetVersion(int version);

        string ToString();
    }

    // Todo: Document
    /// <summary>
    /// Sync delegate.
    /// </summary>
    /// <remarks>See <see cref="MP.RegisterSyncDelegate(Type, string, string)"/> and <see cref="MP.RegisterSyncDelegate(Type, string, string, string[], Type[])"/> to see how to use it.</remarks>
    public interface ISyncDelegate : ISyncCall
    {
        /// <summary>
        /// Instructs ISyncDelegate to cancel synchronization except for <param name="blacklist">
        /// </summary>
        /// <returns>self</returns>
        /// <param name="blacklist">field names to be excluded</param>
        ISyncDelegate CancelIfAnyFieldNull(params string[] blacklist);

        /// <summary>
        /// Instructs ISyncDelegate to cancel synchronization except for <param name="whitelist">
        /// </summary>
        /// <returns>self</returns>
        /// <param name="whitelist">Whitelist.</param>
        ISyncDelegate CancelIfFieldsNull(params string[] whitelist);

        /// <summary>
        /// Cancels if no selected objects.
        /// </summary>
        /// <returns>self</returns>
        ISyncDelegate CancelIfNoSelectedObjects();

        /// <summary>
        /// Removes the nulls from lists.
        /// </summary>
        /// <returns>self</returns>
        /// <param name="listFields">List fields.</param>
        ISyncDelegate RemoveNullsFromLists(params string[] listFields);

        /// <summary>
        /// Sets the context.
        /// </summary>
        /// <returns>self</returns>
        /// <param name="context">Context.</param>
        ISyncDelegate SetContext(SyncContext context);

        /// <summary>
        /// Sets the debug only.
        /// </summary>
        /// <returns>self</returns>
        ISyncDelegate SetDebugOnly();

        string ToString();
    }


    /// <summary>
    /// An attribute that marks a method as a SyncWorker for a type specified in its second parameter.
    /// </summary>
    /// <remarks>
    /// Method with this attribute has to be static.
    /// </remarks>
    /// <example>
    /// <para>An implementation that manually constructs an object.</para>
    /// <code>
    ///    [SyncWorkerAttribute]
    ///    public static void MySyncWorker(SyncWorker sync, ref MyClass inst)
    ///    {
    ///        if(!sync.isWriting)
    ///            inst = new MyClass("hello");
    ///        
    ///        sync.bind(ref inst.myField);
    ///    }
    /// </code>
    /// <para>An implementation that instead of creating a new object, references its existing one which resides in MyThingComp that inherits ThingComp class.</para>
    /// <para>Subclasses of ThingComp are sent as a reference by the multiplayer mod itself.</para>
    /// <code>
    ///    [SyncWorkerAttribute]
    ///    public static void MySyncWorker(SyncWorker sync, ref MyClass inst)
    ///    {
    ///        if(!sync.isWriting)
    ///            MyThingComp parent = null;
    ///            sync.Bind(ref parent);    // Receive its parent
    ///            inst = new MyClass(parent);
    ///        else
    ///            sync.Bind(ref inst.parent);    // Send its parent
    ///        
    ///        sync.bind(ref inst.myField);
    ///    }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method)]
    public class SyncWorkerAttribute : Attribute
    {
        /// <summary>Decides if the type specified in the second parameter should also be used as a syncer for all of its subclasses.</summary>
        public bool isImplicit = false;

        /// <summary>Decides if the method should get an already constructed object in case of reading data.</summary>
        public bool shouldConstruct = false;
    }

    /// <summary>
    /// SyncWorker signature for adding new Types.
    /// </summary>
    /// <param name="obj">Target Type</param>
    /// <remarks><see cref="SyncWorkerAttribute"/> for usage examples.</remarks>
    public delegate void SyncWorkerDelegate<T>(SyncWorker sync, ref T obj);

    /// <summary>
    /// An abstract class that can be both a reader and a writer depending on implementation.
    /// </summary>
    /// <remarks>See <see cref="ISynchronizable"/> and <see cref="SyncWorkerAttribute"/> for usage examples.</remarks>
    public abstract class SyncWorker
    {
        /// <summary><see langword="true"/> if is currently writing.</summary>
        public readonly bool isWriting;

        protected SyncWorker(bool isWriting)
        {
            this.isWriting = isWriting;
        }

        /// <summary>
        /// Write the specified obj, only active during writing.
        /// </summary>
        /// <param name="obj">Object to write.</param>
        /// <typeparam name="T">Type to write.</typeparam>
        public void Write<T>(T obj) {
            if (isWriting) {
                Bind(ref obj);
            }
        }

        /// <summary>
        /// Read the specified Type from the memory stream, only active during reading.
        /// </summary>
        /// <returns>The requested Type object. Null if writing.</returns>
        /// <typeparam name="T">The Type to read.</typeparam>
        public T Read<T>() {
            T obj = default(T);

            if (isWriting) {
                return obj;
            }

            Bind(ref obj);

            return obj;
        }

        /// <summary>Reads or writes a <see cref="Type"/> referenced by <paramref name="type"/>.</summary>
        /// <typeparam name="T">Base type that <paramref name="type"/> derives from.</typeparam>
        /// <param name="type">type to bind</param>
        public abstract void BindType<T>(ref Type type);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref byte obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref sbyte obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref short obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref ushort obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref int obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref uint obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref long obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref ulong obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref float obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref double obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref string obj);

        /// <summary>Reads or writes an object referenced by <paramref name="obj"/>.</summary>
        /// <param name="obj">object to bind</param>
        public abstract void Bind(ref bool obj);

        /// <summary>
        /// Reads or writes an object referenced by <paramref name="obj"/>
        /// </summary>
        /// <remarks>Can read/write types using user defined syncers, <see cref="ISynchronizable"/>s and readers/writers implemented by the multiplayer mod.</remarks>
        /// <typeparam name="T">type of the object to bind</typeparam>
        /// <param name="obj">object to bind</param>
        public abstract void Bind<T>(ref T obj);

        /// <summary>
        /// Uses reflection to bind a field or property
        /// </summary>
        /// <param name="obj">
        /// <para>object where the field or property can be found</para>
        /// <para>if null, <paramref name="name"/> will point at field from the global namespace</para>
        /// </param>
        /// <param name="name">path to the field or property</param>
        public abstract void Bind(object obj, string name);

        /// <summary>
        /// Reads or writes an object inheriting <see cref="ISynchronizable"/> interface. 
        /// </summary>
        /// <remarks>Does not create a new object.</remarks>
        /// <param name="obj">object to bind</param>
        public void Bind(ref ISynchronizable obj)
        {
            obj.Sync(this);
        }
    }

    /// <summary>
    /// An interface that allows syncing objects that inherit it.
    /// </summary>
    public interface ISynchronizable
    {
        /// <summary>
        /// An entry point that is used when object is to be read/written.
        /// </summary>
        /// <remarks>
        /// <para>Requires a default constructor that takes no parameters.</para>
        /// <para>Check <see cref="SyncWorkerAttribute"/> to see how to make a syncer that allows for a manual object construction.</para>
        /// </remarks>
        /// <param name="sync">A SyncWorker that will read/write data bound with Bind methods.</param>
        /// <example>
        /// <para>A simple implementation that binds object's fields x, y, z for reading/writing.</para>
        /// <code>
        /// public void Sync(SyncWorker sync)
        ///    {
        ///        sync.Bind(ref this.x);
        ///        sync.Bind(ref this.y);
        ///        sync.Bind(ref this.z);
        ///    }
        /// </code>
        /// 
        /// <para>An implementation that sends field a, but saves it back into field b when it's received.</para>
        /// <code>
        /// public void Sync(SyncWorker sync)
        ///    {
        ///        if(sync.isWriting)
        ///            sync.Bind(ref this.a);
        ///        else
        ///            sync.Bind(ref this.b);
        ///    }
        /// </code>
        /// </example>
        void Sync(SyncWorker sync);
    }

    public interface IAPI
    {
        bool IsHosting { get; }
        bool IsInMultiplayer { get; }
        string PlayerName { get; }

        void WatchBegin();
        void Watch(Type targetType, string fieldName, object target = null, object index = null);
        void Watch(object target, string fieldName, object index = null);
        void Watch(string memberPath, object target = null, object index = null);
        void WatchEnd();

        void RegisterAll(Assembly assembly);

        ISyncField RegisterSyncField(Type targetType, string memberPath);
        ISyncField RegisterSyncField(FieldInfo field);

        ISyncMethod RegisterSyncMethod(Type type, string methodOrPropertyName, SyncType[] argTypes = null);
        ISyncMethod RegisterSyncMethod(MethodInfo method, SyncType[] argTypes);

        ISyncDelegate RegisterSyncDelegate(Type type, string nestedType, string method);
        ISyncDelegate RegisterSyncDelegate(Type inType, string nestedType, string methodName, string[] fields, Type[] args = null);

        void RegisterSyncWorker<T>(SyncWorkerDelegate<T> syncWorkerDelegate, Type targetType = null, bool isImplicit = false, bool shouldConstruct = false);
    }
}
