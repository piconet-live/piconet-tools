using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Text;
using Fzgx.GameCube.Io;

namespace Fzgx.GameCube.Disc;

/// <summary>A file inside a disc image.</summary>
/// <param name="Path">Path from the disc root with '/' separators, e.g. <c>stage/st01.tpl.lz</c>.</param>
/// <param name="Offset">Byte offset in the image.</param>
/// <param name="Length">Size in bytes.</param>
public readonly record struct DiscFile(string Path, long Offset, int Length);

/// <summary>
/// An uncompressed GameCube disc image (.iso / .gcm): its header and file
/// system table (FST). Files are read on demand from the underlying stream.
/// </summary>
/// <remarks>
/// Layout: game ID at 0x000, disc magic 0xC2339F3D at 0x01C, title at 0x020,
/// FST offset and size at 0x424 and 0x428. FST entries are 12 bytes: a flag
/// byte (1 = directory), a 24-bit name offset into the string table that
/// follows the entries, then for files the data offset and length, for
/// directories the parent index and the index just past their last child.
/// </remarks>
public sealed class GcmDisc : IDisposable
{
    /// <summary>The GameCube disc magic, big-endian at offset 0x1C.</summary>
    public const uint DiscMagic = 0xC2339F3D;

    private const int HeaderSize = 0x440;
    private const int FstEntrySize = 12;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly FrozenDictionary<string, DiscFile> _files;

    /// <summary>Reads the header and file table from <paramref name="stream"/>, which must be seekable.</summary>
    public GcmDisc(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The disc stream must be readable and seekable.", nameof(stream));
        }
        _stream = stream;
        _leaveOpen = leaveOpen;

        var header = ReadAt(0, HeaderSize);
        if (BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x1C)) != DiscMagic)
        {
            throw new InvalidDataException("Not a GameCube disc image (disc magic missing at 0x1C).");
        }
        GameId = Encoding.ASCII.GetString(header, 0, 6);
        Title = ReadTitle(header);
        var fstOffset = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x424));
        var fstSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x428));
        if (fstSize < FstEntrySize || fstOffset + (long)fstSize > stream.Length)
        {
            throw new InvalidDataException($"The file table (0x{fstOffset:X}, {fstSize} bytes) lies outside the image.");
        }
        _files = ParseFst(ReadAt(fstOffset, (int)fstSize), stream.Length)
            .ToFrozenDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Opens a disc image file.</summary>
    public static GcmDisc Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16));

    /// <summary>The six-character game ID, e.g. <c>GFZE01</c> for F-Zero GX (NTSC-U).</summary>
    public string GameId { get; }

    /// <summary>The game title from the disc header.</summary>
    public string Title { get; }

    /// <summary>Every file on the disc.</summary>
    public IEnumerable<DiscFile> Files => _files.Values.OrderBy(file => file.Path, StringComparer.Ordinal);

    /// <summary>True if the disc has a file at <paramref name="path"/> (case-insensitive).</summary>
    public bool Contains(string path) => _files.ContainsKey(path);

    /// <summary>Reads a whole file; throws <see cref="FileNotFoundException"/> if it does not exist.</summary>
    public byte[] ReadFile(string path)
    {
        if (!_files.TryGetValue(path, out var file))
        {
            throw new FileNotFoundException($"The disc has no file '{path}'.", path);
        }
        return ReadAt(file.Offset, file.Length);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    private byte[] ReadAt(long offset, int count)
    {
        var buffer = new byte[count];
        _stream.Position = offset;
        _stream.ReadExactly(buffer);
        return buffer;
    }

    private static string ReadTitle(byte[] header)
    {
        var title = header.AsSpan(0x20, 0x3E0);
        var end = title.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? title : title[..end]).Trim();
    }

    private static List<DiscFile> ParseFst(byte[] fst, long imageLength)
    {
        var reader = new BigEndianReader(fst);
        reader.Position = 8;
        var count = reader.ReadUInt32(); // root entry: "next" is the total entry count
        if (count == 0 || count * (long)FstEntrySize > fst.Length)
        {
            throw new InvalidDataException($"The file table claims {count} entries but holds {fst.Length} bytes.");
        }
        var namesStart = (int)count * FstEntrySize;

        var files = new List<DiscFile>();
        var directories = new Stack<(string Path, uint End)>();
        directories.Push(("", count));
        for (uint index = 1; index < count; index++)
        {
            while (index >= directories.Peek().End)
            {
                directories.Pop();
            }
            reader.Position = (int)index * FstEntrySize;
            var isDirectory = reader.ReadByte() != 0;
            var nameOffset = (reader.ReadByte() << 16) | reader.ReadUInt16();
            var first = reader.ReadUInt32();
            var second = reader.ReadUInt32();

            reader.Position = namesStart + nameOffset;
            var name = reader.ReadCString();
            var path = directories.Peek().Path.Length == 0 ? name : $"{directories.Peek().Path}/{name}";

            if (isDirectory)
            {
                if (second <= index || second > count)
                {
                    throw new InvalidDataException($"Directory '{path}' has an invalid end index {second}.");
                }
                directories.Push((path, second));
            }
            else
            {
                if (first + (long)second > imageLength)
                {
                    throw new InvalidDataException($"File '{path}' lies outside the image.");
                }
                files.Add(new DiscFile(path, first, (int)second));
            }
        }
        return files;
    }
}
