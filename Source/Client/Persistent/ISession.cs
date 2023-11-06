using Multiplayer.API;
using Multiplayer.Client.Experimental;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent
{
    /// <summary>
    /// <para>Used by Multiplayer's session manager to allow for creation of blocking dialogs, while (in case of async time) only pausing specific maps.</para>
    /// <para>Sessions will be reset/reloaded during reloading - to prevent it, implement <see cref="IExposableSession"/> or <see cref="ISemiPersistentSession"/>.</para>
    /// <para>You should avoid implementing this interface directly, instead opting into inheriting <see cref="Session"/> for greater compatibility.</para>
    /// </summary>
    public interface ISession
    {
        /// <summary>
        /// The map this session is used by or <see langword="null"/> in case of global sessions.
        /// </summary>
        Map Map { get; }

        /// <summary>
        /// <para>Used for syncing session across players by assigning them IDs, similarly to how every <see cref="Thing"/> receives an ID.</para>
        /// <para>Automatically applied by the session manager</para>
        /// <para>If inheriting <see cref="Session"/> you don't have to worry about this property.</para>
        /// </summary>
        int SessionId { get; set; }

        /// <summary>
        /// Used by the session manager while joining the game - if it returns <see langword="false"/> it'll get removed.
        /// </summary>
        bool IsSessionValid { get; }

        /// <summary>
        /// <para>Called when checking ticking and if any session returns <see langword="true"/> - it'll force pause the map/game.</para>
        /// <para>In case of local (map) sessions, it'll only be called by the current map. In case of global (world) sessions, it'll be called by the world and each map.</para>
        /// </summary>
        /// <param name="map">Current map (when checked from local session manager) or <see langword="null"/> (when checked from local session manager).</param>
        /// <remarks>If there are multiple sessions active, this method is not guaranteed to run if a session before this one returned <see langword="true"/>.</remarks>
        /// <returns><see langword="true"/> if the session should pause the map/game, <see langword="false"/> otherwise.</returns>
        bool IsCurrentlyPausing(Map map);

        /// <summary>
        /// Called when a session is active, and if any session returns a non-null value, a button will be displayed which will display all options.
        /// </summary>
        /// <param name="entry">Currently processed colonist bar entry. Will be called once per <see cref="ColonistBar.Entry.group"/>.</param>
        /// <returns>Menu option that will be displayed when the session is active. Can be <see langword="null"/>.</returns>
        FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry);

        /// <summary>
        /// Called once the sessions has been added to the list of active sessions. Can be used for initialization.
        /// </summary>
        /// <remarks>In case of <see cref="ISessionWithCreationRestrictions"/>, this will only be called if successfully added.</remarks>
        void PostAddSession();

        /// <summary>
        /// Called once the sessions has been removed to the list of active sessions. Can be used for cleanup.
        /// </summary>
        void PostRemoveSession();
    }

    // todo unused for now
    public interface IPausingWithDialog
    {
        void OpenWindow(bool sound = true);
    }

    /// <summary>
    /// <para>Required by sessions dealing with transferables, like trading or caravan forming. By implementing this interface, Multiplayer will handle majority of syncing of changes in transferables.</para>
    /// <para>When drawing the dialog tied to this session, you'll have to set [REF TO SETTER/METHOD] to the proper session, and set it to null once done.</para>
    /// </summary>
    /// <remarks>For safety, make sure to set [REF TO SETTER/METHOD] in <see langword="try"/> and unset in <see langword="finally"/>.</remarks>
    /// TODO: Replace [REF TO SETTER/METHOD] with actual ref in API
    public interface ISessionWithTransferables : ISession
    {
        /// <summary>
        /// Used when syncing data across players, specifically to retrieve <see cref="Transferable"/> based on the <see cref="Thing"/> it has.
        /// </summary>
        /// <param name="thingId"><see cref="Thing.thingIDNumber"/> of the <see cref="Thing"/>.</param>
        /// <returns><see cref="Transferable"/> which corresponds to a <see cref="Thing"/> with specific <see cref="Thing.thingIDNumber"/>.</returns>
        Transferable GetTransferableByThingId(int thingId);

        /// <summary>
        /// Called when the count in a specific <see cref="Transferable"/> was changed.
        /// </summary>
        /// <param name="tr">Transferable whose count was changed.</param>
        void Notify_CountChanged(Transferable tr);
    }

    /// <summary>
    /// <para>Sessions implementing this interface consist of persistent data.</para>
    /// <para>When inheriting from <see cref="Session"/>, remember to call <c>base.ExposeData()</c> to let it handle <see cref="ISession.SessionId"/></para>
    /// <para>Persistent data:</para>
    /// <list type="bullet">
    ///     <item>Serialized into XML using RimWorld's Scribe system</item>
    ///     <item>Save-bound: survives a server restart</item>
    /// </list>
    /// </summary>
    /// <remarks>A class should NOT implement both this and <see cref="ISemiPersistentSession"/> - it'll be treated as if only implementing this interface.</remarks>
    public interface IExposableSession : ISession, IExposable
    {
    }

    /// <summary>
    /// <para>Sessions implementing this interface consist of semi-persistent data.</para>
    /// <para>Semi-persistent data:</para>
    /// <list type="bullet">
    ///     <item>Serialized into binary using the Sync system</item>
    ///     <item>Session-bound: survives a reload, lost when the server is closed</item>
    /// </list>
    /// </summary>
    /// <remarks>A class should NOT implement both this and <see cref="IExposableSession"/> - it'll be treated as if only implementing <see cref="IExposableSession"/>.</remarks>
    public interface ISemiPersistentSession : ISession
    {
        void Write(SyncWorker sync);

        void Read(SyncWorker sync);
    }

    /// <summary>
    /// Interface used by sessions that have restrictions based on other existing sessions, for example limiting them to only 1 session of specific type.
    /// </summary>
    public interface ISessionWithCreationRestrictions
    {
        /// <summary>
        /// <para>Method used to check if the current session can be created by checking other <see cref="ISession"/>.</para>
        /// <para>Only sessions in the current context are checked (local map sessions or global sessions).</para>
        /// </summary>
        /// <param name="other">The other session the current one is checked against. Can be of different type.</param>
        /// <remarks>Currently only the current class checks against the existing ones - the existing classed don't check against this one.</remarks>
        /// <returns><see langword="true"/> if the current session should be created, <see langword="false"/> otherwise</returns>
        bool CanExistWith(ISession other);
    }

    /// <summary>
    /// Used by sessions that are are required to tick together with the map/world.
    /// </summary>
    public interface ITickingSession
    {
        /// <summary>
        /// Called once per session when the map (for local sessions) or the world (for global sessions) is ticking.
        /// </summary>
        /// <remarks>The sessions are iterated over backwards using a for loop, so it's safe for them to remove themselves from the session manager.</remarks>
        void Tick();
    }
}
