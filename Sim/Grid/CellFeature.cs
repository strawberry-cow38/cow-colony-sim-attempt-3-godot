namespace CowColonySim.Sim.Grid;

public enum CellFeature : byte
{
    Plains,
    Hills,
    Mountains,
    Lake,
}

public static class FeatureResolver
{
    // Cell feature is a pure function of (seed, cellKey). No storage needed —
    // any system can re-derive the feature at any time.
    public static CellFeature Pick(int seed, CellKey key)
    {
        uint h = Hash((uint)seed, key.X, key.Z);
        int roll = (int)(h % 100u);
        if (roll < 35) return CellFeature.Plains;
        if (roll < 65) return CellFeature.Hills;
        if (roll < 85) return CellFeature.Mountains;
        return CellFeature.Lake;
    }

    private static uint Hash(uint seed, int x, int z)
    {
        unchecked
        {
            uint h = seed;
            h = (h ^ (uint)x) * 0x85ebca6bu;
            h = (h ^ (uint)z) * 0xc2b2ae35u;
            h ^= h >> 13;
            h *= 0x27d4eb2fu;
            h ^= h >> 16;
            return h;
        }
    }
}

public sealed class FeatureNoises
{
    public readonly FastNoiseLite Plains;
    public readonly FastNoiseLite Hills;
    public readonly FastNoiseLite Mountains;
    public readonly FastNoiseLite Lake;
    public readonly FastNoiseLite Cubby;

    public FeatureNoises(int seed)
    {
        Plains    = Make(seed + 1, 0.020f, 2);
        Hills     = Make(seed + 2, 0.010f, 4);
        Mountains = Make(seed + 3, 0.006f, 5);
        Lake      = Make(seed + 4, 0.030f, 2);
        // Low-frequency mask for flat cubbies / outpost pockets.
        Cubby     = Make(seed + 5, 0.035f, 2);
    }

    private static FastNoiseLite Make(int seed, float freq, int oct)
    {
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFrequency(freq);
        n.SetFractalType(FastNoiseLite.FractalType.FBm);
        n.SetFractalOctaves(oct);
        n.SetFractalLacunarity(2.0f);
        n.SetFractalGain(0.5f);
        return n;
    }

    public float SampleHeight(CellFeature f, int x, int z)
    {
        switch (f)
        {
            case CellFeature.Plains:
                return 3f + (Plains.GetNoise(x, z) + 1f) * 0.5f * 7f;
            case CellFeature.Hills:
                return 4f + (Hills.GetNoise(x, z) + 1f) * 0.5f * 36f;
            case CellFeature.Mountains:
            {
                var n = Mountains.GetNoise(x, z);
                var ridged = 1f - System.MathF.Abs(n);
                ridged *= ridged;
                // 6..100 raw ridged height — terracing applied post-bilerp.
                return 6f + ridged * 94f;
            }
            case CellFeature.Lake:
                return 1f + (Lake.GetNoise(x, z) + 1f) * 0.5f * 1.5f;
            default:
                return 1f;
        }
    }
}
