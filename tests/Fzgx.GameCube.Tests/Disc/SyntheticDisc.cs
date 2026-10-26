using System.Buffers.Binary;
using System.Text;

namespace Fzgx.GameCube.Tests.Disc;

/// <summary>
/// Builds tiny GameCube disc images in memory for tests: a header, a file
/// system table and the given files. No game data is involved.
/// </summary>
internal static class SyntheticDisc
{
    private const int FstOffset = 0x2440;

    public static byte[] Build(string gameId, params (string Path, byte[] Data)[] files)
    {
        // Build the directory tree as FST entries in depth-first order.
        var entries = new List<(bool IsDirectory, string Name, int Parent, uint A, uint B, string? Path)>
        {
            (true, "", 0, 0, 0, null),
        };
        AddDirectory("", 0, files.Select(f => f.Path).ToList(), entries);

        var names = new MemoryStream();
        var nameOffsets = new List<int>();
        foreach (var entry in entries)
        {
            nameOffsets.Add((int)names.Length);
            if (entry.Name.Length > 0)
            {
                names.Write(Encoding.ASCII.GetBytes(entry.Name));
                names.WriteByte(0);
            }
        }
        var fstSize = (entries.Count * 12) + (int)names.Length;
        var dataStart = Align(FstOffset + fstSize, 0x100);

        var dataOffsets = new Dictionary<string, (uint Offset, int Length)>();
        var cursor = dataStart;
        foreach (var (path, data) in files)
        {
            dataOffsets[path] = ((uint)cursor, data.Length);
            cursor = Align(cursor + data.Length, 0x20);
        }

        var image = new byte[cursor];
        Encoding.ASCII.GetBytes(gameId).CopyTo(image, 0);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x1C), 0xC2339F3D);
        Encoding.ASCII.GetBytes("SYNTHETIC TEST DISC").CopyTo(image, 0x20);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x424), FstOffset);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x428), (uint)fstSize);

        for (var i = 0; i < entries.Count; i++)
        {
            var (isDirectory, _, parent, a, b, path) = entries[i];
            var at = FstOffset + (i * 12);
            image[at] = (byte)(isDirectory ? 1 : 0);
            image[at + 1] = (byte)(nameOffsets[i] >> 16);
            BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(at + 2), (ushort)nameOffsets[i]);
            if (i == 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(at + 8), (uint)entries.Count);
            }
            else if (isDirectory)
            {
                BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(at + 4), (uint)parent);
                BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(at + 8), b);
            }
            else
            {
                var (offset, length) = dataOffsets[path!];
                BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(at + 4), offset);
                BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(at + 8), (uint)length);
            }
        }
        names.ToArray().CopyTo(image, FstOffset + (entries.Count * 12));
        foreach (var (path, data) in files)
        {
            data.CopyTo(image, dataOffsets[path].Offset);
        }
        return image;
    }

    private static void AddDirectory(
        string prefix,
        int parentIndex,
        List<string> paths,
        List<(bool IsDirectory, string Name, int Parent, uint A, uint B, string? Path)> entries)
    {
        var here = paths.Where(p => !p[prefix.Length..].Contains('/')).Order(StringComparer.Ordinal);
        foreach (var path in here)
        {
            entries.Add((false, path[prefix.Length..], parentIndex, 0, 0, path));
        }
        var subdirectories = paths
            .Where(p => p[prefix.Length..].Contains('/'))
            .Select(p => p[prefix.Length..].Split('/')[0])
            .Distinct()
            .Order(StringComparer.Ordinal);
        foreach (var name in subdirectories)
        {
            var index = entries.Count;
            entries.Add((true, name, parentIndex, 0, 0, null));
            var childPrefix = $"{prefix}{name}/";
            AddDirectory(childPrefix, index, paths.Where(p => p.StartsWith(childPrefix, StringComparison.Ordinal)).ToList(), entries);
            entries[index] = entries[index] with { B = (uint)entries.Count };
        }
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
