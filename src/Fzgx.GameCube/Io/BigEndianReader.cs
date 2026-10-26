using System.Buffers.Binary;
using System.Text;

namespace Fzgx.GameCube.Io;

/// <summary>
/// Sequential big-endian reads over a byte buffer. GameCube data is
/// big-endian throughout. Reads past the end throw
/// <see cref="EndOfStreamException"/> naming the offset.
/// </summary>
public sealed class BigEndianReader
{
    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>Reads <paramref name="data"/> from its start.</summary>
    public BigEndianReader(ReadOnlyMemory<byte> data) => _data = data;

    /// <summary>Current offset from the start of the buffer.</summary>
    public int Position { get; set; }

    /// <summary>Total length of the buffer.</summary>
    public int Length => _data.Length;

    /// <summary>Reads one unsigned byte.</summary>
    public byte ReadByte() => Take(1)[0];

    /// <summary>Reads an unsigned 16-bit value.</summary>
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));

    /// <summary>Reads a signed 16-bit value.</summary>
    public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Take(2));

    /// <summary>Reads an unsigned 32-bit value.</summary>
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));

    /// <summary>Reads a signed 32-bit value.</summary>
    public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

    /// <summary>Reads a 32-bit IEEE float.</summary>
    public float ReadSingle() => BinaryPrimitives.ReadSingleBigEndian(Take(4));

    /// <summary>Reads <paramref name="count"/> raw bytes.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count) => Take(count);

    /// <summary>Reads a zero-terminated ASCII string, leaving the position after the terminator.</summary>
    public string ReadCString()
    {
        var span = _data.Span[Position..];
        var end = span.IndexOf((byte)0);
        if (end < 0)
        {
            throw new EndOfStreamException($"Unterminated string at offset 0x{Position:X}.");
        }
        Position += end + 1;
        return Encoding.ASCII.GetString(span[..end]);
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || Position < 0 || Position > _data.Length - count)
        {
            throw new EndOfStreamException($"Reading {count} bytes at offset 0x{Position:X} passes the end (0x{_data.Length:X}).");
        }
        var span = _data.Span.Slice(Position, count);
        Position += count;
        return span;
    }
}
