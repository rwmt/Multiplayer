using Multiplayer.API;
using Multiplayer.Common;
using System;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client
{
    public class SyncField : SyncHandler, ISyncField
    {
        public readonly Type targetType;
        public readonly string memberPath;
        public SyncType fieldType;
        public readonly Type indexType;

        public bool bufferChanges;
        public bool inGameLoop;

        private bool cancelIfValueNull;

        private Action<object, object> preApply;
        private Action<object, object> postApply;

        public SyncField(Type targetType, string memberPath)
        {
            this.targetType = targetType;
            this.memberPath = targetType + "/" + memberPath;
            fieldType = MpReflection.PathType(this.memberPath);
            indexType = MpReflection.IndexType(this.memberPath);
        }

        /// <summary>
        /// Returns whether the original should be cancelled
        /// </summary>
        public bool DoSync(object target, object value, object index = null)
        {
            if (!(inGameLoop || Multiplayer.ShouldSync))
                return false;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.Log.Node(ToString());

            writer.WriteInt32(syncId);

            int mapId = ScheduledCommand.Global;
            if (targetType != null)
            {
                SyncSerialization.WriteSyncObject(writer, target, targetType);
                if (context.map != null)
                    mapId = context.map.uniqueID;
            }

            SyncSerialization.WriteSyncObject(writer, value, fieldType);
            if (indexType != null)
                SyncSerialization.WriteSyncObject(writer, index, indexType);

            writer.Log.Node($"Map id: {mapId}");
            Multiplayer.WriterLog.AddCurrentNode(writer);

            SendSyncCommand(mapId, writer);

            return true;
        }

        public bool DoSyncCatch(object target, object value, object index = null)
        {
            try
            {
                DoSync(target, value, index);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Error syncing field {this}: {e}");
                return false;
            }
        }

        public override void Handle(ByteReader data)
        {
            object target = null;
            if (targetType != null)
            {
                target = SyncSerialization.ReadSyncObject(data, targetType);
                if (target == null)
                    return;
            }

            object value = SyncSerialization.ReadSyncObject(data, fieldType);
            if (cancelIfValueNull && value == null)
                return;

            object index = null;
            if (indexType != null)
                index = SyncSerialization.ReadSyncObject(data, indexType);

            preApply?.Invoke(target, value);

            MpLog.Debug($"Set {memberPath} in {target} to {value}, map {data.MpContext().map}, index {index}");
            MpReflection.SetValue(target, memberPath, value, index);

            postApply?.Invoke(target, value);
        }

        public void Watch(object target = null, object index = null)
        {
            if (!(inGameLoop || Multiplayer.ShouldSync))
                return;

            object value;

            if (bufferChanges && SyncFieldUtil.bufferedChanges[this].TryGetValue(new(target, index), out BufferData cached))
            {
                value = cached.toSend;
                target.SetPropertyOrField(memberPath, value, index);
            }
            else
            {
                value = SyncFieldUtil.SnapshotValueIfNeeded(this, target.GetPropertyOrField(memberPath, index));
            }

            SyncFieldUtil.StackPush(this, target, value, index);
        }

        public ISyncField SetVersion(int version)
        {
            this.version = version;
            return this;
        }

        public ISyncField PreApply(Action<object, object> action)
        {
            preApply = action;
            return this;
        }

        public ISyncField PostApply(Action<object, object> action)
        {
            postApply = action;
            return this;
        }

        public ISyncField SetBufferChanges()
        {
            SyncFieldUtil.bufferedChanges[this] = new();
            Sync.bufferedFields.Add(this);
            bufferChanges = true;
            return this;
        }

        public ISyncField InGameLoop()
        {
            inGameLoop = true;
            return this;
        }

        public ISyncField CancelIfValueNull()
        {
            cancelIfValueNull = true;
            return this;
        }

        public ISyncField SetDebugOnly()
        {
            debugOnly = true;
            return this;
        }

        public ISyncField SetHostOnly()
        {
            hostOnly = true;
            return this;
        }

        public ISyncField ExposeValue()
        {
            fieldType.expose = true;
            return this;
        }

        public override void Validate()
        {
            ValidateType("Target type", targetType);
            ValidateType("Field type", fieldType);
            ValidateType("Index type", indexType);
        }

        public override string ToString()
        {
            return $"SyncField {memberPath}";
        }
    }

}
