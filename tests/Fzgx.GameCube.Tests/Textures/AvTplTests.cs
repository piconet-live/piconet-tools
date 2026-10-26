using System.Buffers.Binary;
using Fzgx.GameCube.Textures;

namespace Fzgx.GameCube.Tests.Textures;

public class AvTplTests
{
    private static readonly byte[] _redRgb565 = [.. Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 0xF8, 0x00 })];

    [Fact]
    public void Lists_textures_and_keeps_empty_slots_so_indices_stay_stable()
    {
        var tpl = AvTpl.Parse(SyntheticTpl.Build((GxTextureFormat.RGB565, 4, 4, _redRgb565), null, (GxTextureFormat.I8, 8, 4, new byte[32])));

        Assert.Equal(3, tpl.Textures.Count);
        Assert.Equal(new AvTplTexture(0, GxTextureFormat.RGB565, 4, 4, 1, 64), tpl.Textures[0]);
        Assert.True(tpl.Textures[1].IsEmpty);
        Assert.Equal(2, tpl.Textures[2].Index);
        Assert.Equal(GxTextureFormat.I8, tpl.Textures[2].Format);
    }

    [Fact]
    public void Decodes_level_zero_and_returns_null_for_empty_slots()
    {
        var tpl = AvTpl.Parse(SyntheticTpl.Build((GxTextureFormat.RGB565, 4, 4, _redRgb565), null));

        var rgba = tpl.DecodeLevel0(tpl.Textures[0]);

        Assert.NotNull(rgba);
        Assert.Equal([255, 0, 0, 255], rgba[..4]);
        Assert.Null(tpl.DecodeLevel0(tpl.Textures[1]));
    }

    [Fact]
    public void A_wrong_header_check_is_rejected()
    {
        var file = SyntheticTpl.Build((GxTextureFormat.RGB565, 4, 4, _redRgb565));
        file[4 + 15] = 0x35;

        var error = Assert.Throws<InvalidDataException>(() => AvTpl.Parse(file));
        Assert.Contains("0x1234", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_outside_the_file_is_rejected()
    {
        var file = SyntheticTpl.Build((GxTextureFormat.RGB565, 4, 4, _redRgb565));

        Assert.Throws<InvalidDataException>(() => AvTpl.Parse(file[..^1]));
    }

    [Fact]
    public void An_impossible_texture_count_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => AvTpl.Parse([0x00, 0x00, 0x10, 0x00, 0, 0, 0, 0]));
    }
}
