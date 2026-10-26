// Layout from GxUtils (LibGxFormat/Gma), GPL-3.0, https://github.com/bobjrsenior/GxUtils,
// cross-checked against the Manifold project's GCMF field order (reference only).

using System.Collections.Immutable;
using System.Numerics;
using Fzgx.GameCube.Io;

namespace Fzgx.GameCube.Models;

/// <summary>
/// Reads Amusement Vision model files (<c>.gma</c>, usually inside <c>.gma.lz</c>).
/// </summary>
/// <remarks>
/// <para>A GMA file lists models by name. Each model is a GCMF block: a 0x40-byte
/// header (flags, bounding sphere, material and mesh counts, header size),
/// 0x20-byte materials whose texture index is at offset 4, then the meshes.</para>
/// <para>Static models store each mesh as a 0x60-byte header followed by
/// triangle-strip groups (counter-clockwise, then clockwise; two more when the
/// mesh flags are 0x0F). A strip is a GX draw command byte (0x98 float,
/// 0x99 16-bit), a vertex count, and the vertices; a zero byte ends a group.</para>
/// <para>Skinned and "effective" models use an indexed vertex pool and are
/// reported as unsupported rather than read.</para>
/// </remarks>
public static class GmaReader
{
    private const uint GcmfMagic = 0x47434D46; // "GCMF"
    private const uint Attribute16Bit = 0x01;
    private const uint AttributeSkin = 0x08;
    private const uint AttributeEffective = 0x10;
    private const uint RenderTwoSided = 0x02;

    [Flags]
    private enum VertexFlags : uint
    {
        MatrixIndex = 0x0000_0001,
        Position = 0x0000_0200,
        Normal = 0x0000_0400,
        Color = 0x0000_0800,
        Uv0 = 0x0000_2000,
        Uv1 = 0x0000_4000,
        Uv2 = 0x0000_8000,
        NormalBinormalTangent = 0x0200_0000,
    }

    private const VertexFlags KnownVertexFlags = VertexFlags.MatrixIndex | VertexFlags.Position | VertexFlags.Normal
        | VertexFlags.Color | VertexFlags.Uv0 | VertexFlags.Uv1 | VertexFlags.Uv2 | VertexFlags.NormalBinormalTangent;

    /// <summary>
    /// Reads every entry of an uncompressed model file. Empty entries (no
    /// model and no name) are null, so indices match the file.
    /// </summary>
    /// <exception cref="InvalidDataException">The file structure is malformed.</exception>
    public static IReadOnlyList<GmaModel?> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var reader = new BigEndianReader(data);
        var count = reader.ReadInt32();
        var modelBase = reader.ReadInt32();
        if (count < 0 || 8 + (count * 8L) > data.Length || modelBase < 0 || modelBase > data.Length)
        {
            throw new InvalidDataException($"Model count {count} or model base 0x{modelBase:X} does not fit the file.");
        }
        var namesBase = 8 + (count * 8);

