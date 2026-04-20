# cow-colony-sim-attempt-3-godot

Third attempt at the 3D cow colony sim. Fresh start in **Godot 4** with **C#** for
the sim core, moving off the three.js / vanilla-JS stack from attempt 2 before
the codebase gets too entrenched to redirect.

Design carries over from attempt 2 (see `docs/DESIGN.md` — to be ported). Code
starts from zero.

## Stack

- **Engine:** Godot 4.3+ (Vulkan renderer on desktop, GL compatibility on web if/when needed)
- **Language:** C# for sim core (ECS, systems, hot loops). GDScript for UI glue + editor tooling.
- **Rendering:** Godot's built-in Vulkan pipeline — proper OIT, shadows, post-fx, GI without hand-rolling.
- **Threading:** real threads via `Task` / `Parallel.For` for cow brains + pathfinding + job board.
- **Save:** Godot `Resource` serialization (binary or JSON) — no hand-rolled gzip+migrations.

## Why the rewrite

Attempt 2 works (1000 cows @ 6 ms/tick, 104 fps on mobile) but is built on:
- single-thread JS main loop
- three.js renderer (no Vulkan, manual OIT via vendored `three-wboit`, hand-built postprocessing)
- hand-rolled archetype ECS + hand-rolled tiered scheduler
- 37-step save migration chain

None of that is bad; all of it is a ceiling. Godot + C# lets us:
- spend time on **game design** instead of engine plumbing
- scale past 1k cows on real threads
- use Vulkan-grade rendering / shadows / post-fx out of the box
- export mobile natively (APK / iOS) instead of fighting browser perf

## Ported from attempt 2

Design + data, not code:

- Tile math: **1 tile = 1.5 m = 43 Godot units** (from attempt 2 `coords.js`)
- Name scheme: Title / First / Nickname / Last
- Skills + traits + backstories (to be re-exported as JSON data)
- GLB models: pine, maple, boulders (3 shapes × 3 tints), bed, corn, bush
- Partial wall tier system (4 quarters + absolute baseFill)
- Zone model: stockpiles + farm zones
- Job categories: chop / cut / haul / mine / till / plant / harvest / cook / smelt / paint / build / deconstruct
- Bill system w/ output destination routing
- Meal quality 0-4 tiers driven by cooking skill roll

## Repo layout (planned)

```
addons/                 # third-party Godot addons if any
assets/
  models/               # GLBs imported from attempt-2 Blender work
  textures/
  audio/
scenes/                 # .tscn scene files
  main.tscn
  ui/
  world/
scripts/
  core/                 # C# ECS + scheduler
  world/                # tile grid, pathfinding
  systems/              # cow brain, jobs, growth, etc.
  ui/                   # GDScript UI glue
docs/
  DESIGN.md             # ported PLAN.md + ARCHITECTURE.md
  STATE.md
```

## Status

**Day 0.** Repo just initialized. Godot project not yet created.
