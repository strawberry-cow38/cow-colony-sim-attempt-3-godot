using System.IO;
using System.IO.Compression;

namespace CowColonySim.Sim.Grid;

/// <summary>
/// Per-cell on-disk persistence for evicted (Cold-tier) chunks. Each cell
/// gets one gzip-compressed file whose payload is a header + chunk blobs.
///
/// File format (gzip-wrapped):
///   [magic  : u32 = 0xC0FFCE11]
///   [version: u32 = 1]
///   [cellX  : i32] [cellZ  : i32]
///   [chunkCt: u32]
/// Per chunk:
///   [keyX: i32] [keyY: i32] [keyZ: i32]
///   [revision: i32]
///   [tiles  : <see cref="Chunk.Volume"/> bytes, one <see cref="TileKind"/> per tile]
///
/// Bump version when Tile grows beyond the Kind byte.
/// </summary>
public sealed class CellStore
{
    public const uint Magic = 0xC0FFCE11u;
    public const uint Version = 1u;

    private readonly string _rootDir;

    public CellStore(string rootDir)
    {
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    public string PathFor(CellKey key)
        => Path.Combine(_rootDir, $"cell_{key.X}_{key.Z}.bin.gz");

    public bool Exists(CellKey key) => File.Exists(PathFor(key));

    public void Delete(CellKey key)
    {
        var p = PathFor(key);
        if (File.Exists(p)) File.Delete(p);
    }

    public void Save(CellKey key, IReadOnlyList<(TilePos ChunkKey, Chunk Chunk)> chunks)
    {
        using var fs = File.Create(PathFor(key));
        using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        using var bw = new BinaryWriter(gz);
        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(key.X);
        bw.Write(key.Z);
        bw.Write((uint)chunks.Count);

        var buf = new byte[Chunk.Volume];
        foreach (var (ck, chunk) in chunks)
        {
            bw.Write(ck.X);
            bw.Write(ck.Y);
            bw.Write(ck.Z);
            bw.Write(chunk.Revision);
            chunk.WriteTileKindsTo(buf, 0);
            bw.Write(buf, 0, Chunk.Volume);
        }
    }

    public List<(TilePos ChunkKey, Chunk Chunk)> Load(CellKey key)
    {
        var path = PathFor(key);
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var br = new BinaryReader(gz);

        var magic = br.ReadUInt32();
        if (magic != Magic) throw new InvalidDataException($"bad magic 0x{magic:X}");
        var version = br.ReadUInt32();
        if (version != Version) throw new InvalidDataException($"unsupported version {version}");
        var cellX = br.ReadInt32();
        var cellZ = br.ReadInt32();
        if (cellX != key.X || cellZ != key.Z)
            throw new InvalidDataException($"cell mismatch: file holds ({cellX},{cellZ}), asked ({key.X},{key.Z})");
        var count = br.ReadUInt32();

        var result = new List<(TilePos, Chunk)>((int)count);
        var buf = new byte[Chunk.Volume];
        for (var i = 0u; i < count; i++)
        {
            var keyX = br.ReadInt32();
            var keyY = br.ReadInt32();
            var keyZ = br.ReadInt32();
            var rev = br.ReadInt32();
            var read = br.Read(buf, 0, Chunk.Volume);
            if (read != Chunk.Volume)
                throw new EndOfStreamException($"short chunk blob (read {read} of {Chunk.Volume})");
            result.Add((new TilePos(keyX, keyY, keyZ), Chunk.FromSerialized(buf, rev)));
        }
        return result;
    }
}
