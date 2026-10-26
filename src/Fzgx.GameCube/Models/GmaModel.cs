using System.Collections.Immutable;
using System.Numerics;

namespace Fzgx.GameCube.Models;

/// <summary>A vertex of a model mesh.</summary>
/// <param name="Position">Model-space position.</param>
/// <param name="Normal">Normal, or zero if the mesh has none.</param>
/// <param name="Color">RGBA vertex colour (0-255 per channel), white if the mesh has none.</param>
/// <param name="Uv">First texture coordinate set, or zero if none.</param>
public readonly record struct GmaVertex(Vector3 Position, Vector3 Normal, uint Color, Vector2 Uv)
{
    /// <summary>Texture coordinates of the mesh's second texture layer (zero if it has none).</summary>
    public Vector2 Uv1 { get; init; }

    /// <summary>Texture coordinates of the mesh's third texture layer (zero if it has none).</summary>
    public Vector2 Uv2 { get; init; }
}

/// <summary>A material: which texture of the paired texture file it shows.</summary>
/// <param name="TextureIndex">Index into the model file's paired <c>.tpl</c>.</param>
public readonly record struct GmaMaterial(int TextureIndex);

/// <summary>
/// Triangles sharing one material. Triangles are listed in the order the
/// strips defined them; the winding is consistent within a mesh but the
/// preview draws both sides.
/// </summary>
/// <param name="MaterialIndex">Primary material, or -1 for none.</param>
/// <param name="IsTranslucent">True for the model's second (translucent) mesh layer.</param>
/// <param name="IsTwoSided">True if the game draws both faces.</param>
/// <param name="Vertices">Vertex list.</param>
/// <param name="Indices">Three vertex indices per triangle.</param>
public sealed record GmaMesh(
    int MaterialIndex,
    bool IsTranslucent,
    bool IsTwoSided,
    ImmutableArray<GmaVertex> Vertices,
    ImmutableArray<int> Indices)
{
    /// <summary>Summed area of the mesh's triangles, in model units squared.</summary>
    public double SurfaceArea() => AreaFacing(double.NegativeInfinity);

    /// <summary>
    /// Summed area of the triangles whose average vertex normal has a Y
    /// component of at least <paramref name="minNormalY"/> (e.g. -0.5 leaves
    /// out surfaces facing downwards). Triangles without normals always count.
    /// </summary>
    public double AreaFacing(double minNormalY)
    {
        var area = 0.0;
        for (var i = 0; i < Indices.Length; i += 3)
        {
            var a = Vertices[Indices[i]];
            var b = Vertices[Indices[i + 1]];
            var c = Vertices[Indices[i + 2]];
            var normal = a.Normal + b.Normal + c.Normal;
            if (normal != Vector3.Zero && normal.Y / 3 < minNormalY)
            {
                continue;
            }
            area += Vector3.Cross(b.Position - a.Position, c.Position - a.Position).Length() / 2;
        }
        return area;
    }
}

/// <summary>One model of a model file.</summary>
/// <param name="Name">The model's name; scene objects refer to models by it.</param>
/// <param name="BoundingCenter">Centre of the model's bounding sphere.</param>
/// <param name="BoundingRadius">Radius of the model's bounding sphere.</param>
/// <param name="Materials">Materials, indexed by the meshes.</param>
/// <param name="Meshes">Geometry; empty when <see cref="UnsupportedReason"/> is set.</param>
/// <param name="UnsupportedReason">Why the geometry was not read (e.g. skinned models), or null.</param>
public sealed record GmaModel(
    string Name,
    Vector3 BoundingCenter,
    float BoundingRadius,
    ImmutableArray<GmaMaterial> Materials,
    ImmutableArray<GmaMesh> Meshes,
    string? UnsupportedReason);
