using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

// 4×4 grid of 512px cells (see tools/bake-grass-atlas.py in old repo).
//   0..6   grass variants
//   7..8   purple stone
//   9      white fallback
//   10..11 dirt
//   12..14 orange cliff rock
//   15     sand
public static class TileAtlas
{
    public const int Cols = 4;
    public const int Rows = 4;
    public const float CellU = 1f / Cols;
    public const float CellV = 1f / Rows;

    private static readonly int[] GrassCells = { 0, 1, 2, 3, 4, 5, 6 };
    private const int DirtCell = 10;
    private const int RockCell = 13;

    public static int CellForTop(TileKind kind, int wx, int wz)
    {
        return kind switch
        {
            TileKind.Floor => GrassCells[Hash(wx, wz) % GrassCells.Length],
            TileKind.Solid => DirtCell,
            _ => DirtCell,
        };
    }

    public static int CellForSide(TileKind kind)
    {
        return kind switch
        {
            TileKind.Floor => DirtCell,
            TileKind.Solid => RockCell,
            _ => DirtCell,
        };
    }

    public static (float u0, float v0, float u1, float v1) CellUV(int cell)
    {
        var col = cell % Cols;
        var row = cell / Cols;
        var u0 = col * CellU;
        var v0 = row * CellV;
        return (u0, v0, u0 + CellU, v0 + CellV);
    }

    private static int Hash(int x, int z)
    {
        unchecked
        {
            var h = (uint)(x * 374761393) ^ (uint)(z * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
