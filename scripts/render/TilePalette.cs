using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public static class TilePalette
{
    public static Color ColorOf(TileKind kind) => kind switch
    {
        TileKind.Solid => new Color(0.55f, 0.52f, 0.48f),
        TileKind.Floor => new Color(0.45f, 0.62f, 0.32f),
        _ => new Color(1, 0, 1),
    };
}
