using System;
using System.Reflection;

namespace Multiplayer.API
{
    /// <summary>
    /// Context flags which are sent along with a command
    /// </summary>
    [Flags]
    public enum SyncContext
    {
        /// <summary>Default  value. (no context)</summary>
        None = 0,
        /// <summary>Send mouse cell context (emulates mouse position)</summary>
        MapMouseCell = 1,
        /// <summary>Send map selected context (object selected on the map)</summary>
        MapSelected = 2,
        /// <summary>Send world selected context (object selected on the world map)</summary>
        WorldSelected = 4,
        /// <summary>Send order queue context (emulates pressing KeyBindingDefOf.QueueOrder)</summary>
        QueueOrder_Down = 8,
        /// <summary>Send current map context</summary>
        CurrentMap = 16,
    }

    /// <summary>
    /// An attribute that is used to mark methods for syncing.
    /// The call will be replicated by the MPApi on all clients automatically.
    /// </summary>
    /// <example>
    /// <para>An example showing how to mark a method for syncing.</para>
    /// <code>
    /// [SyncMethod]
    /// public void MyMethod(...)
    /// {
    ///     ...
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method)]
    public class SyncMethodAttribute : Attribute
    {
        public SyncContext context;

        /// <summary>Instructs SyncMethod to cancel synchronization if any arg is null (see <see cref="ISyncMethod.CancelIfAnyArgNull"/>).</summary>
        public bool cancelIfAnyArgNull = false;

        /// <summary>Instructs SyncMethod to cancel synchronization if no map objects were selected during the call (see <see cref="ISyncMethod.CancelIfNoSelectedMapObjects"/>).</summary>
        public bool cancelIfNoSelectedMapObjects = false;

        /// <summary>Instructs SyncMethod to cancel synchronization if no world objects were selected during call replication(see <see cref="ISyncMethod.CancelIfNoSelectedWorldObjects"/>).</summary>
        public bool cancelIfNoSelectedWorldObjects = false;

        /// <summary>Instructs SyncMethod to synchronize only in debug mode (see <see cref="ISyncMethod.SetDebugOnly"/>).</summary>
        public bool debugOnly = false;

        /// <summary>A list of types to expose (see <see cref="ISyncMethod.ExposeParameter"/>)</summary>
        public int[] exposeParameters;

        /// <param name="context">Context</param>
        public SyncMethodAttribute(SyncContext context = SyncContext.None)
        {
            this.context = context;
        }
    }

    /// <summary>
    /// An attribute that is used to mark fields for syncing.
    /// It will be Watched for changes by the MPApi when instructed.
    /// </summary>
    /// <example>
    /// <para>An example showing how to mark a field for syncing.</para>
    /// <code>
    /// [SyncField]
    /// public class MyClass
    /// {
    ///     [SyncField]
    ///     bool myField;
    /// 
    ///     ...
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public class SyncFieldAttribute : Attribute
    {
        public SyncContext context;

        /// <summary>Instructs SyncField to cancel synchronization if the value of the member it's pointing at is null.</summary>
        public bool cancelIfValueNull = false;

        /// <summary>Instructs SyncField to sync in game loop.</summary>
        public bool inGameLoop = false;

        /// <summary>Instructs SyncField to use a buffer instead of syncing instantly (when <see cref="MP.WatchEnd"/> is called).</summary>
        public bool bufferChanges = true;

        /// <summary>Instructs SyncField to synchronize only in debug mode.</summary>
        public bool debugOnly = false;

        /// <summary>Instructs SyncField to synchronize only if it's invoked by the host.</summary>
        public bool hostOnly = false;

        /// <summary></summary>
        public int version;

        /// <param name="context">Context</param>
        public SyncFieldAttribute(SyncContext context = SyncContext.None)
        {
            this.context = context;
        }
    }

    public struct SyncType
    {
        public readonly Type type;
        public bool expose;
        public bool contextMap;

        public SyncType(Type type)
        {
            this.type = type;
            this.expose = false;
            contextMap = false;
        }

        public static implicit operator SyncType(ParameterInfo param)
        {
            return new SyncType(param.ParameterType) { /*expose = param.HasAttribute<SyncExpose>(), contextMap = param.HasAttribute<SyncContextMap>()*/ };
        }

        public static implicit operator SyncType(Type type)
        {
            return new SyncType(type);
        }
    }
}