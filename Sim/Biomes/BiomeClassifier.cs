namespace CowColonySim.Sim.Biomes;

/// <summary>
/// Whittaker-style (temperature, rainfall) → biome lookup. Rules are
/// ordered: first match wins. Add a biome by registering it in
/// <see cref="BuiltinBiomes"/> then inserting a rule here. No other callers
/// should branch on (temp, rain) — they ask the classifier.
/// </summary>
public static class BiomeClassifier
{
    // Each rule: temperature band (°C) × rainfall band (mm/yr) → biome id.
    // Bands are half-open [min, max). First match wins so order matters:
    // colder / more-specific rules first, temperate fallbacks last.
    private static readonly (float tMin, float tMax, float rMin, float rMax, byte biomeId)[] Rules =
    {
        // Very cold — snow dominates regardless of rainfall.
        (float.MinValue, -10f, 0f,              float.MaxValue, BiomeBuiltins.SnowId),

        // Cold band — tundra dry / taiga wet.
        (-10f,  2f, 0f,    400f,            BiomeBuiltins.TundraId),
        (-10f,  2f, 400f,  float.MaxValue,  BiomeBuiltins.TaigaId),

        // Temperate band — grassland dry, forest wet.
        (2f,   18f, 0f,    500f,            BiomeBuiltins.GrasslandId),
        (2f,   18f, 500f,  float.MaxValue,  BiomeBuiltins.TemperateForestId),

        // Hot band — desert dry, savanna mid, jungle wet. Savanna band
        // narrowed repeatedly to keep hot regions reading mostly as desert
        // or jungle with a thin savanna transition between.
        (18f,  float.MaxValue, 0f,    1100f,          BiomeBuiltins.DesertId),
        (18f,  float.MaxValue, 1100f, 1500f,          BiomeBuiltins.SavannaId),
        (18f,  float.MaxValue, 1500f, float.MaxValue, BiomeBuiltins.JungleId),
    };

    public static byte Pick(float tempC, float rainMm)
    {
        foreach (var r in Rules)
        {
            if (tempC >= r.tMin && tempC < r.tMax
                && rainMm >= r.rMin && rainMm < r.rMax)
                return r.biomeId;
        }
        return BiomeBuiltins.UnknownId;
    }
}
