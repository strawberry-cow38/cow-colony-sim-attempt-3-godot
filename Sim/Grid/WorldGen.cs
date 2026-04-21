using System.Threading.Tasks;

namespace CowColonySim.Sim.Grid;

public static class WorldGen
{
    public const int DefaultMinHeight = 1;
    public const int DefaultMaxHeight = 103;
    public const float DefaultFrequency = 0.02f;

    // Plateau terrace for mountain peaks. Above PlateauStartTiles the height
    // snaps to PlateauStepTiles multiples with a short ramp on the top
    // PlateauRampFrac of each step so successive tiers read as flat-topped
    // plateaus rather than a smooth dome. Gives buildable tops atop peaks.
    private const float PlateauStartTiles = 40f;
    private const float PlateauStepTiles = 10f;
    private const float PlateauRampFrac = 0.15f;

    // Cubby: shallow flat pocket anywhere on the map. Anywhere the cubby
    // noise exceeds the threshold the local surface is forced to a flat
    // depressed level, carving hideable outpost nooks into hills/mountains.
    private const float CubbyThreshold = 0.55f;
    private const float CubbyBlendBand = 0.15f;
    private const float CubbyDepthTiles = 6f;

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

                h = Terrace(h);

                // Cubby pass. noise in [-1, 1]; above threshold force a flat
                // depressed plateau. Blend band keeps edges walkable instead
                // of a sharp wall.
                var cubbyN = (noises.Cubby.GetNoise(x, z) + 1f) * 0.5f;
                if (cubbyN > CubbyThreshold)
                {
                    var floor = MathF.Max(minHeight + 1f, h - CubbyDepthTiles);
                    var t = Smooth(MathF.Min(1f, (cubbyN - CubbyThreshold) / CubbyBlendBand));
                    h = h + (floor - h) * t;
                }

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

    public static int SurfaceY(TileWorld tiles, int x, int z, int maxProbe = 128)
    {
        for (var y = maxProbe; y >= 0; y--)
        {
            if (!tiles.Get(new TilePos(x, y, z)).IsEmpty) return y + 1;
        }
        return 0;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Terrace(float h)
    {
        if (h <= PlateauStartTiles) return h;
        var over = h - PlateauStartTiles;
        var band = MathF.Floor(over / PlateauStepTiles) * PlateauStepTiles;
        var frac = (over - band) / PlateauStepTiles;
        var rampStart = 1f - PlateauRampFrac;
        var ramp = frac <= rampStart
            ? 0f
            : Smooth((frac - rampStart) / PlateauRampFrac);
        return PlateauStartTiles + band + ramp * PlateauStepTiles;
    }
}
