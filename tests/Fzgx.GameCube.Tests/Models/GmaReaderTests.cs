using System.Numerics;
using Fzgx.GameCube.Models;
using static Fzgx.GameCube.Tests.Models.SyntheticGma;

namespace Fzgx.GameCube.Tests.Models;

public class GmaReaderTests
{
    // A 10x10 quad in the XZ plane as one strip: 2 triangles, area 100.
    private static readonly Vector3[] _quad = [new(0, 0, 0), new(10, 0, 0), new(0, 0, 10), new(10, 0, 10)];
    private static readonly Vector3[] _triangle = [new(0, 5, 0), new(4, 5, 0), new(0, 5, 3)];

    // The same triangle wound the other way, as strips in a clockwise group are.
    private static readonly Vector3[] _clockwiseTriangle = [new(0, 5, 0), new(0, 5, 3), new(4, 5, 0)];

    private static byte[] Sample() => Build(
        new Model("road", 0, [7, 3], [new Mesh(0, false, [new Strip(false, _quad), new Strip(true, _clockwiseTriangle)])],
            TranslucentMeshes: [new Mesh(1, true, [new Strip(false, _triangle)], WithNormalsAndUv: false)]),
        null,
        new Model("machine", 0x08, [2], []));

    [Fact]
    public void Lists_models_by_name_and_keeps_empty_entries()
    {
        var models = GmaReader.Read(Sample());

        Assert.Equal(3, models.Count);
        Assert.Equal("road", models[0]!.Name);
        Assert.Null(models[1]);
        Assert.Equal("machine", models[2]!.Name);
    }

    [Fact]
    public void Reads_the_bounding_sphere_and_material_texture_indices()
    {
        var road = GmaReader.Read(Sample())[0]!;

        Assert.Equal(new Vector3(1, 2, 3), road.BoundingCenter);
        Assert.Equal(50, road.BoundingRadius);
        Assert.Equal([new GmaMaterial(7), new GmaMaterial(3)], road.Materials);
    }

    [Fact]
    public void Turns_strips_into_triangles()
    {
        var mesh = GmaReader.Read(Sample())[0]!.Meshes[0];

        Assert.Equal(0, mesh.MaterialIndex);
        Assert.False(mesh.IsTranslucent);
        Assert.Equal(7, mesh.Vertices.Length);  // 4 + 3
        Assert.Equal(9, mesh.Indices.Length);   // 2 + 1 triangles
        Assert.Equal(100 + 6, mesh.SurfaceArea(), 4);
    }

    [Fact]
    public void Strip_triangles_share_one_winding_including_clockwise_groups()
    {
        var mesh = GmaReader.Read(Sample())[0]!.Meshes[0];

        // All three triangles lie in horizontal planes; their normals must agree in sign.
        var signs = Enumerable.Range(0, mesh.Indices.Length / 3).Select(t =>
        {
            var a = mesh.Vertices[mesh.Indices[3 * t]].Position;
            var b = mesh.Vertices[mesh.Indices[(3 * t) + 1]].Position;
            var c = mesh.Vertices[mesh.Indices[(3 * t) + 2]].Position;
            return Math.Sign(Vector3.Cross(b - a, c - a).Y);
        }).Distinct().ToList();
        Assert.Single(signs);
    }

    [Fact]
    public void Area_facing_leaves_out_downward_surfaces_but_counts_meshes_without_normals()
    {
        var file = Build(new Model("m", 0, [0],
        [
            new Mesh(0, false, [new Strip(false, _quad)], NormalY: -1),
            new Mesh(0, false, [new Strip(false, _quad)], NormalY: 0.9f),
            new Mesh(0, false, [new Strip(false, _quad)], WithNormalsAndUv: false),
        ]));
        var meshes = GmaReader.Read(file)[0]!.Meshes;

        Assert.Equal(0, meshes[0].AreaFacing(-0.5), 4);
        Assert.Equal(100, meshes[0].SurfaceArea(), 4);
        Assert.Equal(100, meshes[1].AreaFacing(-0.5), 4);
        Assert.Equal(100, meshes[2].AreaFacing(-0.5), 4);
    }

    [Fact]
    public void Reads_normals_and_texture_coordinates_when_present()
    {
        var mesh = GmaReader.Read(Sample())[0]!.Meshes[0];

        Assert.Equal(new Vector3(0, 1, 0), mesh.Vertices[1].Normal);
        Assert.Equal(new Vector2(1, 0), mesh.Vertices[1].Uv);
        Assert.Equal(0xFFFFFFFFu, mesh.Vertices[1].Color); // no colour attribute: white
    }

    [Fact]
    public void Reads_every_texture_layers_coordinates()
    {
        var gma = Build(new Model("LAYERS", 0, [0, 1, 2], [new Mesh(0, false, [new Strip(false, _quad)], ExtraUvSets: 2)]));

        var vertex = GmaReader.Read(gma)[0]!.Meshes[0].Vertices[1];

        Assert.Equal(new Vector2(1, 0), vertex.Uv);
        Assert.Equal(new Vector2(2, 0), vertex.Uv1);
        Assert.Equal(new Vector2(3, 0), vertex.Uv2);
    }

    [Fact]
    public void Translucent_meshes_are_marked_and_may_lack_attributes()
    {
        var mesh = GmaReader.Read(Sample())[0]!.Meshes[1];

        Assert.True(mesh.IsTranslucent);
        Assert.True(mesh.IsTwoSided);
        Assert.Equal(1, mesh.MaterialIndex);
        Assert.Equal(Vector3.Zero, mesh.Vertices[0].Normal);
    }

    [Fact]
    public void Skinned_models_are_listed_with_a_reason_instead_of_geometry()
    {
        var machine = GmaReader.Read(Sample())[2]!;

        Assert.Empty(machine.Meshes);
        Assert.Contains("not supported", machine.UnsupportedReason, StringComparison.Ordinal);
        Assert.Equal([new GmaMaterial(2)], machine.Materials);
    }

    [Fact]
    public void A_model_without_the_gcmf_magic_is_rejected()
    {
        var file = Sample();
        var modelStart = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(4));
        file[modelStart] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => GmaReader.Read(file));
    }

    [Fact]
    public void An_impossible_model_count_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => GmaReader.Read([0x00, 0x01, 0x00, 0x00, 0, 0, 0, 0]));
    }
}
