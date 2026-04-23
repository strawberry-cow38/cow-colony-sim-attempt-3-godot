namespace CowColonySim.Sim.Grid;

/// <summary>
/// Global noise stack that drives <see cref="WorldGen"/>. Every layer is
/// sampled directly at (x, z) world tile coordinates — no cell-level feature
/// picks, no bilerp. Continuous coverage means terrain is seamless by
/// construction and cliffs emerge from the plateau-quantization pass rather
/// than from random high-frequency gradients.
/// </summary>
public sealed class NoiseStack
{
    public readonly FastNoiseLite Continent;
    public readonly FastNoiseLite MountainMask;
    public readonly FastNoiseLite Ridge;
    public readonly FastNoiseLite Detail;
    public readonly FastNoiseLite Lake;
    public readonly FastNoiseLite RiverSource;
    public readonly FastNoiseLite Meander;
    public readonly FastNoiseLite Temperature;
    public readonly FastNoiseLite Rainfall;
    // Smooth noise field sampled inside the biome-border band to decide,
    // tile by tile, whether the tile belongs to its own cell or the nearer
    // neighbor. Mid-freq so the resulting blend zone has visible fingers
    // interlocking between two biomes instead of tile-scale salt/pepper.
    public readonly FastNoiseLite BiomeBorder;

    public NoiseStack(int seed)
    {
        Continent    = Make(seed + 1, 0.0015f, 3, FastNoiseLite.FractalType.FBm);
        MountainMask = Make(seed + 2, 0.0025f, 2, FastNoiseLite.FractalType.FBm);
        Ridge        = Make(seed + 3, 0.006f,  4, FastNoiseLite.FractalType.Ridged);
        Detail       = Make(seed + 4, 0.04f,   2, FastNoiseLite.FractalType.FBm);
        Lake         = Make(seed + 5, 0.010f,  2, FastNoiseLite.FractalType.FBm);
        RiverSource  = Make(seed + 6, 0.003f,  2, FastNoiseLite.FractalType.FBm);
        Meander      = Make(seed + 7, 0.015f,  2, FastNoiseLite.FractalType.FBm);
        // Climate octaves are intentionally low-frequency so neighboring tiles
        // differ only by fractions of a degree / a few mm. Sharp biome seams
        // then require deliberate classification, not noise aliasing.
        Temperature  = Make(seed + 8, 0.0010f, 3, FastNoiseLite.FractalType.FBm);
        Rainfall     = Make(seed + 9, 0.0012f, 3, FastNoiseLite.FractalType.FBm);
        BiomeBorder  = Make(seed + 10, 0.08f, 2, FastNoiseLite.FractalType.FBm);
    }

    private static FastNoiseLite Make(int seed, float freq, int oct, FastNoiseLite.FractalType type)
    {
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFrequency(freq);
        n.SetFractalType(type);
        n.SetFractalOctaves(oct);
        n.SetFractalLacunarity(2.0f);
        n.SetFractalGain(0.5f);
        return n;
    }
}
