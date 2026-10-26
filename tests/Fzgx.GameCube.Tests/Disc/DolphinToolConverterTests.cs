using Fzgx.GameCube.Disc;

namespace Fzgx.GameCube.Tests.Disc;

public class DolphinToolConverterTests
{
    [Theory]
    [InlineData("F-Zero GX (USA).rvz", true)]
    [InlineData("game.RVZ", true)]
    [InlineData("game.wia", true)]
    [InlineData("game.gcz", true)]
    [InlineData("game.iso", false)]
    [InlineData("game.gcm", false)]
    public void Knows_which_images_need_conversion(string path, bool expected)
    {
        Assert.Equal(expected, DolphinToolConverter.NeedsConversion(path));
    }

    [Fact]
    public void Builds_a_convert_command_with_mapped_paths()
    {
        var converter = new DolphinToolConverter("/opt/dolphin/DolphinTool.exe", path => $"W:{path}");

        var info = converter.CreateStartInfo("/games/fzgx.rvz", "/cache/fzgx.iso");

        Assert.Equal("/opt/dolphin/DolphinTool.exe", info.FileName);
        Assert.Equal(
            ["convert", "--format=iso", "--input=W:/games/fzgx.rvz", "--output=W:/cache/fzgx.iso"],
            info.ArgumentList);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void Paths_with_spaces_stay_single_arguments()
    {
        var converter = new DolphinToolConverter("DolphinTool", path => path);

        var info = converter.CreateStartInfo("/my games/F-Zero GX (USA).rvz", "/out dir/x.iso");

        Assert.Contains("--input=/my games/F-Zero GX (USA).rvz", info.ArgumentList);
        Assert.Equal(4, info.ArgumentList.Count);
    }

    [Fact]
    public async Task Missing_input_is_reported_before_running_anything()
    {
        var converter = new DolphinToolConverter("/nonexistent/DolphinTool", path => path);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => converter.ConvertAsync("/nonexistent/game.rvz", "/tmp/out.iso", TestContext.Current.CancellationToken));
    }
}
