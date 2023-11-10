using Multiplayer.API;
using Multiplayer.Client.Persistent;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Experimental;

/// <summary>
/// <para>Used by Multiplayer's session manager to allow for creation of blocking dialogs, while (in case of async time) only pausing specific maps.</para>
/// <para>Sessions will be reset/reloaded during reloading - to prevent it, inherit from <see cref="ExposableSession"/> or <see cref="SemiPersistentSession"/>.</para>
/// <para>You should avoid implementing this interface directly, instead opting into inheriting <see cref="Session"/> for greater compatibility.</para>
/// </summary>
public abstract class Session
{
    // Use internal to prevent mods from easily modifying it?
    protected int sessionId;
    // Should it be virtual?
    /// <summary>
    /// <para>Used for syncing session across players by assigning them IDs, similarly to how every <see cref="Thing"/> receives an ID.</para>
    /// <para>Automatically applied by the session manager</para>
    /// <para>If inheriting <see cref="Session"/> you don't have to worry about this property.</para>
    /// </summary>
    public int SessionId
    {
        get => sessionId;
        set => sessionId = value;
    }

    /// <summary>
    /// Used by the session manager while joining the game - if it returns <see langword="false"/> it'll get removed.
    /// </summary>
    public virtual bool IsSessionValid => true;

    /// <summary>
    /// Called once the sessions has been added to the list of active sessions. Can be used for initialization.
    /// </summary>
    /// <remarks>In case of <see cref="ISessionWithCreationRestrictions"/>, this will only be called if successfully added.</remarks>
    public virtual void PostAddSession()
    {
    }

    /// <summary>
    /// Called once the sessions has been removed to the list of active sessions. Can be used for cleanup.
    /// </summary>
    public virtual void PostRemoveSession()
    {
    }

    protected static void SwitchToMapOrWorld(Map map)
    {
        if (map == null)
        {
            Find.World.renderer.wantedMode = WorldRenderMode.Planet;
        }
        else
        {
            if (WorldRendererUtility.WorldRenderedNow) CameraJumper.TryHideWorld();
            Current.Game.CurrentMap = map;
        }
    }

    /// <summary>
    /// The map this session is used by or <see langword="null"/> in case of global sessions.
    /// </summary>
    public abstract Map Map { get; }

    /// <summary>
    /// <para>Called when checking ticking and if any session returns <see langword="true"/> - it'll force pause the map/game.</para>
    /// <para>In case of local (map) sessions, it'll only be called by the current map. In case of global (world) sessions, it'll be called by the world and each map.</para>
    /// </summary>
    /// <param name="map">Current map (when checked from local session manager) or <see langword="null"/> (when checked from local session manager).</param>
    /// <remarks>If there are multiple sessions active, this method is not guaranteed to run if a session before this one returned <see langword="true"/>.</remarks>
    /// <returns><see langword="true"/> if the session should pause the map/game, <see langword="false"/> otherwise.</returns>
    public abstract bool IsCurrentlyPausing(Map map);

    /// <summary>
    /// Called when a session is active, and if any session returns a non-null value, a button will be displayed which will display all options.
    /// </summary>
    /// <param name="entry">Currently processed colonist bar entry. Will be called once per <see cref="ColonistBar.Entry.group"/>.</param>
    /// <returns>Menu option that will be displayed when the session is active. Can be <see langword="null"/>.</returns>
    public abstract FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry);
}

/// <summary>
/// <para>Sessions inheriting from this class contain persistent data.</para>
/// <para>When inheriting from this class, remember to call <c>base.ExposeData()</c> to let it handle <see cref="Session.SessionId"/></para>
/// <para>Persistent data:</para>
/// <list type="bullet">
///     <item>Serialized into XML using RimWorld's Scribe system</item>
///     <item>Save-bound: survives a server restart</item>
/// </list>
/// </summary>
public abstract class ExposableSession : Session, IExposable
{
    // For subclasses implementing IExplosableSession
    public virtual void ExposeData()
    {
        Scribe_Values.Look(ref sessionId, "sessionId");
    }
}

/// <summary>
/// <para>Sessions implementing this interface consist of semi-persistent data.</para>
/// <para>Semi-persistent data:</para>
/// <list type="bullet">
///     <item>Serialized into binary using the Sync system</item>
///     <item>Session-bound: survives a reload, lost when the server is closed</item>
/// </list>
/// </summary>
public abstract class SemiPersistentSession : Session
{
    /// <summary>
    /// Writes/reads the data used by this session.
    /// </summary>
    /// <param name="sync">Sync worker used for writing/reading the data.</param>
    public abstract void Sync(SyncWorker sync);
}
