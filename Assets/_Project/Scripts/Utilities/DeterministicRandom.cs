namespace RallyGame.Utilities
{
    /// Seeded RNG used for weekly dealer stock and rival times. Deterministic from
    /// (week, salt) so the same week regenerates identically after a save/load.
    public struct DeterministicRandom
    {
        private uint state;

        public DeterministicRandom(int seed, string salt = "")
        {
            unchecked
            {
                uint h = 2166136261u ^ (uint)seed;
                foreach (char c in salt) { h ^= c; h *= 16777619u; }
                state = h == 0u ? 1u : h;
            }
        }

        public uint NextUInt()
        {
            // xorshift32
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return state;
        }

        public float Value01() => (NextUInt() & 0xFFFFFF) / 16777215f;
        public int Range(int minInclusive, int maxExclusive)
            => minInclusive + (int)(Value01() * (maxExclusive - minInclusive - 0.0001f));
        public float Range(float min, float max) => min + Value01() * (max - min);
    }
}
