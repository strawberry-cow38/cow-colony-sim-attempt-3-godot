using Godot;
using System;
using System.Collections.Generic;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;
using GArray = Godot.Collections.Array;

namespace CowColonySim.Render;

/// <summary>
/// Flat chevron arrows stamped every SampleStrideCells along each river path,
/// oriented with the river's flow direction. Debug-only: toggled by F3, on by
/// default so the next build ships visible. Arrows sit slightly above the
/// water plane so they read as floating markers rather than submerged geometry.
/// </summary>
public sealed partial class RiverArrowRenderer : Node3D
{
    public static bool ShowArrows = true;

    private const int   SampleStrideCells = 24;
    private const float ArrowLengthMeters = 2.4f;
    private const float ArrowHalfWidth    = 0.9f;
    private const float ArrowLiftMeters   = 0.4f;

    private SimHost? _simHost;
    private ArrayMesh? _mesh;
    private StandardMaterial3D? _material;
    private long _lastMutationTick = long.MinValue;
    private readonly List<MeshInstance3D> _instances = new();

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
        _mesh = BuildArrowMesh();
        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.88f, 0.12f),
            Roughness = 0.85f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Visible = ShowArrows;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            ShowArrows = !ShowArrows;
            Visible = ShowArrows;
        }
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;
        var tick = _simHost.Tiles.MutationTick;
        if (tick == _lastMutationTick) return;
        _lastMutationTick = tick;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var mi in _instances) mi.QueueFree();
        _instances.Clear();
        if (_simHost == null) return;

        foreach (var path in _simHost.Tiles.RiverPaths)
        {
            var count = path.Cells.Count;
            if (count < 4) continue;
            // Local tangent window: look ahead/behind a fixed distance
            // along the path and take the displacement as the flow vector
            // at the sample. Short enough to follow bends, long enough to
            // smooth out per-cell staircase jitter.
            const int window = 8;
            for (var i = SampleStrideCells / 2; i < count; i += SampleStrideCells)
            {
                var a = path.Cells[Math.Max(0, i - window)];
                var b = path.Cells[Math.Min(count - 1, i + window)];
                var dx = b.X - a.X;
                var dz = b.Z - a.Z;
                var mag = MathF.Sqrt(dx * dx + dz * dz);
                if (mag < 1e-3f) continue;
                var fx = dx / mag;
                var fz = dz / mag;
                var c = path.Cells[i];
                PlaceArrow(c.X, c.Z, fx, fz);
            }
        }
    }

    private void PlaceArrow(int tileX, int tileZ, float fx, float fz)
    {
        var mi = new MeshInstance3D
        {
            Mesh = _mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var origin = new Vector3(
            tileX * SimConstants.TileWidthMeters + SimConstants.TileWidthMeters * 0.5f,
            WorldGen.WaterLevelY * SimConstants.TileHeightMeters + ArrowLiftMeters,
            tileZ * SimConstants.TileWidthMeters + SimConstants.TileWidthMeters * 0.5f);
        var basis = new Basis(
            new Vector3(fx, 0f, fz),
            new Vector3(0f, 1f, 0f),
            new Vector3(-fz, 0f, fx));
        mi.Transform = new Transform3D(basis, origin);
        AddChild(mi);
        _instances.Add(mi);
    }

    private static ArrayMesh BuildArrowMesh()
    {
        // Flat chevron in XZ plane pointing +X. 4-vert shaft rectangle plus
        // 3-vert tip triangle. Two-sided (material sets cull=disabled) so it
        // reads from above or below the water plane.
        var verts = new Vector3[]
        {
            new(-ArrowLengthMeters * 0.5f, 0f, -ArrowHalfWidth * 0.4f),
            new(-ArrowLengthMeters * 0.5f, 0f,  ArrowHalfWidth * 0.4f),
            new( ArrowLengthMeters * 0.1f, 0f,  ArrowHalfWidth * 0.4f),
            new( ArrowLengthMeters * 0.1f, 0f, -ArrowHalfWidth * 0.4f),
            new( ArrowLengthMeters * 0.1f, 0f, -ArrowHalfWidth),
            new( ArrowLengthMeters * 0.1f, 0f,  ArrowHalfWidth),
            new( ArrowLengthMeters * 0.5f, 0f,  0f),
        };
        var indices = new int[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 };
        var normals = new Vector3[verts.Length];
        for (var i = 0; i < normals.Length; i++) normals[i] = Vector3.Up;

        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index]  = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
