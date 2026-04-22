using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim;

public partial class BenchHarness : Node
{
    private const double WarmupSec = 3.0;
    private const double SampleSec = 20.0;
    private const int StressCowCount = 500;
    private const int StressSpawnRadius = 80;

    private bool _enabled;
    private bool _stress;
    private double _elapsed;
    private readonly List<double> _fps = new();

    private Render.OrbitCamera? _cam;
    private Vector3 _camCenter;
    private float _camRadius;

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineArgs())
        {
            if (a == "--bench") _enabled = true;
            if (a == "--stress") _stress = true;
        }
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a == "--bench") _enabled = true;
            if (a == "--stress") _stress = true;
        }

        if (!_enabled) return;
        GD.Print($"[BENCH] enabled — warmup {WarmupSec}s, sample {SampleSec}s, stress={_stress}");
        ProcessMode = ProcessModeEnum.Always;
        CallDeferred(nameof(AfterSceneReady));
    }

    private void AfterSceneReady()
    {
        if (_stress) SpawnStressCows();
        _cam = FindOrbitCamera(GetTree().Root);
        if (_cam != null)
        {
            _camCenter = _cam.Target;
            _camRadius = 60f;
            _cam.Radius = _camRadius;
            GD.Print($"[BENCH] camera hooked at {_camCenter}");
        }
        else
        {
            GD.Print("[BENCH] no OrbitCamera found");
        }
    }

    private Render.OrbitCamera? FindOrbitCamera(Node n)
    {
        if (n is Render.OrbitCamera cam) return cam;
        foreach (var child in n.GetChildren())
        {
            var found = FindOrbitCamera(child);
            if (found != null) return found;
        }
        return null;
    }

    private void SpawnStressCows()
    {
        var host = GetNodeOrNull<SimHost>("/root/SimHost");
        if (host == null) { GD.Print("[BENCH] SimHost missing, skip spawn"); return; }

        var rng = new Random(1234);
        int spawned = 0;
        for (int i = 0; i < StressCowCount * 4 && spawned < StressCowCount; i++)
        {
            int x = rng.Next(-StressSpawnRadius, StressSpawnRadius);
            int z = rng.Next(-StressSpawnRadius, StressSpawnRadius);
            int y;
            try { y = WorldGen.SurfaceY(host.Tiles, x, z); }
            catch { continue; }
            var cow = host.World.Spawn();
            cow.Add(new Colonist());
            cow.Add(TileMath.FeetOfTile(new TilePos(x, y, z)));
            spawned++;
        }
        GD.Print($"[BENCH] spawned {spawned} stress cows");
    }

    public override void _Process(double delta)
    {
        if (!_enabled) return;
        _elapsed += delta;

        if (_cam != null)
        {
            var t = (float)_elapsed;
            _cam.YawDegrees = (t * 30f) % 360f;
            var px = Mathf.Sin(t * 0.5f) * 40f;
            var pz = Mathf.Cos(t * 0.5f) * 40f;
            _cam.Target = _camCenter + new Vector3(px, 0, pz);
        }

        if (_elapsed < WarmupSec) return;

        var fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        _fps.Add(fps);

        if (_elapsed - WarmupSec < SampleSec) return;

        var avg = _fps.Average();
        var min = _fps.Min();
        var max = _fps.Max();
        _fps.Sort();
        var p01 = _fps[(int)(_fps.Count * 0.01)];
        var p99 = _fps[(int)(_fps.Count * 0.99)];
        var frame = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        var phys = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var objs = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        var vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);

        GD.Print("[BENCH] RESULTS");
        GD.Print($"[BENCH] stress={_stress} samples={_fps.Count}");
        GD.Print($"[BENCH] fps avg={avg:0.0} min={min:0.0} max={max:0.0} p01={p01:0.0} p99={p99:0.0}");
        GD.Print($"[BENCH] frame process={frame:0.00}ms physics={phys:0.00}ms draws={draws:0} objs={objs:0} prims={prims:0} vram={vram:0.0}MB");
        GD.Print("[BENCH] done, quitting");
        GetTree().Quit(0);
        _enabled = false;
    }
}
