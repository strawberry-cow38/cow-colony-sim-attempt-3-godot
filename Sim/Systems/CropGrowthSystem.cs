using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Crops;

namespace CowColonySim.Sim.Systems;

/// <summary>
/// Advances <see cref="Crop.Growth"/> toward 1.0 at the rate defined by each
/// crop kind's <see cref="CropDef.GrowthTicksToMature"/>. Runs every sim
/// tick but the per-crop advance is tiny — full maturity is ~thousands to
/// hundreds-of-thousands of ticks.
///
/// Framework intent: same system drives tree growth, wheat growth, berry
/// regrowth. Harvest systems read <see cref="Crop.Growth"/> to decide yield.
/// </summary>
public static class CropGrowthSystem
{
    public static void Step(fennecs.World world, int ticksElapsed = 1)
    {
        world.Stream<Crop>().For((ref Crop c) =>
        {
            if (c.Growth >= 1f) return;
            var def = CropRegistry.Get(c.KindId);
            if (def.GrowthTicksToMature <= 0) return;
            var delta = (float)ticksElapsed / def.GrowthTicksToMature;
            c.Growth = Math.Min(1f, c.Growth + delta);
        });
    }
}
