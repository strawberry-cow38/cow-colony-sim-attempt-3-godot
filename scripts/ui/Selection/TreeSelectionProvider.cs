using System.Collections.Generic;
using Godot;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Crops;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI.Selection;

/// <summary>
/// Picks <see cref="Crop"/> entities (trees for now) out of the ECS via a
/// ray-vs-AABB slab test. Each live crop contributes one AABB sized to the
/// tile it sits on and its current growth-scaled model height.
///
/// Stashes the picked Entity in <see cref="SelectionTarget.Payload"/> so the
/// Chop/Cancel actions can mutate <c>Crop.MarkedJobId</c> without searching
/// the world again on every click.
/// </summary>
public sealed class TreeSelectionProvider : ISelectionProvider
{
    private readonly SimHost _host;

    public TreeSelectionProvider(SimHost host) { _host = host; }

    public bool TryPick(Vector3 rayOrigin, Vector3 rayDirection,
        out SelectionTarget target, out float distance)
    {
        // Capture into a struct instead of individual locals so the Fennecs
        // .For lambda can mutate best-* without fighting C#'s "can't capture
        // out parameter in a lambda" rule.
        var best = new PickState { Distance = float.MaxValue };
        var origin = rayOrigin;
        var dir = rayDirection;

        _host.World.Stream<Crop, CropTile>().For((in Entity e, ref Crop crop, ref CropTile tile) =>
        {
            var def = CropRegistry.Get(crop.KindId);
            if (def.Id == CropRegistry.NoCrop) return;
            var aabb = TreeBounds(def, tile.Pos, crop.Growth);
            if (!RayAabb(origin, dir, aabb, out var t)) return;
            if (t >= best.Distance) return;
            best.Distance = t;
            best.Bounds = aabb;
            best.Entity = e;
            best.Kind = crop.KindId;
            best.Growth = crop.Growth;
            best.Marked = crop.MarkedJobId;
            best.Tile = tile.Pos;
            best.Found = true;
        });

        if (!best.Found)
        {
            target = null!;
            distance = float.MaxValue;
            return false;
        }

        var defPicked = CropRegistry.Get(best.Kind);
        var actions = BuildActions(best.Entity, best.Kind, best.Tile, best.Marked);
        target = new SelectionTarget(
            Name: defPicked.Name,
            Description: BuildDescription(defPicked, best.Growth, best.Marked),
            Bounds: best.Bounds,
            Actions: actions,
            Payload: best.Entity);
        distance = best.Distance;
        return true;
    }

    private struct PickState
    {
        public float Distance;
        public Aabb Bounds;
        public Entity Entity;
        public byte Kind;
        public float Growth;
        public int Marked;
        public TilePos Tile;
        public bool Found;
    }

    private static string BuildDescription(CropDef def, float growth, int marked)
    {
        var pct = Mathf.RoundToInt(Mathf.Clamp(growth, 0f, 1f) * 100f);
        var yield = BuiltinCrops.YieldOf(def, growth);
        var mark = marked != 0 ? "\nMarked for chop" : string.Empty;
        return $"Growth: {pct}%\nYield at harvest: {yield} wood{mark}";
    }

    private IReadOnlyList<SelectionAction> BuildActions(Entity entity, byte kindId, TilePos tile, int currentMark)
    {
        if (currentMark != 0)
        {
            return new[]
            {
                new SelectionAction("Cancel", () => SetMarked(tile, 0)),
            };
        }
        return new[]
        {
            new SelectionAction("Chop", () => SetMarked(tile, 1)),
        };
    }

    private void SetMarked(TilePos tile, int mark)
    {
        // Re-lookup by tile position — the Entity handle captured at pick time
        // could be stale if the crop despawned between pick and click. Tile
        // position is unique per crop (one crop per tile).
        _host.World.Stream<Crop, CropTile>().For((in Entity e, ref Crop crop, ref CropTile ct) =>
        {
            if (ct.Pos.X != tile.X || ct.Pos.Y != tile.Y || ct.Pos.Z != tile.Z) return;
            crop.MarkedJobId = mark;
        });
    }

    private static Aabb TreeBounds(CropDef def, TilePos tile, float growth)
    {
        var tw = SimConstants.TileWidthMeters;
        var th = SimConstants.TileHeightMeters;
        var growthScale = Mathf.Lerp(0.25f, 1.0f, growth);
        var targetH = def.ModelHeightMeters > 0
            ? def.ModelHeightMeters
            : def.TrunkHeightMeters + def.CanopyHeightMeters;
        var h = targetH * growthScale;
        var x = tile.X * tw;
        var z = tile.Z * tw;
        var y = tile.Y * th;
        return new Aabb(new Vector3(x, y, z), new Vector3(tw, h, tw));
    }

    /// <summary>Slab test. Returns true with the nearest positive t where the
    /// ray enters the box. Handles axis-aligned ray directions (inf slopes)
    /// via the standard <c>1/0 = inf</c> IEEE behaviour.</summary>
    private static bool RayAabb(Vector3 origin, Vector3 dir, Aabb box, out float tEnter)
    {
        tEnter = 0f;
        var min = box.Position;
        var max = box.Position + box.Size;
        float tmin = float.NegativeInfinity;
        float tmax = float.PositiveInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            var o = origin[axis];
            var d = dir[axis];
            var lo = min[axis];
            var hi = max[axis];
            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi) return false;
                continue;
            }
            var inv = 1f / d;
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            if (tmin > tmax) return false;
        }
        if (tmax < 0f) return false;
        tEnter = tmin > 0f ? tmin : tmax;
        return true;
    }
}
