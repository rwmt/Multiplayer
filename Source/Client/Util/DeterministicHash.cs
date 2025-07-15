namespace Multiplayer.Client.Util
{
    public static class DeterministicHash
    {
        public const int DefaultSeed = 352654597;
        public const int DefaultSeed2 = 1566083941;

        // 3-value combiner  ──────────────────────────────────────
        public static int HashCombineInt(int v1, int v2, int v3)
        {
            unchecked
            {
                int h = DefaultSeed;
                h = ((h << 5) + h + (h >> 27)) ^ v1;
                h = ((h << 5) + h + (h >> 27)) ^ v2;
                h = ((h << 5) + h + (h >> 27)) ^ v3;
                return h;
            }
        }

        // 5-value combiner  ──────────────────────────────────────
        public static int HashCombineInt(int v1, int v2, int v3, int v4, int v5)
        {
            unchecked
            {
                int h1 = DefaultSeed;
                int h2 = h1;

                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v1;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v2;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v3;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v4;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v5;

                return h1 + h2 * DefaultSeed2;
            }
        }

        // 6-value combiner  ──────────────────────────────────────
        public static int HashCombineInt(int v1, int v2, int v3, int v4, int v5, int v6)
        {
            unchecked
            {
                int h1 = DefaultSeed;
                int h2 = h1;

                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v1;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v2;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v3;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v4;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v5;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v6;

                return h1 + h2 * DefaultSeed2;
            }
        }

        // 7-value combiner ─────────────────────────────────────────
        public static int HashCombineInt(int v1, int v2, int v3, int v4, int v5, int v6, int v7)
        {
            unchecked
            {
                int h1 = DefaultSeed;
                int h2 = h1;

                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v1;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v2;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v3;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v4;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v5;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v6;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v7;

                return h1 + h2 * DefaultSeed2;
            }
        }

        // 8-value combiner ─────────────────────────────────────────
        public static int HashCombineInt(int v1, int v2, int v3, int v4, int v5, int v6, int v7, int v8)
        {
            unchecked
            {
                int h1 = DefaultSeed;
                int h2 = h1;

                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v1;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v2;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v3;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v4;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v5;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v6;
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ v7;
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ v8;

                return h1 + h2 * DefaultSeed2;
            }
        }
    }
}
