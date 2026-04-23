using Godot;
using System.Collections.Generic;
using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Crops;

namespace CowColonySim.Render;

/// <summary>
/// Renders every <see cref="Crop"/> entity as a trunk + canopy pair of
/// <see cref="MeshInstance3D"/>s scaled by <see cref="Crop.Growth"/>.
///
/// Meshes + materials are cached per kind — adding a new crop kind means
/// registering a <see cref="CropDef"/> and letting the renderer discover it
/// on first sighting. Per-frame work is O(crops): walk the ECS, add/update
/// nodes for live entities, prune nodes for despawned ones.
///
/// Not yet: LOD / MultiMesh instancing. Profile once densities get noisy.
/// </summary>
public sealed partial class TreeRenderer : Node3D
{
    private SimHost? _simHost;
    private readonly Dictionary<byte, (Mesh trunk, Mesh canopy, StandardMaterial3D trunkMat, StandardMaterial3D canopyMat)> _kindCache = new();
    private readonly Dictionary<Entity, (MeshInstance3D trunk, MeshInstance3D canopy, byte kindId)> _instances = new();

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;

        var seen = new HashSet<Entity>();
        _simHost.World.Stream<Crop, CropTile>().For((in Entity e, ref Crop crop, ref CropTile tile) =>
        {
            seen.Add(e);
            var def = CropRegistry.Get(crop.KindId);
            if (def.Id == CropRegistry.NoCrop) return;
            EnsureKindMeshes(def);

            if (!_instances.TryGetValue(e, out var slot) || slot.kindId != crop.KindId)
            {
                if (_instances.TryGetValue(e, out var stale))
                {
                    stale.trunk.QueueFree();
                    stale.canopy.QueueFree();
                }
                var kind = _kindCache[crop.KindId];
                var trunkMi = new MeshInstance3D { Mesh = kind.trunk, MaterialOverride = kind.trunkMat };
                var canopyMi = new MeshInstance3D { Mesh = kind.canopy, MaterialOverride = kind.canopyMat };
                AddChild(trunkMi);
                AddChild(canopyMi);
                slot = (trunkMi, canopyMi, crop.KindId);
                _instances[e] = slot;
            }

            ApplyTransform(slot.trunk, slot.canopy, def, tile.Pos, crop.Growth);
        });

        if (seen.Count == _instances.Count) return;
        var stale = new List<Entity>();
        foreach (var kv in _instances) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var e in stale)
        {
            var slot = _instances[e];
            slot.trunk.QueueFree();
            slot.canopy.QueueFree();
            _instances.Remove(e);
        }
    }

    private void EnsureKindMeshes(CropDef def)
    {
        if (_kindCache.ContainsKey(def.Id)) return;
        var trunkMat = new StandardMaterial3D
        {
            AlbedoColor = HexColor(def.TrunkColor),
            Roughness = 0.95f,
        };
        var canopyMat = new StandardMaterial3D
        {
            AlbedoColor = HexColor(def.CanopyColor),
            Roughness = 0.9f,
        };
        var trunk = new CylinderMesh
        {
            TopRadius = def.TrunkRadiusMeters,
            BottomRadius = def.TrunkRadiusMeters,
            Height = def.TrunkHeightMeters,
        };
        Mesh canopy = def.CanopyShape switch
        {
            CanopyShape.Cone => new CylinderMesh
            {
                TopRadius = 0.01f,
                BottomRadius = def.CanopyRadiusMeters,
                Height = def.CanopyHeightMeters,
            },
            CanopyShape.Cube => new BoxMesh
            {
                Size = new Vector3(def.CanopyRadiusMeters * 2f, def.CanopyHeightMeters, def.CanopyRadiusMeters * 2f),
            },
            _ => new SphereMesh
            {
                Radius = def.CanopyRadiusMeters,
                Height = def.CanopyHeightMeters,
            },
        };
        _kindCache[def.Id] = (trunk, canopy, trunkMat, canopyMat);
    }

    private static void ApplyTransform(MeshInstance3D trunk, MeshInstance3D canopy, CropDef def, CowColonySim.Sim.Grid.TilePos feet, float growth)
    {
        // Clamp sapling size — pure growth=0 reads as invisible.
        var scale = Mathf.Lerp(0.25f, 1.0f, growth);
        var x = feet.X + 0.5f;
        var z = feet.Z + 0.5f;
        var baseY = feet.Y * CowColonySim.Sim.SimConstants.TileHeightMeters;
        trunk.Scale = new Vector3(scale, scale, scale);
        trunk.Position = new Vector3(x, baseY + def.TrunkHeightMeters * scale * 0.5f, z);
        canopy.Scale = new Vector3(scale, scale, scale);
        canopy.Position = new Vector3(
            x,
            baseY + def.TrunkHeightMeters * scale + def.CanopyHeightMeters * scale * 0.5f,
            z);
    }

    private static Color HexColor(uint rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f);
}
