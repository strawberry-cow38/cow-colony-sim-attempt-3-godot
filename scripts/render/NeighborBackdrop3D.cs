using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

/// <summary>
/// Low-LOD visual backdrop for the 8 overworld-map cells surrounding the
/// playable pocket. Each neighbor cell renders as a single flat biome-
/// tinted quad at sea level, giving the pocket a sense of continuing
/// terrain without running the actual WorldGen pipeline for them.
///
/// First-pass flat colored tiles only. Later phases can layer coarse
/// heightmaps, tree silhouettes, or edge fog on top without touching
/// this class — it's a decoration node, not a data source.
/// </summary>
public partial class NeighborBackdrop3D : Node3D
{
    // Height the quads sit at, matching WorldGen.WaterLevelY so the
    // surrounding plane blends with the pocket's sea-level baseline.
    private const float BackdropY = 0f;

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

        var tileW = SimConstants.TileWidthMeters;
        var cellMeters = Cell.SizeTiles * tileW;
        var mapCoord = _sim.CurrentMapCoord;

        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;

            var nx = mapCoord.X + dx;
            var nz = mapCoord.Z + dz;
            // Off-map neighbors: leave the slot empty so the world map's
            // edge reads as "the end" rather than an oddly-colored tile.
            if (!WorldMap.InBounds(nx, nz)) continue;

            var cell = _sim.Overworld.Get(nx, nz);
            var biome = BiomeRegistry.Get(cell.BiomeId);
            var color = new Color(biome.DebugR, biome.DebugG, biome.DebugB);

            var quad = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(cellMeters, cellMeters) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = color,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                    Roughness = 1f,
                },
                Position = new Vector3(dx * cellMeters, BackdropY, dz * cellMeters),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(quad);
        }
    }
}
