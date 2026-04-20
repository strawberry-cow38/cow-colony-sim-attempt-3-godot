namespace CowColonySim.Sim.Grid;

public static class WorldGen
{
    public const int DefaultMinHeight = 1;
    public const int DefaultMaxHeight = 6;
    public const float DefaultFrequency = 0.02f;

    public static int Generate(TileWorld tiles, int seed, int sizeX, int sizeZ,
        int minHeight = DefaultMinHeight, int maxHeight = DefaultMaxHeight,
        float frequency = DefaultFrequency)
    {
        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFrequency(frequency);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(4);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);

        var halfX = sizeX / 2;
        var halfZ = sizeZ / 2;
        var range = maxHeight - minHeight;
        var solid = new Tile(TileKind.Solid);
        var grass = new Tile(TileKind.Floor);
        var surfaceTiles = 0;
        for (var x = -halfX; x < sizeX - halfX; x++)
        for (var z = -halfZ; z < sizeZ - halfZ; z++)
        {
            var n = noise.GetNoise(x, z);
            var norm = (n + 1f) * 0.5f;
            var height = minHeight + (int)MathF.Round(norm * range);
            if (height < 1) height = 1;
            for (var y = 0; y < height - 1; y++)
            {
                tiles.Set(new TilePos(x, y, z), solid);
            }
            tiles.Set(new TilePos(x, height - 1, z), grass);
            surfaceTiles++;
        }
        return surfaceTiles;
    }

    public static int SurfaceY(TileWorld tiles, int x, int z, int maxProbe = 64)
    {
        for (var y = maxProbe; y >= 0; y--)
        {
            if (!tiles.Get(new TilePos(x, y, z)).IsEmpty) return y + 1;
        }
        return 0;
    }
}
