using Multiplayer.API;
using Multiplayer.Client.Experimental;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent
{
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
    public interface ISessionWithTransferables
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
        bool CanExistWith(Session other);
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
