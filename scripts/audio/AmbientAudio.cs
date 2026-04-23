using System.Collections.Generic;
using Godot;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Audio;

/// <summary>
/// Drives per-biome ambient loops. One <see cref="AudioStreamPlayer"/> per
/// biome, all routed through the "Ambient" audio bus so the settings menu
/// can mix them with a single slider. Active player = current pocket biome;
/// crossfades over <see cref="FadeSeconds"/> when the biome changes.
///
/// Clips live at res://assets/audio/ambient/{biome}.wav, loop mode forced
/// to Forward on load (Godot's wav importer defaults to None).
/// </summary>
public sealed partial class AmbientAudio : Node
{
    public const string AmbientBus = "Ambient";
    public const float FadeSeconds = 1.2f;
    public const float ActiveVolumeDb = 0f;
    public const float SilentVolumeDb = -60f;

    /// <summary>Bus volume staged by <see cref="UI.SettingsMenu"/> when it
    /// beats <see cref="_Ready"/> in node-init order. <see cref="EnsureAmbientBus"/>
    /// applies it once the bus exists.</summary>
    public static float? PendingVolumeDb;

    private SimHost _sim = null!;
    private readonly Dictionary<byte, AudioStreamPlayer> _players = new();
    private byte _currentBiome = BiomeBuiltins.UnknownId;

    public override void _Ready()
    {
        EnsureAmbientBus();

        _sim = GetNode<SimHost>("/root/SimHost");
        _sim.WorldRegenerated += OnWorldChanged;
        _sim.WorldSelectionChanged += OnWorldChanged;

        Register(BiomeBuiltins.DesertId, "res://assets/audio/ambient/desert.wav");

        foreach (var p in _players.Values) p.Play();
        UpdateTargetBiome();
    }

    public override void _ExitTree()
    {
        if (_sim != null)
        {
            _sim.WorldRegenerated -= OnWorldChanged;
            _sim.WorldSelectionChanged -= OnWorldChanged;
        }
    }

    public override void _Process(double delta)
    {
        if (_sim == null) return;
        UpdateTargetBiome();
        var step = (float)delta * (ActiveVolumeDb - SilentVolumeDb) / FadeSeconds;
        foreach (var (biome, player) in _players)
        {
            var target = biome == _currentBiome ? ActiveVolumeDb : SilentVolumeDb;
            player.VolumeDb = Mathf.MoveToward(player.VolumeDb, target, step);
        }
    }

    private void UpdateTargetBiome()
    {
        if (_sim.AwaitingWorldSelection)
        {
            _currentBiome = BiomeBuiltins.UnknownId;
            return;
        }
        var cell = _sim.Overworld.Get(_sim.CurrentMapCoord);
        _currentBiome = cell.IsOcean ? BiomeBuiltins.UnknownId : cell.BiomeId;
    }

    private void OnWorldChanged() => UpdateTargetBiome();

    private void Register(byte biomeId, string resPath)
    {
        var stream = LoadLoopingWav(resPath);
        if (stream == null) return;
        var player = new AudioStreamPlayer
        {
            Stream = stream,
            Bus = AmbientBus,
            VolumeDb = SilentVolumeDb,
            Autoplay = false,
        };
        AddChild(player);
        _players[biomeId] = player;
    }

    private static AudioStreamWav? LoadLoopingWav(string resPath)
    {
        if (!ResourceLoader.Exists(resPath))
        {
            GD.PushWarning($"AmbientAudio: missing {resPath}");
            return null;
        }
        var stream = GD.Load<AudioStreamWav>(resPath);
        if (stream == null)
        {
            GD.PushWarning($"AmbientAudio: load failed for {resPath}");
            return null;
        }
        // Force looping — Godot's wav importer defaults to no loop, which
        // would leave the clip silent after first playthrough. Resource
        // is shared; mutating here is fine since nothing else uses it.
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        stream.LoopBegin = 0;
        stream.LoopEnd = 0;
        return stream;
    }

    private static void EnsureAmbientBus()
    {
        var idx = AudioServer.GetBusIndex(AmbientBus);
        if (idx == -1)
        {
            idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, AmbientBus);
            AudioServer.SetBusSend(idx, "Master");
        }
        if (PendingVolumeDb is { } db)
        {
            AudioServer.SetBusVolumeDb(idx, db);
            AudioServer.SetBusMute(idx, db <= -79.9f);
        }
    }
}
