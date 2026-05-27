using System;

namespace Multiplayer.Client
{
    public static class CacheForReloading
    {
        public static ReloadCacheScope Begin(bool enabled)
        {
            // Fast reload caches are intentionally disabled for RimWorld 1.6.
            // The previous MapDrawer and WorldGrid caches reused object graphs that
            // now own load references, native arrays, draw layers, and map-local state.
            // Keep this scope as the single place to add future proven-safe caches.
            return default;
        }

        public readonly struct ReloadCacheScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
