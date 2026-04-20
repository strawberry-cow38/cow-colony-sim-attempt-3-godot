using Godot;
using System.Collections.Generic;
using fennecs;
using CowColonySim.Sim.Components;

namespace CowColonySim.Render;

public sealed partial class ColonistRenderer : Node3D
{
    private const float CapsuleHeight = 1.4f;
    private const float CapsuleRadius = 0.35f;

    private SimHost? _simHost;
    private StandardMaterial3D? _material;
    private Mesh? _mesh;
    private readonly Dictionary<Entity, MeshInstance3D> _instances = new();

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.88f, 0.82f),
            Roughness = 0.8f,
        };
        _mesh = new CapsuleMesh { Height = CapsuleHeight, Radius = CapsuleRadius };
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;

        var seen = new HashSet<Entity>();
        _simHost.World.Stream<Position, Colonist>().For((in Entity e, ref Position p, ref Colonist _) =>
        {
            seen.Add(e);
            if (!_instances.TryGetValue(e, out var mi))
            {
                mi = new MeshInstance3D { Mesh = _mesh, MaterialOverride = _material };
                AddChild(mi);
                _instances[e] = mi;
            }
            mi.Position = new Vector3(p.X, p.Y + CapsuleHeight * 0.5f + CapsuleRadius, p.Z);
        });

        if (seen.Count == _instances.Count) return;
        var stale = new List<Entity>();
        foreach (var kv in _instances)
        {
            if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
        }
        foreach (var e in stale)
        {
            _instances[e].QueueFree();
            _instances.Remove(e);
        }
    }
}
