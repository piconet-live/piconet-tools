using Fzgx.GameCube.Textures;

namespace Fzgx.GameCube.Tests.Textures;

public class GxTextureEncoderTests
{
    /// <summary>
    /// A 16x16 picture: a white diagonal band and a yellow corner square on
    /// black, laid out so no 4x4 block holds more than two colours (all CMPR
    /// can store exactly).
    /// </summary>
    private static byte[] Picture(int size = 16)
    {
        var rgba = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var at = ((y * size) + x) * 4;
                var (r, g, b) = Math.Abs(x - y) <= 2 ? (255, 255, 255) : x >= 12 && y < 4 ? (255, 255, 0) : (0, 0, 0);
                (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = ((byte)r, (byte)g, (byte)b, 255);
            }
        }
        return rgba;
    }

    [Fact]
    public void I4_round_trips_every_level_of_intensity()
    {
        var rgba = new byte[8 * 8 * 4];
        for (var i = 0; i < 64; i++)
        {
            rgba[(4 * i) + 0] = rgba[(4 * i) + 1] = rgba[(4 * i) + 2] = rgba[(4 * i) + 3] = (byte)(i % 16 * 17);
        }

        var encoded = GxTextureEncoder.Encode(GxTextureFormat.I4, 8, 8, rgba);
        var decoded = GxTextureDecoder.Decode(GxTextureFormat.I4, 8, 8, encoded);

        Assert.Equal(32, encoded.Length);
        Assert.Equal(rgba, decoded);
    }

    [Fact]
    public void Cmpr_round_trips_blocks_of_two_colours_exactly()
    {
        var rgba = Picture();

        var encoded = GxTextureEncoder.Encode(GxTextureFormat.CMPR, 16, 16, rgba);
        var decoded = GxTextureDecoder.Decode(GxTextureFormat.CMPR, 16, 16, encoded);

        Assert.Equal(GxTextureDecoder.LevelSize(GxTextureFormat.CMPR, 16, 16), encoded.Length);
        Assert.Equal(rgba, decoded);
    }

    [Fact]
    public void Cmpr_block_of_one_colour_needs_no_blending()
    {
        var rgba = Enumerable.Repeat(new byte[] { 255, 255, 0, 255 }, 64).SelectMany(p => p).ToArray();

        var decoded = GxTextureDecoder.Decode(GxTextureFormat.CMPR, 8, 8, GxTextureEncoder.Encode(GxTextureFormat.CMPR, 8, 8, rgba));

        Assert.Equal(rgba, decoded);
    }

    [Fact]
    public void Halving_averages_two_by_two_blocks()
    {
        byte[] rgba = [0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 255];

        var half = GxTextureEncoder.Halve(rgba, 2, 2);

        Assert.Equal([128, 128, 128, 255], half);
    }

    [Fact]
    public void Formats_without_an_encoder_are_refused()
    {
        Assert.False(GxTextureEncoder.CanEncode(GxTextureFormat.RGB5A3));
        Assert.Throws<NotSupportedException>(() => GxTextureEncoder.Encode(GxTextureFormat.RGB5A3, 4, 4, new byte[64]));
    }
}
