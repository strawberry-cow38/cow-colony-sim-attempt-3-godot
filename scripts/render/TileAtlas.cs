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
    public const int AtlasPx = 2048;
    public const float CellU = 1f / Cols;
    public const float CellV = 1f / Rows;

    // Inset UVs by this many atlas pixels to avoid mipmap bleeding between
    // adjacent cells. 8px roughly matches a mip level 3 footprint.
    private const float InsetPx = 8f;
    private const float InsetU = InsetPx / AtlasPx;

    private static readonly int[] GrassCells = { 0, 1, 2, 3, 4, 5, 6 };
    private const int DirtCell = 10;
    private const int RockCell = 13;
    private const int SandCell = 15;
    // Cell 9 in the atlas is a near-white fallback. Pairing it with a tint
    // gives a clean colored surface — sand or dirt cells would muddy the
    // multiplied result.
    private const int WhiteCell = 9;

    public static int CellForTop(TileKind kind, int wx, int wz)
    {
        return kind switch
        {
            TileKind.Floor => GrassCells[Hash(wx, wz) % GrassCells.Length],
            TileKind.Solid => DirtCell,
            TileKind.Water => WhiteCell,
            TileKind.Sand  => SandCell,
            _ => DirtCell,
        };
    }

    public static int CellForSide(TileKind kind)
    {
        return kind switch
        {
            TileKind.Floor => DirtCell,
            TileKind.Solid => RockCell,
            TileKind.Water => WhiteCell,
            TileKind.Sand  => SandCell,
            _ => DirtCell,
        };
    }

    // Vertex-color tint multiplied with the albedo texture (material has
    // vertex_color_use_as_albedo=true). Used to re-color shared atlas cells
    // per TileKind without baking extra cells. Water rides on the white
    // atlas cell so the multiply lands on a clean blue. Alpha < 1 so lake
    // beds show through — needs GridRenderer's StandardMaterial3D to carry
    // TransparencyEnum.Alpha for the channel to be honored.
    public static Color TintFor(TileKind kind) => kind switch
    {
        TileKind.Water => new Color(0.30f, 0.50f, 0.85f, 0.55f),
        _ => Colors.White,
    };

    public static (float u0, float v0, float u1, float v1) CellUV(int cell)
    {
        var col = cell % Cols;
        var row = cell / Cols;
        var u0 = col * CellU + InsetU;
        var v0 = row * CellV + InsetU;
        var u1 = (col + 1) * CellU - InsetU;
        var v1 = (row + 1) * CellV - InsetU;
        return (u0, v0, u1, v1);
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