        var models = new List<GmaModel?>(count);
        for (var i = 0; i < count; i++)
        {
            reader.Position = 8 + (i * 8);
            var modelOffset = reader.ReadInt32();
            var nameOffset = reader.ReadInt32();
            if (modelOffset == -1 && nameOffset == 0)
            {
                models.Add(null);
                continue;
            }
            reader.Position = namesBase + nameOffset;
            var name = reader.ReadCString();
            models.Add(ReadModel(data, modelBase + modelOffset, name));
        }
        return models;
    }

    private static GmaModel ReadModel(byte[] data, int start, string name)
    {
        var reader = new BigEndianReader(data) { Position = start };
        if (reader.ReadUInt32() != GcmfMagic)
        {
            throw new InvalidDataException($"Model '{name}' at 0x{start:X} does not start with GCMF.");
        }
        var attributes = reader.ReadUInt32();
        var center = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var radius = reader.ReadSingle();
        var materialCount = reader.ReadUInt16();
        var opaqueMeshes = reader.ReadUInt16();
        var translucentMeshes = reader.ReadUInt16();
        reader.Position = start + 0x20;
        var headerSize = reader.ReadInt32();

        reader.Position = start + 0x40;
        var materials = ImmutableArray.CreateBuilder<GmaMaterial>(materialCount);
        for (var m = 0; m < materialCount; m++)
        {
            var materialStart = reader.Position;
            reader.Position = materialStart + 4;
            materials.Add(new GmaMaterial(reader.ReadUInt16()));
            reader.Position = materialStart + 0x20;
        }

        if ((attributes & (AttributeSkin | AttributeEffective)) != 0)
        {
            return new GmaModel(name, center, radius, materials.MoveToImmutable(), [],
                "skinned or effective model (indexed vertices) is not supported yet");
        }

        reader.Position = start + headerSize;
        var is16Bit = (attributes & Attribute16Bit) != 0;
        var meshes = ImmutableArray.CreateBuilder<GmaMesh>(opaqueMeshes + translucentMeshes);
        for (var m = 0; m < opaqueMeshes + translucentMeshes; m++)
        {
            var mesh = ReadMesh(reader, is16Bit, isTranslucent: m >= opaqueMeshes, out var unsupported);
            if (mesh is null)
            {
                return new GmaModel(name, center, radius, materials.MoveToImmutable(), [], unsupported);
            }
            meshes.Add(mesh);
        }
        return new GmaModel(name, center, radius, materials.MoveToImmutable(), meshes.MoveToImmutable(), null);
    }

    private static GmaMesh? ReadMesh(BigEndianReader reader, bool is16Bit, bool isTranslucent, out string? unsupported)
    {
        var start = reader.Position;
        var renderFlags = reader.ReadUInt32();
        reader.Position = start + 0x13;
        var groups = reader.ReadByte();
        reader.Position = start + 0x16;
        var primaryMaterial = reader.ReadUInt16();
        reader.Position = start + 0x1C;
        var vertexFlags = (VertexFlags)reader.ReadUInt32();
        reader.Position = start + 0x28;
        var sizes = new List<int> { reader.ReadInt32(), reader.ReadInt32() };
        reader.Position = start + 0x60;

        if ((vertexFlags & ~KnownVertexFlags) != 0 || (vertexFlags & VertexFlags.Position) == 0)
        {
            unsupported = $"mesh with unknown vertex flags 0x{(uint)vertexFlags:X8}";
            return null;
        }

        var vertices = ImmutableArray.CreateBuilder<GmaVertex>();
        var indices = ImmutableArray.CreateBuilder<int>();
        for (var group = 0; group < 4; group++)
        {
            if (group == 2)
            {
                if ((groups & 0x0F) != 0x0F)
                {
                    break;
                }
                // Extra header before the third and fourth groups.
                var extraStart = reader.Position;
                reader.Position = extraStart + 8;
                sizes.Add(reader.ReadInt32());
                sizes.Add(reader.ReadInt32());
                reader.Position = extraStart + 0x20;
            }
            if ((groups & (1 << group)) == 0)
            {
                continue;
            }
            var groupEnd = reader.Position + sizes[group];
            // Groups 1 and 3 hold clockwise strips; flip them to match 0 and 2.
            ReadStripGroup(reader, groupEnd, vertexFlags, is16Bit, flip: group % 2 == 1, vertices, indices);
            reader.Position = groupEnd;
        }

        unsupported = null;
        return new GmaMesh(
            primaryMaterial == ushort.MaxValue ? -1 : primaryMaterial,
            isTranslucent,
            (renderFlags & RenderTwoSided) != 0,
            vertices.ToImmutable(),
            indices.ToImmutable());
    }

    private static void ReadStripGroup(
        BigEndianReader reader,
        int groupEnd,
        VertexFlags flags,
        bool is16Bit,
        bool flip,
        ImmutableArray<GmaVertex>.Builder vertices,
        ImmutableArray<int>.Builder indices)
    {
        reader.ReadByte(); // leading zero
        while (reader.Position < groupEnd)
        {
            var command = reader.ReadByte();
            if (command == 0)
            {
                break; // padding after the last strip
            }
            if (command is not (0x98 or 0x99))
            {
                throw new InvalidDataException($"Unknown strip command 0x{command:X2} at 0x{reader.Position - 1:X}.");
            }
            var count = reader.ReadUInt16();
            var first = vertices.Count;
            for (var v = 0; v < count; v++)
            {
                vertices.Add(ReadVertex(reader, flags, is16Bit));
            }
            for (var k = 0; k + 2 < count; k++)
            {
                // Strip triangles alternate winding; normalise them.
                var (a, b, c) = k % 2 == 0 ? (k, k + 1, k + 2) : (k + 1, k, k + 2);
                if (flip)
                {
                    (b, c) = (c, b);
                }
                indices.AddRange(first + a, first + b, first + c);
            }
        }
    }

    private static GmaVertex ReadVertex(BigEndianReader reader, VertexFlags flags, bool is16Bit)
    {
        if (flags.HasFlag(VertexFlags.MatrixIndex))
        {
            reader.ReadByte();
        }
        var position = new Vector3(Number(reader, is16Bit), Number(reader, is16Bit), Number(reader, is16Bit));
        var normal = flags.HasFlag(VertexFlags.Normal)
            ? new Vector3(Number(reader, is16Bit), Number(reader, is16Bit), Number(reader, is16Bit))
            : Vector3.Zero;
        if (flags.HasFlag(VertexFlags.NormalBinormalTangent))
        {
            reader.ReadBytes(9 * 4); // normal, binormal, tangent as floats; unused for previews
        }
        var color = flags.HasFlag(VertexFlags.Color) ? reader.ReadUInt32() : 0xFFFFFFFF;
        var uv = flags.HasFlag(VertexFlags.Uv0) ? new Vector2(Number(reader, is16Bit), Number(reader, is16Bit)) : Vector2.Zero;
        var uv1 = flags.HasFlag(VertexFlags.Uv1) ? new Vector2(Number(reader, is16Bit), Number(reader, is16Bit)) : Vector2.Zero;
        var uv2 = flags.HasFlag(VertexFlags.Uv2) ? new Vector2(Number(reader, is16Bit), Number(reader, is16Bit)) : Vector2.Zero;
        return new GmaVertex(position, normal, color, uv) { Uv1 = uv1, Uv2 = uv2 };
    }

    /// <summary>A float, or in 16-bit models a signed fixed-point value with 13 fraction bits.</summary>
    private static float Number(BigEndianReader reader, bool is16Bit) =>
        is16Bit ? reader.ReadInt16() / 8191f : reader.ReadSingle();
}
