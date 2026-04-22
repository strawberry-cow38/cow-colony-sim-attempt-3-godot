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

    public NoiseStack(int seed)
    {
        Continent    = Make(seed + 1, 0.0015f, 3, FastNoiseLite.FractalType.FBm);
        MountainMask = Make(seed + 2, 0.0025f, 2, FastNoiseLite.FractalType.FBm);
        Ridge        = Make(seed + 3, 0.006f,  4, FastNoiseLite.FractalType.Ridged);
        Detail       = Make(seed + 4, 0.04f,   2, FastNoiseLite.FractalType.FBm);
        Lake         = Make(seed + 5, 0.010f,  2, FastNoiseLite.FractalType.FBm);
        RiverSource  = Make(seed + 6, 0.003f,  2, FastNoiseLite.FractalType.FBm);
        Meander      = Make(seed + 7, 0.012f,  2, FastNoiseLite.FractalType.FBm);
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
