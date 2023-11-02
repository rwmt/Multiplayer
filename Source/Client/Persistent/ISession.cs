using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent
{
    public interface ISession
    {
        Map Map { get; }

        int SessionId { get; set; }

        bool IsSessionValid { get; }

        /// <summary>
        ///
        /// </summary>
        /// <param name="map">Current map (when checked from local session manager) or <see langword="null"/> (when checked from local session manager).</param>
        /// <returns></returns>
        bool IsCurrentlyPausing(Map map);

        FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry);

        void PostAddSession();

        void PostRemoveSession();
    }

    // todo unused for now
    public interface IPausingWithDialog
    {
        void OpenWindow(bool sound = true);
    }

    public interface ISessionWithTransferables : ISession
    {
        Transferable GetTransferableByThingId(int thingId);

        void Notify_CountChanged(Transferable tr);
    }

    public interface IExposableSession : ISession, IExposable
    {
    }

    public interface ISemiPersistentSession : ISession
    {
        void Write(SyncWorker sync);

        void Read(SyncWorker sync);
    }

    public interface ISessionWithCreationRestrictions
    {
        /// <summary>
        /// <para>Method used to check if the current session can be created by checking every other existing <see cref="ISession"/>.</para>
        /// <para>Currently only the current class checks against the existing ones - the existing classed don't check against this one.</para>
        /// </summary>
        /// <param name="other">The other session the current one is checked against. Can be of different type.</param>
        /// <returns><see langword="true"/> if the current session should be created, <see langword="false"/> otherwise</returns>
        bool CanExistWith(ISession other);
    }

    public interface ITickingSession
    {
        void Tick();
    }
}
