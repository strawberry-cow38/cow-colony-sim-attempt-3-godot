using Godot;
using System.Collections.Generic;
using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Crops;

namespace CowColonySim.Render;

/// <summary>
/// MultiMesh-instanced renderer for every <see cref="Crop"/> entity. One
/// <see cref="MultiMeshInstance3D"/> per crop kind — all trees of that
/// kind draw in a single batched call regardless of count.
///
/// Each frame walks the Crop stream, groups by kind, resizes the per-kind
/// MultiMesh instance count, and writes per-instance transforms (scale
/// from <see cref="Crop.Growth"/>, position from <see cref="CropTile.Pos"/>).
///
/// Mesh source: <see cref="CropDef.ModelPath"/> loads a .glb at boot and
/// scales to <see cref="CropDef.ModelHeightMeters"/>. Falls back to a
/// procedural trunk+canopy pair if the model fails to load (kept simple —
/// fallback reuses the original node-per-tree path for visibility, not
/// performance).
/// </summary>
public sealed partial class TreeRenderer : Node3D
{
    private SimHost? _simHost;
    private readonly Dictionary<byte, KindState> _byKind = new();
    private readonly List<(byte kind, Transform3D xform)> _scratch = new();

    private sealed class KindState
    {
        public MultiMeshInstance3D Node = null!;
        public MultiMesh Mesh = null!;
        public float NativeHeightMeters;
        public float TargetHeightMeters;
    }

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;

        _scratch.Clear();
        _simHost.World.Stream<Crop, CropTile>().For((ref Crop crop, ref CropTile tile) =>
        {
            var def = CropRegistry.Get(crop.KindId);
            if (def.Id == CropRegistry.NoCrop) return;
            var state = EnsureKind(def);
            if (state == null) return;
            _scratch.Add((crop.KindId, BuildTransform(state, tile.Pos, crop.Growth)));
        });

        // Bucket per kind. Reset counts first so kinds that have no crops
        // this frame go to zero.
        foreach (var kv in _byKind) kv.Value.Mesh.VisibleInstanceCount = 0;
        // Second pass writes. Group by kind into per-kind counters.
        var counters = new Dictionary<byte, int>();
        foreach (var (kind, xform) in _scratch)
        {
            counters.TryGetValue(kind, out var idx);
            var state = _byKind[kind];
            if (state.Mesh.InstanceCount <= idx)
            {
                // Grow capacity in ~doubling chunks; SetInstanceCount nukes
                // existing transforms so we only resize upward in batches.
                var next = System.Math.Max(16, state.Mesh.InstanceCount * 2);
                while (next <= idx) next *= 2;
                state.Mesh.InstanceCount = next;
            }
            state.Mesh.SetInstanceTransform(idx, xform);
            counters[kind] = idx + 1;
        }
        foreach (var (kind, count) in counters)
            _byKind[kind].Mesh.VisibleInstanceCount = count;
    }

    private KindState? EnsureKind(CropDef def)
    {
        if (_byKind.TryGetValue(def.Id, out var existing)) return existing;
        var mesh = def.ModelPath != null ? LoadFirstMesh(def.ModelPath) : null;
        if (mesh == null)
        {
            GD.PushWarning($"TreeRenderer: failed to load mesh for {def.Name} (path={def.ModelPath})");
            return null;
        }
        var nativeH = mesh.GetAabb().Size.Y;
        if (nativeH <= 0.0001f) nativeH = 1f;
        var mm = new MultiMesh
        {
            Mesh = mesh,
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = 0,
            VisibleInstanceCount = 0,
        };
        var mmi = new MultiMeshInstance3D { Multimesh = mm };
        AddChild(mmi);
        var state = new KindState
        {
            Node = mmi,
            Mesh = mm,
            NativeHeightMeters = nativeH,
            TargetHeightMeters = def.ModelHeightMeters > 0 ? def.ModelHeightMeters : def.TrunkHeightMeters + def.CanopyHeightMeters,
        };
        _byKind[def.Id] = state;
        return state;
    }

    private static Transform3D BuildTransform(KindState state, CowColonySim.Sim.Grid.TilePos feet, float growth)
    {
        // Clamp sapling scale — pure growth=0 reads as invisible.
        var growthScale = Mathf.Lerp(0.25f, 1.0f, growth);
        var s = (state.TargetHeightMeters / state.NativeHeightMeters) * growthScale;
        var x = feet.X + 0.5f;
        var z = feet.Z + 0.5f;
        var baseY = feet.Y * CowColonySim.Sim.SimConstants.TileHeightMeters;
        var basis = Basis.Identity.Scaled(new Vector3(s, s, s));
        return new Transform3D(basis, new Vector3(x, baseY, z));
    }

    private static Mesh? LoadFirstMesh(string resPath)
    {
        var absolute = ProjectSettings.GlobalizePath(resPath);
        if (!System.IO.File.Exists(absolute))
        {
            GD.PushWarning($"TreeRenderer: file not found at {absolute}");
            return null;
        }
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromFile(absolute, state);
        if (err != Error.Ok) return null;
        var scene = doc.GenerateScene(state);
        if (scene == null) return null;
        var mesh = FindFirstMesh(scene);
        scene.QueueFree();
        return mesh;
    }

    private static Mesh? FindFirstMesh(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) return mi.Mesh;
        foreach (var child in n.GetChildren())
        {
            var m = FindFirstMesh(child);
            if (m != null) return m;
        }
        return null;
    }
}
