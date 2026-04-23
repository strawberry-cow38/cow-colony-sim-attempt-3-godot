using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

/// <summary>
/// Low-LOD visual backdrop for the 8 overworld-map cells surrounding the
/// playable pocket. Each neighbor cell renders as a coarse heightmap
/// mesh — same continent + mountain noise the pocket uses, just sampled
/// at low res — tinted by the cell's biome. Gives the pocket a sense of
/// continuing terrain: mountains stretch past the playable edge, plains
/// recede into flat colored land.
///
/// No rivers, lakes, detail noise, or snow caps — backdrop only, not
/// explorable. Rebuilds on <see cref="SimHost.WorldRegenerated"/>.
/// </summary>
public partial class NeighborBackdrop3D : Node3D
{
    // Sampling resolution per neighbor: VertsPerSide × VertsPerSide
    // vertices laid over a Cell.SizeTiles × Cell.SizeTiles footprint.
    // 33 verts = 32 quads per edge = ~8-tile sample step at 256-tile
    // cells. Cheap (≈1k verts per neighbor, <10k total) and captures
    // mountain silhouettes fine for a distant backdrop.
    private const int VertsPerSide = 33;

    private SimHost? _sim;

    public override void _Ready()
    {
        _sim = GetNode<SimHost>("/root/SimHost");
        _sim.WorldRegenerated += Rebuild;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (_sim != null) _sim.WorldRegenerated -= Rebuild;
    }

    private void Rebuild()
    {
        if (_sim == null) return;

        foreach (var child in GetChildren()) child.QueueFree();

        var noise = new NoiseStack(_sim.CurrentSeed);
        var tileW = SimConstants.TileWidthMeters;
        var tileH = SimConstants.TileHeightMeters;
        var cellTiles = Cell.SizeTiles;
        var cellMeters = cellTiles * tileW;
        var mapCoord = _sim.CurrentMapCoord;

        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;

            var nx = mapCoord.X + dx;
            var nz = mapCoord.Z + dz;
            if (!WorldMap.InBounds(nx, nz)) continue;

            var cell = _sim.Overworld.Get(nx, nz);
            var biome = BiomeRegistry.Get(cell.BiomeId);
            var color = new Color(biome.DebugR, biome.DebugG, biome.DebugB);

            // Pocket tile coords span [-cellTiles/2, +cellTiles/2). Neighbor
            // at offset (dx, dz) spans the next cell over in each axis, so
            // sample the same continuous noise stack at its tile range.
            var tileOriginX = dx * cellTiles - cellTiles / 2;
            var tileOriginZ = dz * cellTiles - cellTiles / 2;

            var mesh = BuildHeightMesh(noise, tileOriginX, tileOriginZ, cellTiles, tileW, tileH, color);
            AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                    Roughness = 1f,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    private static ArrayMesh BuildHeightMesh(
        NoiseStack noise, int tileOriginX, int tileOriginZ, int cellTiles,
        float tileW, float tileH, Color color)
    {
        var n = VertsPerSide;
        var step = cellTiles / (float)(n - 1);
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        var indices = new int[(n - 1) * (n - 1) * 6];

        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            var tx = tileOriginX + i * step;
            var tz = tileOriginZ + j * step;
            var h = SampleHeight(noise, tx, tz);
            verts[i + j * n] = new Vector3(tx * tileW, h * tileH, tz * tileW);
            colors[i + j * n] = color;
        }

        // Face normals averaged per vertex via cross products over the
        // 2x neighbor triangles. Simple central-difference gradient over
        // the heightmap is good enough at this res.
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            var iL = Mathf.Max(i - 1, 0);
            var iR = Mathf.Min(i + 1, n - 1);
            var jD = Mathf.Max(j - 1, 0);
            var jU = Mathf.Min(j + 1, n - 1);
            var hL = verts[iL + j * n].Y;
            var hR = verts[iR + j * n].Y;
            var hD = verts[i + jD * n].Y;
            var hU = verts[i + jU * n].Y;
            var nrm = new Vector3(hL - hR, 2f * tileW * step, hD - hU).Normalized();
            normals[i + j * n] = nrm;
        }

        var k = 0;
        for (var i = 0; i < n - 1; i++)
        for (var j = 0; j < n - 1; j++)
        {
            var a = i + j * n;
            var b = (i + 1) + j * n;
            var c = i + (j + 1) * n;
            var d = (i + 1) + (j + 1) * n;
            indices[k++] = a; indices[k++] = c; indices[k++] = b;
            indices[k++] = b; indices[k++] = c; indices[k++] = d;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // Coarse height sample. Same continent + mountain layers WorldGen
    // uses for its heightmap pass — sans rivers, lakes, detail, coast —
    // so neighbor silhouettes match what would emerge inside that cell
    // if it became the playable pocket.
    private static float SampleHeight(NoiseStack noise, float x, float z)
    {
        var continent = noise.Continent.GetNoise(x, z);
        var baseH = 6f + continent * 4f;

        var maskRaw = (noise.MountainMask.GetNoise(x, z) + 1f) * 0.5f;
        var mountainWeight = Smoothstep(0.75f, 0.95f, maskRaw);
        if (mountainWeight > 0f)
        {
            var ridge = (noise.Ridge.GetNoise(x, z) + 1f) * 0.5f;
            var mountainH = 12f + ridge * 90f;
            baseH = Lerp(baseH, mountainH, mountainWeight);
        }
        return baseH;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
