namespace CowColonySim.Sim.Crops;

/// <summary>
/// Initial tree roster. Only pine + maple ship with real .glb meshes
/// (attempt-2 assets). Birch / oak will come back once we have art for
/// them — same metadata record, just different Id + ModelPath.
/// </summary>
public static class BuiltinCrops
{
    public const byte PineId  = 1;
    public const byte MapleId = 2;

    public static readonly CropDef Pine = new(
        Id: PineId, Name: "Pine",
        GrowthTicksToMature: 216_000,
        MinYieldGrowth: 0.5f, MaxYield: 10,
        TrunkColor: 0x4A2F1C, CanopyColor: 0x1D4D2A,
        CanopyShape: CanopyShape.Cone,
        TrunkHeightMeters: 3.5f, TrunkRadiusMeters: 0.35f,
        CanopyHeightMeters: 3.2f, CanopyRadiusMeters: 1.3f,
        ModelPath: "res://assets/models/pine.glb",
        ModelHeightMeters: 6.7f);

    public static readonly CropDef Maple = new(
        Id: MapleId, Name: "Maple",
        GrowthTicksToMature: 192_000,
        MinYieldGrowth: 0.5f, MaxYield: 20,
        TrunkColor: 0x7D5A3C, CanopyColor: 0xC8632A,
        CanopyShape: CanopyShape.Sphere,
        TrunkHeightMeters: 3.2f, TrunkRadiusMeters: 0.22f,
        CanopyHeightMeters: 2.2f, CanopyRadiusMeters: 1.3f,
        ModelPath: "res://assets/models/maple.glb",
        ModelHeightMeters: 5.4f);

    private static readonly byte[] TreeKinds = { PineId, MapleId };

    public static void RegisterAll()
    {
        CropRegistry.Register(Pine);
        CropRegistry.Register(Maple);
    }

    public static byte RandomTreeKind(Random rng) => TreeKinds[rng.Next(TreeKinds.Length)];

    /// <summary>Linear yield ramp from 0 at <c>MinYieldGrowth</c> to
    /// <c>MaxYield</c> at 1.0. Under <c>MinYieldGrowth</c> the crop is too
    /// small to yield anything.</summary>
    public static int YieldOf(CropDef def, float growth)
    {
        if (growth < def.MinYieldGrowth) return 0;
        var span = 1f - def.MinYieldGrowth;
        if (span <= 0f) return def.MaxYield;
        var t = (growth - def.MinYieldGrowth) / span;
        return Math.Max(0, (int)Math.Round(def.MaxYield * t));
    }
}
