// Layout from GxUtils (LibGxFormat/Tpl/Tpl.cs, TplTexture.cs), GPL-3.0.
// https://github.com/bobjrsenior/GxUtils

using Fzgx.GameCube.Io;

namespace Fzgx.GameCube.Textures;

/// <summary>One slot in an Amusement Vision texture file.</summary>
/// <param name="Index">Position in the file; model materials refer to textures by this index.</param>
/// <param name="Format">Pixel format.</param>
/// <param name="Width">Width of mip level 0 in pixels.</param>
/// <param name="Height">Height of mip level 0 in pixels.</param>
/// <param name="LevelCount">Number of mip levels stored.</param>
/// <param name="DataOffset">Offset of mip level 0 in the file.</param>
public readonly record struct AvTplTexture(
    int Index,
    GxTextureFormat Format,
    int Width,
    int Height,
    int LevelCount,
    int DataOffset)
{
    /// <summary>True for a placeholder slot that holds no image.</summary>
    public bool IsEmpty => LevelCount == 0;
}

/// <summary>
/// An Amusement Vision texture file (<c>.tpl</c>, usually inside <c>.tpl.lz</c>).
/// This is not Nintendo's TPL format.
/// </summary>
/// <remarks>
/// Big-endian: a texture count, then 16 bytes per texture - format (u32),
/// data offset (u32), width, height and mip level count (u16 each), and the
/// constant 0x1234. All-zero entries are empty slots that keep later indices
/// stable. Mip levels follow level 0; F-Zero GX sizes small CMPR levels with
/// 4-pixel alignment, which only matters when reading levels beyond 0.
/// </remarks>
public sealed class AvTpl
{
    private const ushort EntryCheck = 0x1234;
    private readonly byte[] _data;

    private AvTpl(byte[] data, IReadOnlyList<AvTplTexture> textures)
    {
        _data = data;
        Textures = textures;
    }

    /// <summary>Every slot, in file order (including empty ones).</summary>
    public IReadOnlyList<AvTplTexture> Textures { get; }

    /// <summary>Parses an uncompressed texture file.</summary>
    /// <exception cref="InvalidDataException">The header is malformed.</exception>
    public static AvTpl Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var reader = new BigEndianReader(data);
        var count = reader.ReadInt32();
        if (count < 0 || 4 + (count * 16L) > data.Length)
        {
            throw new InvalidDataException($"Texture count {count} does not fit in {data.Length} bytes.");
        }

        var textures = new List<AvTplTexture>(count);
        for (var index = 0; index < count; index++)
        {
            var format = reader.ReadInt32();
            var offset = reader.ReadInt32();
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            var levels = reader.ReadUInt16();
            var check = reader.ReadUInt16();

            if (offset == 0 && width == 0 && height == 0 && levels == 0)
            {
                textures.Add(new AvTplTexture(index, (GxTextureFormat)format, 0, 0, 0, 0));
                continue;
            }
            if (check != EntryCheck)
            {
                throw new InvalidDataException($"Texture {index}: header check is 0x{check:X4}, expected 0x1234.");
            }
            if (!Enum.IsDefined((GxTextureFormat)format) || width == 0 || height == 0 || levels == 0)
            {
                throw new InvalidDataException($"Texture {index}: invalid format {format} or size {width}x{height}x{levels}.");
            }
            var texture = new AvTplTexture(index, (GxTextureFormat)format, width, height, levels, offset);
            if (offset < 0 || offset + (long)GxTextureDecoder.LevelSize(texture.Format, width, height) > data.Length)
            {
                throw new InvalidDataException($"Texture {index}: level 0 lies outside the file.");
            }
            textures.Add(texture);
        }
        return new AvTpl(data, textures);
    }

    /// <summary>The encoded (game-format) bytes of mip level 0; empty for empty slots.</summary>
    public ReadOnlyMemory<byte> Level0Data(AvTplTexture texture) => texture.IsEmpty
        ? ReadOnlyMemory<byte>.Empty
        : _data.AsMemory(texture.DataOffset, GxTextureDecoder.LevelSize(texture.Format, texture.Width, texture.Height));

    /// <summary>Decodes mip level 0 of a texture to RGBA; null for empty slots.</summary>
    /// <exception cref="NotSupportedException">The texture uses a palette format.</exception>
    public byte[]? DecodeLevel0(AvTplTexture texture)
    {
        if (texture.IsEmpty)
        {
            return null;
        }
        return GxTextureDecoder.Decode(texture.Format, texture.Width, texture.Height, Level0Data(texture).Span);
    }
}
