using System.Buffers.Binary;
using System.Text;
using Fzgx.GameCube.Compression;

namespace Fzgx.GameCube.Tests.Compression;

public class AvLzTests
{
    private static byte[] File(byte[] body, int uncompressedSize, bool headerCounted = false)
    {
        var file = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(file, body.Length + (headerCounted ? 8 : 0));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), uncompressedSize);
        body.CopyTo(file, 8);
        return file;
    }

    // Flag byte 0x01: item 0 is a literal 'A', item 1 a match. The match
    // copies 5 bytes from ring position 4078 (0xFEE), where the literal was
    // written: low byte 0xEE, then high nibble 0xF and length 5 - 3 = 2.
    // The copy overlaps what it writes, giving "AAAAAA".
    private static readonly byte[] _handBuilt = [0x01, (byte)'A', 0xEE, 0xF2];

    [Fact]
    public void Decodes_a_hand_built_stream_with_an_overlapping_match()
    {
        var output = AvLz.Decompress(File(_handBuilt, 6));

        Assert.Equal("AAAAAA", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void Accepts_headers_that_count_their_own_eight_bytes()
    {
        var output = AvLz.Decompress(File(_handBuilt, 6, headerCounted: true));

        Assert.Equal(6, output.Length);
    }

    [Fact]
    public void Matches_may_read_the_zero_filled_ring_before_any_output()
    {
        // A match from position 0 of the fresh ring copies zeros.
        var output = AvLz.Decompress(File([0x00, 0x00, 0x0F], 18));

        Assert.Equal(new byte[18], output);
    }

    [Fact]
    public void Rejects_a_size_field_that_matches_neither_convention()
    {
        var file = File(_handBuilt, 6);
        file[0] = 99;

        Assert.Throws<InvalidDataException>(() => AvLz.Decompress(file));
    }

    [Fact]
    public void Rejects_output_of_the_wrong_length()
    {
        Assert.Throws<InvalidDataException>(() => AvLz.Decompress(File(_handBuilt, 7)));
    }

    [Fact]
    public void Rejects_a_truncated_match()
    {
        Assert.Throws<InvalidDataException>(() => AvLz.Decompress(File([0x00, 0xEE], 3)));
    }

    [Fact]
    public void Rejects_files_shorter_than_the_header()
    {
        Assert.Throws<InvalidDataException>(() => AvLz.Decompress(new byte[5]));
    }

    public static TheoryData<string, byte[]> RoundTripCases()
    {
        var random = new Random(1234);
        var noise = new byte[5000];
        random.NextBytes(noise);
        var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("F-ZERO GX COURSE DATA ", 400)));
        var wrapping = Enumerable.Range(0, 12_000).Select(i => (byte)((i * 7) % 251)).ToArray();
        return new TheoryData<string, byte[]>
        {
            { "empty", [] },
            { "one byte", [42] },
            { "zeros (match the initial ring)", new byte[300] },
            { "one repeated byte (overlapping copies)", Enumerable.Repeat((byte)0xAB, 1000).ToArray() },
            { "random (incompressible)", noise },
            { "repetitive text", text },
            { "longer than the ring buffer", wrapping },
        };
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Compress_then_decompress_returns_the_input(string name, byte[] data)
    {
        Assert.NotNull(name);
        var file = AvLz.Compress(data);

        Assert.Equal(data, AvLz.Decompress(file));
        Assert.Equal(file.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(file)); // F-Zero GX header
        Assert.Equal(data.Length, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4)));
    }

    [Fact]
    public void Repetitive_data_actually_compresses()
    {
        var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("F-ZERO GX COURSE DATA ", 400)));

        Assert.True(AvLz.Compress(text).Length < text.Length / 5);
    }
}
