using System.Text;
using Fzgx.GameCube.Disc;

namespace Fzgx.GameCube.Tests.Disc;

public class GcmDiscTests
{
    private static readonly byte[] _st01 = Encoding.ASCII.GetBytes("stage one textures");
    private static readonly byte[] _coli = Encoding.ASCII.GetBytes("course one");
    private static readonly byte[] _bg = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();

    private static GcmDisc Open(byte[] image) => new(new MemoryStream(image));

    private static byte[] Sample() => SyntheticDisc.Build(
        "GFZE01",
        ("stage/st01.tpl.lz", _st01),
        ("stage/COLI_COURSE01.lz", _coli),
        ("bg/bg_mut.tpl", _bg),
        ("opening.bnr", [1, 2, 3]));

    [Fact]
    public void Reads_the_game_id_and_title()
    {
        using var disc = Open(Sample());

        Assert.Equal("GFZE01", disc.GameId);
        Assert.Equal("SYNTHETIC TEST DISC", disc.Title);
    }

    [Fact]
    public void Lists_files_with_full_paths_in_order()
    {
        using var disc = Open(Sample());

        Assert.Equal(
            ["bg/bg_mut.tpl", "opening.bnr", "stage/COLI_COURSE01.lz", "stage/st01.tpl.lz"],
            disc.Files.Select(file => file.Path));
    }

    [Fact]
    public void Reads_file_contents_in_nested_directories()
    {
        using var disc = Open(Sample());

        Assert.Equal(_st01, disc.ReadFile("stage/st01.tpl.lz"));
        Assert.Equal(_coli, disc.ReadFile("stage/COLI_COURSE01.lz"));
        Assert.Equal(_bg, disc.ReadFile("bg/bg_mut.tpl"));
        Assert.Equal([1, 2, 3], disc.ReadFile("opening.bnr"));
    }

    [Fact]
    public void Paths_are_case_insensitive()
    {
        using var disc = Open(Sample());

        Assert.True(disc.Contains("STAGE/coli_course01.LZ"));
        Assert.Equal(_coli, disc.ReadFile("Stage/COLI_COURSE01.lz"));
    }

    [Fact]
    public void Missing_files_are_reported_by_name()
    {
        using var disc = Open(Sample());

        var error = Assert.Throws<FileNotFoundException>(() => disc.ReadFile("stage/st99.tpl.lz"));
        Assert.Contains("st99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_without_the_disc_magic_are_rejected()
    {
        var image = Sample();
        image[0x1C] = 0;

        var error = Assert.Throws<InvalidDataException>(() => Open(image));
        Assert.Contains("Not a GameCube disc image", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_table_outside_the_image_is_rejected()
    {
        var image = Sample();
        image[0x424] = 0x7F; // FST offset far beyond the end

        Assert.Throws<InvalidDataException>(() => Open(image));
    }

    [Fact]
    public void Disposing_closes_the_stream_unless_asked_to_leave_it_open()
    {
        var owned = new MemoryStream(Sample());
        var shared = new MemoryStream(Sample());

        new GcmDisc(owned).Dispose();
        new GcmDisc(shared, leaveOpen: true).Dispose();

        Assert.False(owned.CanRead);
        Assert.True(shared.CanRead);
    }
}
