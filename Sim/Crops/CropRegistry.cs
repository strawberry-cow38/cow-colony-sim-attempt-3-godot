using System.Collections.Generic;

namespace CowColonySim.Sim.Crops;

/// <summary>
/// Process-wide crop kind registry. IDs are stable across save/load; IDs in
/// [0,255] so a <c>byte</c> fits in components. Tests/tooling reset via
/// <see cref="Clear"/> — SimHost registers builtins at boot.
/// </summary>
public static class CropRegistry
{
    public const byte NoCrop = 0;

    private static readonly List<CropDef> _byId = new() { default };
    private static readonly Dictionary<string, byte> _byName = new();

    public static IReadOnlyList<CropDef> All => _byId;

    public static byte Register(CropDef def)
    {
        if (def.Id == NoCrop)
            throw new ArgumentException($"Crop id {NoCrop} is reserved for 'no crop'");
        while (_byId.Count <= def.Id) _byId.Add(default);
        _byId[def.Id] = def;
        _byName[def.Name] = def.Id;
        return def.Id;
    }

    public static CropDef Get(byte id)
    {
        if (id == NoCrop || id >= _byId.Count) return default;
        return _byId[id];
    }

    public static byte GetId(string name) =>
        _byName.TryGetValue(name, out var id) ? id : NoCrop;

    public static void Clear()
    {
        _byId.Clear();
        _byId.Add(default);
        _byName.Clear();
    }
}
