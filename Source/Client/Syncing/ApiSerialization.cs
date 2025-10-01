using System;
using System.Reflection;
using Multiplayer.API;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client;

public static class ApiSerialization
{
    public static Type[] syncSimples;
    public static Type[] sessions;

    public static void Init()
    {
        syncSimples = TypeCache.AllInterfaceImplementationsOrdered(typeof(ISyncSimple));
        sessions = TypeCache.AllSubclassesNonAbstractOrdered(typeof(Session));

        foreach (var sessionType in sessions)
        {
            // The docs don't require the Session's ctor(Map) to be public, and it's not required for an ExposableSession.
            // Only a SemiPersistentSession requires for the constructor to be public
            if (sessionType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(Map)], []) == null)
            {
                // Required by the docs of the Session type. Further checks are just more specific variations of this
                // check.
                Log.Error(
                    $"[Multiplayer] Session type {sessionType.FullName} is invalid!\n" +
                    $"It does not have a constructor with a single parameter of type {nameof(Map)}. This will cause " +
                    $"issues with the session");
                continue;
            }

            try
            {
                if (typeof(SemiPersistentSession).IsAssignableFrom(sessionType))
                {
                    // Used by SessionManager.ReadSessionData
                    Activator.CreateInstance(sessionType, [null]);
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    $"[Multiplayer] Session type {sessionType.FullName} is invalid!\n" +
                    $"It cannot be instantiated with a single null parameter. This will cause issues when the " +
                    $"session is attached to the world (as opposed to a map). This is likely caused by another " +
                    $"single-param constructor which cannot be differentiated from the other constructor when the " +
                    $"value passed is null. In that case consider creating a static factory method instead.\n\n{e}");
            }

            try
            {
                if (typeof(ExposableSession).IsAssignableFrom(sessionType))
                {
                    // Used by SessionManager.ExposeSessions (through Scribe_Deep.Look)
                    ScribeExtractor.CreateInstance(sessionType, [null]);
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    $"[Multiplayer] Session type {sessionType.FullName} is invalid!\n" +
                    $"It cannot be instantiated with a single null parameter. This will cause issues when the " +
                    $"session is attached to the world (as opposed to a map). This is likely caused by another " +
                    $"single-param constructor which cannot be differentiated from the other constructor when the " +
                    $"value passed is null. In that case consider creating a static factory method instead.\n\n{e}");
            }
        }
    }
}
