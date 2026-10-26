using System.Buffers.Binary;
using Fzgx.GameCube.Textures;

namespace Fzgx.GameCube.Tests.Textures;

/// <summary>Writes small Amusement Vision texture files for tests.</summary>
internal static class SyntheticTpl
{
    /// <summary>Builds a texture file: entries of (format, width, height, levels, level-0 data), null for an empty slot.</summary>
    public static byte[] Build(params (GxTextureFormat Format, int Width, int Height, byte[] Data)?[] textures)
    {
        var headerSize = 4 + (16 * textures.Length);
        var dataStart = (headerSize + 31) / 32 * 32;
        var file = new List<byte>(new byte[dataStart]);
        BinaryPrimitives.WriteInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(file), textures.Length);
        for (var i = 0; i < textures.Length; i++)
        {
            if (textures[i] is not var (format, width, height, data))
            {
                continue; // all-zero entry
            }
            var entry = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(file).Slice(4 + (16 * i), 16);
            BinaryPrimitives.WriteInt32BigEndian(entry, (int)format);
            BinaryPrimitives.WriteInt32BigEndian(entry[4..], file.Count);
            BinaryPrimitives.WriteUInt16BigEndian(entry[8..], (ushort)width);
            BinaryPrimitives.WriteUInt16BigEndian(entry[10..], (ushort)height);
            BinaryPrimitives.WriteUInt16BigEndian(entry[12..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(entry[14..], 0x1234);
            file.AddRange(data);
        }
        return [.. file];
    }
}
