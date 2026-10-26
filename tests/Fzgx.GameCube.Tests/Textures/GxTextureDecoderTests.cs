using Fzgx.GameCube.Textures;

namespace Fzgx.GameCube.Tests.Textures;

public class GxTextureDecoderTests
{
    private static (byte R, byte G, byte B, byte A) Pixel(byte[] rgba, int width, int x, int y)
    {
        var at = ((y * width) + x) * 4;
        return (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]);
    }

    [Theory]
    [InlineData(GxTextureFormat.I4, 8, 8, 32)]
    [InlineData(GxTextureFormat.I8, 8, 8, 64)]
    [InlineData(GxTextureFormat.RGB565, 8, 8, 128)]
    [InlineData(GxTextureFormat.RGBA8, 8, 8, 256)]
    [InlineData(GxTextureFormat.CMPR, 16, 16, 128)]
    [InlineData(GxTextureFormat.CMPR, 4, 4, 32)] // padded to one whole 8x8 tile
    [InlineData(GxTextureFormat.RGB5A3, 5, 3, 64)] // 2x1 tiles
    public void Level_sizes_are_padded_to_whole_tiles(GxTextureFormat format, int width, int height, int bytes)
    {
        Assert.Equal(bytes, GxTextureDecoder.LevelSize(format, width, height));
    }

    [Fact]
    public void I8_copies_intensity_to_every_channel_in_tile_order()
    {
        // Two 8x4 tiles side by side: the second tile's first byte is pixel (8, 0).
        var data = new byte[64];
        data[0] = 10;   // (0, 0)
        data[9] = 20;   // (1, 1)
        data[32] = 30;  // (8, 0)

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.I8, 16, 4, data);

        Assert.Equal((10, 10, 10, 10), Pixel(rgba, 16, 0, 0));
        Assert.Equal((20, 20, 20, 20), Pixel(rgba, 16, 1, 1));
        Assert.Equal((30, 30, 30, 30), Pixel(rgba, 16, 8, 0));
    }

    [Fact]
    public void I4_reads_the_high_nibble_first()
    {
        var data = new byte[32];
        data[0] = 0xF1;

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.I4, 8, 8, data);

        Assert.Equal((255, 255, 255, 255), Pixel(rgba, 8, 0, 0));
        Assert.Equal((17, 17, 17, 17), Pixel(rgba, 8, 1, 0));
    }

    [Fact]
    public void IA4_and_IA8_split_alpha_and_intensity()
    {
        var ia4 = new byte[32];
        ia4[0] = 0x3C; // alpha 3, intensity 12
        var ia8 = new byte[32];
        ia8[0] = 0x80; // alpha
        ia8[1] = 0x40; // intensity

        Assert.Equal((204, 204, 204, 51), Pixel(GxTextureDecoder.Decode(GxTextureFormat.IA4, 8, 4, ia4), 8, 0, 0));
        Assert.Equal((64, 64, 64, 128), Pixel(GxTextureDecoder.Decode(GxTextureFormat.IA8, 4, 4, ia8), 4, 0, 0));
    }

    [Fact]
    public void RGB565_expands_to_full_range()
    {
        byte[] data = [0xF8, 0x00, 0x07, 0xE0, 0x00, 0x1F, 0xFF, 0xFF, .. new byte[24]];

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.RGB565, 4, 4, data);

        Assert.Equal((255, 0, 0, 255), Pixel(rgba, 4, 0, 0));
        Assert.Equal((0, 255, 0, 255), Pixel(rgba, 4, 1, 0));
        Assert.Equal((0, 0, 255, 255), Pixel(rgba, 4, 2, 0));
        Assert.Equal((255, 255, 255, 255), Pixel(rgba, 4, 3, 0));
    }

    [Fact]
    public void RGB5A3_switches_between_opaque_and_translucent_encodings()
    {
        // 0xFC00: top bit set, red 31 -> opaque red. 0x4F00: alpha 4, red 15.
        byte[] data = [0xFC, 0x00, 0x4F, 0x00, .. new byte[28]];

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.RGB5A3, 4, 4, data);

        Assert.Equal((255, 0, 0, 255), Pixel(rgba, 4, 0, 0));
        Assert.Equal((255, 0, 0, 146), Pixel(rgba, 4, 1, 0));
    }

    [Fact]
    public void RGBA8_takes_alpha_red_then_green_blue_halves()
    {
        var data = new byte[64];
        data[0] = 0x11; data[1] = 0x22; // pixel 0: A, R
        data[32] = 0x33; data[33] = 0x44; // pixel 0: G, B

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.RGBA8, 4, 4, data);

        Assert.Equal((0x22, 0x33, 0x44, 0x11), Pixel(rgba, 4, 0, 0));
    }

    [Fact]
    public void CMPR_four_colour_blocks_blend_at_three_and_five_eighths()
    {
        // Block 0: c0 = red (0xF800) > c1 = blue (0x001F); row 0 indices 0,1,2,3 = 0b00_01_10_11.
        var data = new byte[32];
        data[0] = 0xF8; data[1] = 0x00; data[2] = 0x00; data[3] = 0x1F; data[4] = 0x1B;

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.CMPR, 8, 8, data);

        Assert.Equal((255, 0, 0, 255), Pixel(rgba, 8, 0, 0));
        Assert.Equal((0, 0, 255, 255), Pixel(rgba, 8, 1, 0));
        Assert.Equal((159, 0, 95, 255), Pixel(rgba, 8, 2, 0));  // 5/8 red + 3/8 blue
        Assert.Equal((95, 0, 159, 255), Pixel(rgba, 8, 3, 0));
    }

    [Fact]
    public void CMPR_three_colour_blocks_have_a_transparent_entry_and_blocks_tile_in_quadrants()
    {
        // Block 3 (bottom-right): c0 = black <= c1 = white; row 0 indices 2, 3 then 0, 0.
        var data = new byte[32];
        data[24] = 0x00; data[25] = 0x00; data[26] = 0xFF; data[27] = 0xFF; data[28] = 0xB0;

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.CMPR, 8, 8, data);

        Assert.Equal((127, 127, 127, 255), Pixel(rgba, 8, 4, 4)); // average
        Assert.Equal((0, 0, 0, 0), Pixel(rgba, 8, 5, 4));         // transparent
    }

    [Fact]
    public void Pixels_outside_the_image_are_dropped()
    {
        var data = new byte[32];
        data[4 * 2] = 0xFF; data[(4 * 2) + 1] = 0xFF; // RGB565 pixel (0, 2) of the 4x4 tile

        var rgba = GxTextureDecoder.Decode(GxTextureFormat.RGB565, 2, 2, data);

        Assert.Equal(2 * 2 * 4, rgba.Length);
    }

    [Fact]
    public void Palette_formats_and_short_data_are_rejected()
    {
        Assert.False(GxTextureDecoder.CanDecode(GxTextureFormat.CI8));
        Assert.Throws<NotSupportedException>(() => GxTextureDecoder.Decode(GxTextureFormat.CI8, 8, 4, new byte[32]));
        Assert.Throws<ArgumentException>(() => GxTextureDecoder.Decode(GxTextureFormat.RGBA8, 4, 4, new byte[63]));
    }
}
