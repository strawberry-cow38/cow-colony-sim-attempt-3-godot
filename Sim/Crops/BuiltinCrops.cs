namespace CowColonySim.Sim.Crops;

/// <summary>
/// Attempt-2 tree roster ported forward: Birch / Pine / Oak / Maple.
/// Yield + growth-ticks + visuals mirror the old <c>trees.js</c> so the
/// biome feel carries over. Same metadata shape will host wheat / berries
/// later — crops aren't a separate system, just different <see cref="CropDef"/>s.
/// </summary>
public static class BuiltinCrops
{
    public const byte BirchId = 1;
    public const byte PineId  = 2;
    public const byte OakId   = 3;
    public const byte MapleId = 4;

    public static readonly CropDef Birch = new(
        Id: BirchId, Name: "Birch",
        GrowthTicksToMature: 144_000, // 2400 long-tier ticks @ attempt-2; at 60Hz * ~40s/tick this is placeholder for phase A
        MinYieldGrowth: 0.5f, MaxYield: 14,
        TrunkColor: 0xE8E2D4, CanopyColor: 0x9FCC64,
        CanopyShape: CanopyShape.Sphere,
        TrunkHeightMeters: 3.0f, TrunkRadiusMeters: 0.18f,
        CanopyHeightMeters: 2.0f, CanopyRadiusMeters: 1.1f);

    public static readonly CropDef Pine = new(
        Id: PineId, Name: "Pine",
        GrowthTicksToMature: 216_000,
        MinYieldGrowth: 0.5f, MaxYield: 10,
        TrunkColor: 0x4A2F1C, CanopyColor: 0x1D4D2A,
        CanopyShape: CanopyShape.Cone,
        TrunkHeightMeters: 3.5f, TrunkRadiusMeters: 0.35f,
        CanopyHeightMeters: 3.2f, CanopyRadiusMeters: 1.3f);

    public static readonly CropDef Oak = new(
        Id: OakId, Name: "Oak",
        GrowthTicksToMature: 384_000,
        MinYieldGrowth: 0.5f, MaxYield: 28,
        TrunkColor: 0x5A3820, CanopyColor: 0x2E6F3A,
        CanopyShape: CanopyShape.Sphere,
        TrunkHeightMeters: 3.8f, TrunkRadiusMeters: 0.32f,
        CanopyHeightMeters: 2.6f, CanopyRadiusMeters: 1.7f);

    public static readonly CropDef Maple = new(
        Id: MapleId, Name: "Maple",
        GrowthTicksToMature: 192_000,
        MinYieldGrowth: 0.5f, MaxYield: 20,
        TrunkColor: 0x7D5A3C, CanopyColor: 0xC8632A,
        CanopyShape: CanopyShape.Sphere,
        TrunkHeightMeters: 3.2f, TrunkRadiusMeters: 0.22f,
        CanopyHeightMeters: 2.2f, CanopyRadiusMeters: 1.3f);

    private static readonly byte[] TreeKinds = { BirchId, PineId, OakId, MapleId };

    public static void RegisterAll()
    {
        CropRegistry.Register(Birch);
        CropRegistry.Register(Pine);
        CropRegistry.Register(Oak);
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
