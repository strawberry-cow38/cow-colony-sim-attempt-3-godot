using System.Collections.Generic;

namespace CowColonySim.Sim.Biomes;

/// <summary>
/// Central biome table. Populated once at startup via <see cref="Register"/>;
/// everything else reads only.
///
/// Callers MUST NOT switch on biome id outside this registry — ask the
/// registry for the metadata they need (surface tile, debug color, etc.)
/// so adding a biome costs one registration, not edits across the codebase.
/// </summary>
public static class BiomeRegistry
{
    private static readonly List<BiomeDef> _byId = new();
    private static readonly Dictionary<string, byte> _idByName = new();
    private static readonly object _sync = new();

    public static IReadOnlyList<BiomeDef> All => _byId;

    public static byte Register(BiomeDef def)
    {
        lock (_sync)
        {
            while (_byId.Count <= def.Id) _byId.Add(null!);
            _byId[def.Id] = def;
            _idByName[def.Name] = def.Id;
            return def.Id;
        }
    }

    public static BiomeDef Get(byte id)
    {
        lock (_sync)
        {
            if (id < _byId.Count && _byId[id] != null) return _byId[id];
            return BuiltinBiomes.Unknown;
        }
    }

    public static bool TryGetByName(string name, out byte id)
    {
        lock (_sync)
        {
            return _idByName.TryGetValue(name, out id);
        }
    }

    public static void Clear()
    {
        lock (_sync)
        {
            _byId.Clear();
            _idByName.Clear();
        }
    }
}
