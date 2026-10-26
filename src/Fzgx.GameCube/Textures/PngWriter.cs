using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Fzgx.GameCube.Textures;

/// <summary>Writes 8-bit RGBA images as PNG files, so decoded textures can be cached and viewed.</summary>
public static class PngWriter
{
    private static readonly byte[] _signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] _crcTable = BuildCrcTable();

    /// <summary>Encodes <paramref name="rgba"/> (width * height * 4 bytes, rows top to bottom).</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes of RGBA, got {rgba.Length}.", nameof(rgba));
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // colour type: RGBA
        // compression, filter and interlace methods: 0

        // Each row starts with filter type 0 (none).
        var raw = new byte[height * ((width * 4) + 1)];
        for (var y = 0; y < height; y++)
        {
            rgba.Slice(y * width * 4, width * 4).CopyTo(raw.AsSpan((y * ((width * 4) + 1)) + 1));
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write(_signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(field, data.Length);
        output.Write(field);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(Crc32(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(field, crc);
        output.Write(field);
    }

    private static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
