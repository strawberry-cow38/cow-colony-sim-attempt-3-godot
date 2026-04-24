using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim;

public partial class Main : Node3D
{
	private bool _screenshotPending;
	private string _screenshotPath = "";
	private int _screenshotDelayFrames;

	public override void _Ready()
	{
		Engine.MaxFps = 0;
		GD.Print("Cow Colony Sim — attempt 3, day 0.");
		ProcessCommandLine();
	}

	private WorldMapCoord? _autoSettleCoord;

	private void ProcessCommandLine()
	{
		var args = OS.GetCmdlineArgs();
		foreach (var raw in args)
		{
			if (raw == "--settle-at-center") _autoSettleCoord = WorldMap.Center;
			else if (raw.StartsWith("--settle-at="))
			{
				var s = raw.Substring("--settle-at=".Length).Split(',');
				if (s.Length == 2 && int.TryParse(s[0], out var x) && int.TryParse(s[1], out var z))
					_autoSettleCoord = new WorldMapCoord(x, z);
			}
			else if (raw.StartsWith("--screenshot="))
			{
				_screenshotPending = true;
				_screenshotPath = raw.Substring("--screenshot=".Length);
			}
			else if (raw.StartsWith("--after-frames="))
			{
				int.TryParse(raw.Substring("--after-frames=".Length), out _screenshotDelayFrames);
			}
		}
		if (_autoSettleCoord.HasValue)
		{
			CallDeferred(nameof(AutoSettle));
		}
	}

	private void AutoSettle()
	{
		var sim = GetNode<SimHost>("/root/SimHost");
		var coord = _autoSettleCoord!.Value;
		// Debug screenshots want a tree-friendly biome so we can actually
		// *see* the trees. Spiral out from the requested coord until we
		// hit grassland / forest / taiga / jungle / savanna.
		if (!IsTreeFriendly(sim, coord))
		{
			for (var r = 1; r < WorldMap.Size; r++)
			{
				var found = false;
				for (var dx = -r; dx <= r && !found; dx++)
				for (var dz = -r; dz <= r && !found; dz++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
					var c = new WorldMapCoord(_autoSettleCoord.Value.X + dx, _autoSettleCoord.Value.Z + dz);
					if (!WorldMap.InBounds(c.X, c.Z)) continue;
					if (IsTreeFriendly(sim, c)) { coord = c; found = true; }
				}
				if (found) break;
			}
		}
		sim.SettleAt(coord);
		GD.Print($"Main: auto-settled at ({coord.X},{coord.Z}).");
	}

	private static bool IsTreeFriendly(SimHost sim, WorldMapCoord c)
	{
		if (!WorldMap.InBounds(c.X, c.Z)) return false;
		if (sim.Overworld.IsOcean(c)) return false;
		var biome = sim.Overworld.Get(c).BiomeId;
		return biome == Sim.Biomes.BiomeBuiltins.GrasslandId
			|| biome == Sim.Biomes.BiomeBuiltins.TemperateForestId
			|| biome == Sim.Biomes.BiomeBuiltins.TaigaId
			|| biome == Sim.Biomes.BiomeBuiltins.JungleId
			|| biome == Sim.Biomes.BiomeBuiltins.SavannaId;
	}

	public override void _Process(double delta)
	{
		if (!_screenshotPending) return;
		if (_screenshotDelayFrames > 0) { _screenshotDelayFrames--; return; }
		_screenshotPending = false;
		var img = GetViewport().GetTexture().GetImage();
		var err = img.SavePng(_screenshotPath);
		GD.Print($"Main: saved screenshot to {_screenshotPath} err={err}.");
		GetTree().Quit();
	}
}
