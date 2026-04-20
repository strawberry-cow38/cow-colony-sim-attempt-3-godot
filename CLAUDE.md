# Cow Colony Sim (attempt 3) — Architecture Notes

Godot 4.6.2 + .NET 8 + C# + Fennecs 0.6 ECS. Multi-project:
- `Sim/` — pure C# classlib, no Godot types; the simulation kernel.
- `scripts/` — Godot scene-graph glue (renderers, input, host node).
- `Tests/` — xunit suite over `Sim/`.

## Threading discipline

Heavy sim systems are split **Compute (parallel, pure) → Apply (serial, mutating)**.
Compute reads inputs, writes its own output buffer. Apply drains those
buffers on the main thread and mutates ECS / `TileWorld` / Godot scene.

Examples already wired this way:
- `Sim/Systems/PathPlanSystem.cs` — `Parallel.For` over A* requests, then
  serial loop applies `PathCurrent` via `entity.Add`.
- `Sim/Grid/WorldGen.cs` — `Parallel.For` samples noise into `int[,]`
  heightmap, then serial loop writes tiles.
- `scripts/render/GridRenderer.cs` — `Task.Run` builds `MeshBuildResult`
  per dirty chunk; main `_Process` drains a `ConcurrentQueue` and
  assembles `ArrayMesh` on the main thread (Godot API requirement).

### Rules

1. Any new "heavy" system (A*, meshing, noise, bulk tile ops) must keep
   compute pure — no ECS mutation, no shared mutable state — so it can
   be moved into a worker thread without rewrite.
2. Never mutate Fennecs entities from a worker. Apply serially.
3. Never call Godot APIs from a worker. Build arrays / POCOs, assemble
   on main thread.
4. `TileWorld.Get` is safe for concurrent reads **as long as nothing
   writes during that phase**. Schedule tile writes into one serial
   phase per tick.
5. Systems that use shared RNG (e.g. `WanderSystem`) stay single-threaded
   until they're refactored to carry per-entity RNG.
6. Sim tick ordering stays deterministic for tests/saves. Parallelism
   lives inside a system, never across tick boundaries.

## Tile model

Anisotropic cubes: **1.5m × 0.75m × 1.5m** (W × H × L). Constants in
`Sim/Constants.cs`:
- `TileWidthMeters = 1.5f`, `TileHeightMeters = 0.75f`
- `HeadroomTiles = 2` — colonist (~1.5m) needs 2 empty tiles above feet.

A* climbs dy = ±1 (0.75m step). One tile = knee-height step; two tiles
stacked = cow-height wall.

## Tier model

`Sim/Grid/ChunkState.cs`: `Dormant < Ambient < Live`. Players mark
regions with `ClaimedRegion(min, max, minTier)`. Roaming entities add
`LiveAnchor`. `ChunkTierSystem` unions them, dilates LIVE chunks into an
AMBIENT halo, writes the per-chunk map. Rate switching (LIVE=60Hz /
AMBIENT=1Hz / DORMANT=frozen) is phase 4b, not yet implemented.
