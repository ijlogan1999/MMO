using System;

namespace Economy.Core.Random
{
    /// <summary>
    /// Deterministic random source (xoshiro256**, seeded with SplitMix64).
    /// System.Random is not guaranteed to give the same sequence across .NET
    /// versions or Unity, so every roll in the economy goes through this type.
    /// The same seed always replays the same run, on any platform.
    /// </summary>
    public sealed class SimRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        public SimRandom(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix64(ref x);
            _s1 = SplitMix64(ref x);
            _s2 = SplitMix64(ref x);
            _s3 = SplitMix64(ref x);
        }

        /// <summary>
        /// A new, independent stream derived from this one's seed state and a
        /// stream id, so subsystems (agents, stabilizer, scenarios) do not shift
        /// each other's rolls when one of them draws more numbers.
        /// </summary>
        public SimRandom Fork(ulong streamId)
        {
            ulong mixed = _s0 ^ (streamId * 0x9E3779B97F4A7C15UL);
            return new SimRandom(mixed);
        }

        public ulong NextULong()
        {
            ulong result = RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range is empty.");
            ulong range = (ulong)((long)maxExclusive - minInclusive);
            // Rejection sampling removes modulo bias.
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
            ulong r;
            do { r = NextULong(); } while (r >= limit);
            return (int)((long)minInclusive + (long)(r % range));
        }

        /// <summary>True with the given probability (0 to 1).</summary>
        public bool Chance(double probability) => NextDouble() < probability;

        /// <summary>Normally distributed value (Box-Muller).</summary>
        public double NextGaussian(double mean, double stdDev)
        {
            double u1 = 1.0 - NextDouble(); // (0, 1]
            double u2 = NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + z * stdDev;
        }

        private static ulong SplitMix64(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
    }
}
