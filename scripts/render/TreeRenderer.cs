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
        // Y offset (in native-mesh local units) between the mesh's pivot and
        // its bottom face. baseY needs to be shifted by NativePivotToBottom
        // * appliedScale so a tree whose .glb origin sits at the model center
        // still lands its feet on the ground.
        public float NativePivotToBottom;
        // Native-space XZ delta from model pivot to AABB centroid. Apply with
        // negation so the mesh centroid lands at tile center regardless of
        // where blender authored the origin (off-pivot trunks).
        public float NativePivotCenterOffsetX;
        public float NativePivotCenterOffsetZ;
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
        var aabb = mesh.GetAabb();
        var nativeH = aabb.Size.Y;
        if (nativeH <= 0.0001f) nativeH = 1f;
        var pivotToBottom = -aabb.Position.Y;
        var centerOffX = -(aabb.Position.X + aabb.Size.X * 0.5f);
        var centerOffZ = -(aabb.Position.Z + aabb.Size.Z * 0.5f);
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
            NativePivotToBottom = pivotToBottom,
            NativePivotCenterOffsetX = centerOffX,
            NativePivotCenterOffsetZ = centerOffZ,
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
        // Shift so the mesh's AABB centroid lands over the tile center in X/Z
        // and the mesh's lowest point sits flush on baseY — handles .glb
        // models whose origin was authored off-pivot (center vs base, trunk
        // offset from canopy centroid, etc.).
        var footOffset = state.NativePivotToBottom * s;
        var xOffset = state.NativePivotCenterOffsetX * s;
        var zOffset = state.NativePivotCenterOffsetZ * s;
        return new Transform3D(basis, new Vector3(x + xOffset, baseY + footOffset, z + zOffset));
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
        var merged = MergeAllMeshes(scene);
        scene.QueueFree();
        return merged;
    }

    /// <summary>Walk every MeshInstance3D under <paramref name="root"/> and
    /// copy its surfaces (with local transform baked in) into a single
    /// ArrayMesh. Required because MultiMesh renders one Mesh resource per
    /// instance; leaving trunk and canopy as sibling nodes gives MultiMesh
    /// only one of the two.</summary>
    private static ArrayMesh? MergeAllMeshes(Node root)
    {
        var merged = new ArrayMesh();
        CollectInto(root, Transform3D.Identity, merged);
        return merged.GetSurfaceCount() > 0 ? merged : null;
    }

    private static void CollectInto(Node n, Transform3D parentXform, ArrayMesh into)
    {
        var xform = parentXform;
        if (n is Node3D n3d) xform = parentXform * n3d.Transform;
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            var src = mi.Mesh;
            for (var s = 0; s < src.GetSurfaceCount(); s++)
            {
                var arrays = src.SurfaceGetArrays(s);
                TransformPositions(arrays, xform);
                into.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                var mat = mi.GetActiveMaterial(s) ?? src.SurfaceGetMaterial(s);
                if (mat != null) into.SurfaceSetMaterial(into.GetSurfaceCount() - 1, mat);
            }
        }
        foreach (var child in n.GetChildren()) CollectInto(child, xform, into);
    }

    private static void TransformPositions(Godot.Collections.Array arrays, Transform3D xform)
    {
        const int vertIdx = (int)Mesh.ArrayType.Vertex;
        const int normIdx = (int)Mesh.ArrayType.Normal;
        if (arrays[vertIdx].VariantType == Variant.Type.PackedVector3Array)
        {
            var verts = arrays[vertIdx].AsVector3Array();
            for (var i = 0; i < verts.Length; i++) verts[i] = xform * verts[i];
            arrays[vertIdx] = verts;
        }
        if (arrays.Count > normIdx && arrays[normIdx].VariantType == Variant.Type.PackedVector3Array)
        {
            var normals = arrays[normIdx].AsVector3Array();
            var basis = xform.Basis.Inverse().Transposed();
            for (var i = 0; i < normals.Length; i++) normals[i] = (basis * normals[i]).Normalized();
            arrays[normIdx] = normals;
        }
    }
}
