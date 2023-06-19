using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client.Networking
{
    public interface IConnectionStatusListener
    {
        void Connected();
        void Disconnected();
    }

    public static class ConnectionStatusListeners
    {
        private static IEnumerable<IConnectionStatusListener> All
        {
            get
            {
                if (Find.WindowStack != null)
                    foreach (Window window in Find.WindowStack.Windows.ToList())
                        if (window is IConnectionStatusListener listener)
                            yield return listener;

                if (Multiplayer.Client?.StateObj is IConnectionStatusListener state)
                    yield return state;

                if (Multiplayer.session != null)
                    yield return Multiplayer.session;
            }
        }

        public static void TryNotifyAll_Connected()
        {
            foreach (var listener in All)
            {
                try
                {
                    listener.Connected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
        }

        public static void TryNotifyAll_Disconnected()
        {
            foreach (var listener in All)
            {
                try
                {
                    listener.Disconnected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
        }
    }

}
