using Fzgx.GameCube.Io;

namespace Fzgx.GameCube.Tests.Io;

public class BigEndianReaderTests
{
    [Fact]
    public void Reads_values_most_significant_byte_first()
    {
        byte[] data = [0x12, 0x34, 0xFF, 0xFE, 0x00, 0x00, 0x00, 0x2A, 0x3F, 0x80, 0x00, 0x00, 0xAB];
        var reader = new BigEndianReader(data);

        Assert.Equal(0x1234, reader.ReadUInt16());
        Assert.Equal(-2, reader.ReadInt16());
        Assert.Equal(42u, reader.ReadUInt32());
        Assert.Equal(1.0f, reader.ReadSingle());
        Assert.Equal(0xAB, reader.ReadByte());
        Assert.Equal(data.Length, reader.Position);
    }

    [Fact]
    public void Reads_zero_terminated_strings()
    {
        var reader = new BigEndianReader("stage\0bg\0"u8.ToArray());

        Assert.Equal("stage", reader.ReadCString());
        Assert.Equal("bg", reader.ReadCString());
    }

    [Fact]
    public void Reading_past_the_end_names_the_offset()
    {
        var reader = new BigEndianReader(new byte[3]) { Position = 2 };

        var error = Assert.Throws<EndOfStreamException>(() => reader.ReadUInt32());
        Assert.Contains("0x2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unterminated_strings_are_rejected()
    {
        Assert.Throws<EndOfStreamException>(() => new BigEndianReader("abc"u8.ToArray()).ReadCString());
    }
}
