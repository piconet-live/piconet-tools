using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Fzgx.GameCube.Textures;

namespace Fzgx.GameCube.Tests.Textures;

public class PngWriterTests
{
    private static readonly byte[] _twoByOne = [255, 0, 0, 255, 0, 0, 255, 128];

    private static List<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[], uint)>();
        var at = 8;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            chunks.Add((type, png[(at + 8)..(at + 8 + length)], BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length))));
            at += 12 + length;
        }
        return chunks;
    }

    [Fact]
    public void Writes_signature_header_data_and_end_chunks()
    {
        var png = PngWriter.Encode(2, 1, _twoByOne);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var header = chunks[0].Data;
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal([8, 6, 0, 0, 0], header[8..]);
    }

    [Fact]
    public void Chunk_checksums_are_standard_crc32()
    {
        // Every PNG's empty IEND chunk has this well-known CRC.
        var end = Chunks(PngWriter.Encode(2, 1, _twoByOne))[^1];

        Assert.Equal(0xAE426082u, end.Crc);
    }

    [Fact]
    public void Image_data_holds_unfiltered_rows_of_the_pixels()
    {
        var data = Chunks(PngWriter.Encode(2, 1, _twoByOne))[1].Data;
        using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);

        Assert.Equal([0, .. _twoByOne], raw.ToArray());
    }

    [Fact]
    public void Rejects_pixel_data_of_the_wrong_size()
    {
        Assert.Throws<ArgumentException>(() => PngWriter.Encode(2, 2, _twoByOne));
    }
}
