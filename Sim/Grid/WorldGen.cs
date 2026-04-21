using System.Threading.Tasks;

namespace CowColonySim.Sim.Grid;

public static class WorldGen
{
    public const int DefaultMinHeight = 1;
    public const int DefaultMaxHeight = 51;
    public const float DefaultFrequency = 0.02f;

    public static int Generate(TileWorld tiles, int seed, int sizeX, int sizeZ,
        int minHeight = DefaultMinHeight, int maxHeight = DefaultMaxHeight,
        float frequency = DefaultFrequency)
    {
        var noises = new FeatureNoises(seed);
        var halfX = sizeX / 2;
        var halfZ = sizeZ / 2;
        const int cellSize = SimConstants.CellSizeTiles;
        const int cellHalf = cellSize / 2;

        var heights = new int[sizeX, sizeZ];
        Parallel.For(0, sizeX, xi =>
        {
            for (var zi = 0; zi < sizeZ; zi++)
            {
                var x = xi - halfX;
                var z = zi - halfZ;

                // 4-cell bilerp anchored on cell centers. Guarantees C0
                // continuity across cell borders because weights sum to 1
                // and each corner's feature height is a continuous global
                // noise sampled at (x, z).
                float cxf = (x - (float)cellHalf) / cellSize;
                float czf = (z - (float)cellHalf) / cellSize;
                int cx0 = (int)MathF.Floor(cxf);
                int cz0 = (int)MathF.Floor(czf);
                float fx = cxf - cx0;
                float fz = czf - cz0;
                float wx = Smooth(fx);
                float wz = Smooth(fz);

                var f00 = FeatureResolver.Pick(seed, new CellKey(cx0, cz0));
                var f10 = FeatureResolver.Pick(seed, new CellKey(cx0 + 1, cz0));
                var f01 = FeatureResolver.Pick(seed, new CellKey(cx0, cz0 + 1));
                var f11 = FeatureResolver.Pick(seed, new CellKey(cx0 + 1, cz0 + 1));

                float h00 = noises.SampleHeight(f00, x, z);
                float h10 = noises.SampleHeight(f10, x, z);
                float h01 = noises.SampleHeight(f01, x, z);
                float h11 = noises.SampleHeight(f11, x, z);

                float h = (1f - wx) * (1f - wz) * h00
                        + wx         * (1f - wz) * h10
                        + (1f - wx) * wz         * h01
                        + wx         * wz         * h11;

                int hi = (int)MathF.Round(h);
                if (hi < minHeight) hi = minHeight;
                if (hi > maxHeight) hi = maxHeight;
                heights[xi, zi] = hi;
            }
        });

        var solid = new Tile(TileKind.Solid);
        var grass = new Tile(TileKind.Floor);
        var surfaceTiles = 0;
        for (var xi = 0; xi < sizeX; xi++)
        for (var zi = 0; zi < sizeZ; zi++)
        {
            var height = heights[xi, zi];
            var x = xi - halfX;
            var z = zi - halfZ;
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

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
