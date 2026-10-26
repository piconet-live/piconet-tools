using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Fzgx.GameCube.Tests.Models;

/// <summary>
/// Writes small model files for tests, following the layout GmaReader
/// documents. Tests built on it check the reader's internal consistency;
/// real game files are the independent check.
/// </summary>
internal static class SyntheticGma
{
    public sealed record Strip(bool Clockwise, Vector3[] Positions);

    /// <param name="ExtraUvSets">Texture coordinate sets after the first (0-2): set k is the first times k + 1.</param>
    public sealed record Mesh(int Material, bool TwoSided, Strip[] Strips, bool WithNormalsAndUv = true, float NormalY = 1, int ExtraUvSets = 0);

    public sealed record Model(string Name, uint Attributes, int[] TextureIndices, Mesh[] OpaqueMeshes, Mesh[]? TranslucentMeshes = null);

    /// <summary>Builds a file; a null model writes an empty entry.</summary>
    public static byte[] Build(params Model?[] models)
    {
        var names = new MemoryStream();
        var blocks = new List<byte[]>();
        var entries = new List<(int ModelOffset, int NameOffset)>();
        var modelOffset = 0;
        foreach (var model in models)
        {
            if (model is null)
            {
                entries.Add((-1, 0));
                continue;
            }
            entries.Add((modelOffset, (int)names.Length));
            names.Write(Encoding.ASCII.GetBytes(model.Name));
            names.WriteByte(0);
            var block = BuildModel(model);
            blocks.Add(block);
            modelOffset += block.Length;
        }

        var headerLength = Align(8 + (8 * entries.Count) + (int)names.Length, 0x20);
        var output = new MemoryStream();
        Write32(output, entries.Count);
        Write32(output, headerLength);
        foreach (var (model, name) in entries)
        {
            Write32(output, model);
            Write32(output, name);
        }
        output.Write(names.ToArray());
        Pad(output, headerLength);
        foreach (var block in blocks)
        {
            output.Write(block);
        }
        return output.ToArray();
    }

    private static byte[] BuildModel(Model model)
    {
        var translucent = model.TranslucentMeshes ?? [];
        var header = new MemoryStream();
        Write32(header, 0x47434D46);
        Write32(header, model.Attributes);
        WriteFloat(header, 1); WriteFloat(header, 2); WriteFloat(header, 3); WriteFloat(header, 50);
        Write16(header, model.TextureIndices.Length);
        Write16(header, model.OpaqueMeshes.Length);
        Write16(header, translucent.Length);
        header.WriteByte(0); header.WriteByte(0);
        var headerSize = Align(0x40 + (0x20 * model.TextureIndices.Length), 0x20);
        Write32(header, headerSize);
        Pad(header, 0x40);
        for (var i = 0; i < model.TextureIndices.Length; i++)
        {
            Write32(header, 0);                     // flags
            Write16(header, model.TextureIndices[i]); // texture index at +4
            header.WriteByte(0); header.WriteByte(0);
            Write32(header, 0);
            Write16(header, 0);
            Write16(header, i);                     // material index check at +0x0E
            Pad(header, 0x40 + (0x20 * (i + 1)));
        }
        Pad(header, headerSize);

        foreach (var mesh in model.OpaqueMeshes.Concat(translucent))
        {
            WriteMesh(header, mesh);
        }
        return header.ToArray();
    }

    private static void WriteMesh(MemoryStream output, Mesh mesh)
    {
        var ccw = Group(mesh.Strips.Where(s => !s.Clockwise), mesh.WithNormalsAndUv, mesh.NormalY, mesh.ExtraUvSets);
        var cw = Group(mesh.Strips.Where(s => s.Clockwise), mesh.WithNormalsAndUv, mesh.NormalY, mesh.ExtraUvSets);
        var start = (int)output.Length;
        Write32(output, mesh.TwoSided ? 0x02u : 0u);
        Pad(output, start + 0x12);
        output.WriteByte(1); // used material count
        output.WriteByte((byte)((ccw.Length > 0 ? 1 : 0) | (cw.Length > 0 ? 2 : 0)));
        Write16(output, 0);
        Write16(output, mesh.Material);
        Write16(output, 0xFFFF);
        Write16(output, 0xFFFF);
        var extraUvFlags = mesh.ExtraUvSets switch { 1 => 0x4000u, 2 => 0xC000u, _ => 0u };
        Write32(output, mesh.WithNormalsAndUv ? 0x2600u | extraUvFlags : 0x200u); // position (+ normal, uv0, uv1, uv2)
        Pad(output, start + 0x28);
        Write32(output, ccw.Length);
        Write32(output, cw.Length);
        Pad(output, start + 0x60);
        output.Write(ccw);
        output.Write(cw);
    }

    private static byte[] Group(IEnumerable<Strip> strips, bool normalsAndUv, float normalY, int extraUvSets)
    {
        var list = strips.ToList();
        if (list.Count == 0)
        {
            return [];
        }
        var group = new MemoryStream();
        group.WriteByte(0);
        foreach (var strip in list)
        {
            group.WriteByte(0x98);
            Write16(group, strip.Positions.Length);
            foreach (var p in strip.Positions)
            {
                WriteFloat(group, p.X); WriteFloat(group, p.Y); WriteFloat(group, p.Z);
                if (normalsAndUv)
                {
                    WriteFloat(group, 0); WriteFloat(group, normalY); WriteFloat(group, 0);
                    WriteFloat(group, p.X / 10); WriteFloat(group, p.Z / 10);
                    for (var set = 1; set <= extraUvSets; set++)
                    {
                        WriteFloat(group, (set + 1) * p.X / 10); WriteFloat(group, (set + 1) * p.Z / 10);
                    }
                }
            }
        }
        Pad(group, Align((int)group.Length + 1, 0x20));
        return group.ToArray();
    }

    private static void Write16(Stream s, int value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)value);
        s.Write(b);
    }

    private static void Write32(Stream s, long value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)value);
        s.Write(b);
    }

    private static void WriteFloat(Stream s, float value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(b, value);
        s.Write(b);
    }

    private static void Pad(MemoryStream s, int length)
    {
        while (s.Length < length)
        {
            s.WriteByte(0);
        }
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
