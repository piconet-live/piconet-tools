using System.Buffers.Binary;

namespace Fzgx.GameCube.Textures;

/// <summary>
/// Decodes GameCube GX textures to 8-bit RGBA, rows top to bottom.
/// </summary>
/// <remarks>
/// GX stores pixels in tiles (blocks) that run left to right, top to bottom;
/// each tile is 32 bytes (64 for RGBA8), pixels row by row inside it. Images
/// are padded to whole tiles; padding is skipped. Intensity formats copy the
/// intensity into alpha as the hardware does. Palette formats are not
/// supported (F-Zero GX course textures do not use them).
/// </remarks>
public static class GxTextureDecoder
{
    /// <summary>True if <paramref name="format"/> can be decoded.</summary>
    public static bool CanDecode(GxTextureFormat format) =>
        format is GxTextureFormat.I4 or GxTextureFormat.I8 or GxTextureFormat.IA4 or GxTextureFormat.IA8
            or GxTextureFormat.RGB565 or GxTextureFormat.RGB5A3 or GxTextureFormat.RGBA8 or GxTextureFormat.CMPR;

    /// <summary>Tile width and height of a format, in pixels.</summary>
    public static (int Width, int Height) TileSize(GxTextureFormat format) => format switch
    {
        GxTextureFormat.I4 or GxTextureFormat.CI4 or GxTextureFormat.CMPR => (8, 8),
        GxTextureFormat.I8 or GxTextureFormat.IA4 or GxTextureFormat.CI8 => (8, 4),
        _ => (4, 4),
    };

    /// <summary>Bytes of one mip level at the given size, padded to whole tiles.</summary>
    public static int LevelSize(GxTextureFormat format, int width, int height)
    {
        var (tileWidth, tileHeight) = TileSize(format);
        var tileBytes = format == GxTextureFormat.RGBA8 ? 64 : 32;
        return DivideUp(width, tileWidth) * DivideUp(height, tileHeight) * tileBytes;
    }

    /// <summary>Decodes one mip level; returns width * height * 4 bytes of RGBA.</summary>
    /// <exception cref="NotSupportedException">The format is a palette format.</exception>
    /// <exception cref="ArgumentException">The data is shorter than the level.</exception>
    public static byte[] Decode(GxTextureFormat format, int width, int height, ReadOnlySpan<byte> data)
    {
        if (!CanDecode(format))
        {
            throw new NotSupportedException($"Texture format {format} cannot be decoded.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var size = LevelSize(format, width, height);
        if (data.Length < size)
        {
            throw new ArgumentException($"{format} {width}x{height} needs {size} bytes, got {data.Length}.", nameof(data));
        }

        var rgba = new byte[width * height * 4];
        var (tileWidth, tileHeight) = TileSize(format);
        var tileBytes = format == GxTextureFormat.RGBA8 ? 64 : 32;
        var tile = 0;
        for (var tileY = 0; tileY < height; tileY += tileHeight)
        {
            for (var tileX = 0; tileX < width; tileX += tileWidth)
            {
                var source = data.Slice(tile++ * tileBytes, tileBytes);
                var image = new Target(rgba, width, height, tileX, tileY);
                switch (format)
                {
                    case GxTextureFormat.CMPR:
                        DecodeCmprTile(source, image);
                        break;
                    case GxTextureFormat.RGBA8:
                        DecodeRgba8Tile(source, image);
                        break;
                    default:
                        DecodeSimpleTile(format, source, image, tileWidth, tileHeight);
                        break;
                }
            }
        }
        return rgba;
    }

    private static void DecodeSimpleTile(GxTextureFormat format, ReadOnlySpan<byte> source, Target image, int tileWidth, int tileHeight)
    {
        for (var i = 0; i < tileWidth * tileHeight; i++)
        {
            var (x, y) = (i % tileWidth, i / tileWidth);
            switch (format)
            {
                case GxTextureFormat.I4:
                    var nibble = (i % 2 == 0 ? source[i / 2] >> 4 : source[i / 2]) & 0xF;
                    var i4 = (byte)(nibble * 17);
                    image.Set(x, y, i4, i4, i4, i4);
                    break;
                case GxTextureFormat.I8:
                    image.Set(x, y, source[i], source[i], source[i], source[i]);
                    break;
                case GxTextureFormat.IA4:
                    var intensity = (byte)((source[i] & 0xF) * 17);
                    image.Set(x, y, intensity, intensity, intensity, (byte)((source[i] >> 4) * 17));
                    break;
                case GxTextureFormat.IA8:
                    image.Set(x, y, source[(2 * i) + 1], source[(2 * i) + 1], source[(2 * i) + 1], source[2 * i]);
                    break;
                case GxTextureFormat.RGB565:
                    var (r, g, b) = Rgb565(BinaryPrimitives.ReadUInt16BigEndian(source[(2 * i)..]));
                    image.Set(x, y, r, g, b, 255);
                    break;
                case GxTextureFormat.RGB5A3:
                    SetRgb5A3(image, x, y, BinaryPrimitives.ReadUInt16BigEndian(source[(2 * i)..]));
                    break;
                default:
                    throw new NotSupportedException(format.ToString());
            }
        }
    }

    private static void SetRgb5A3(Target image, int x, int y, ushort value)
    {
        if ((value & 0x8000) != 0)
        {
            // 1RRRRRGGGGGBBBBB: opaque
            image.Set(x, y, Expand5((value >> 10) & 0x1F), Expand5((value >> 5) & 0x1F), Expand5(value & 0x1F), 255);
        }
        else
        {
            // 0AAARRRRGGGGBBBB: 3-bit alpha
            var alpha = (value >> 12) & 0x7;
            image.Set(x, y, (byte)(((value >> 8) & 0xF) * 17), (byte)(((value >> 4) & 0xF) * 17), (byte)((value & 0xF) * 17),
                (byte)((alpha << 5) | (alpha << 2) | (alpha >> 1)));
        }
    }

    /// <summary>RGBA8 tiles hold 16 AR pairs, then 16 GB pairs.</summary>
    private static void DecodeRgba8Tile(ReadOnlySpan<byte> source, Target image)
    {
        for (var i = 0; i < 16; i++)
        {
            image.Set(i % 4, i / 4, source[(2 * i) + 1], source[32 + (2 * i)], source[32 + (2 * i) + 1], source[2 * i]);
        }
    }

    /// <summary>A CMPR tile is four DXT1-style 4x4 blocks: top-left, top-right, bottom-left, bottom-right.</summary>
    private static void DecodeCmprTile(ReadOnlySpan<byte> source, Target image)
    {
        Span<byte> palette = stackalloc byte[16];
        for (var block = 0; block < 4; block++)
        {
            var bytes = source.Slice(block * 8, 8);
            var offsetX = (block % 2) * 4;
            var offsetY = (block / 2) * 4;
            var c0 = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            var c1 = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]);
            var (r0, g0, b0) = Rgb565(c0);
            var (r1, g1, b1) = Rgb565(c1);
            Store(palette, 0, r0, g0, b0, 255);
            Store(palette, 1, r1, g1, b1, 255);
            if (c0 > c1)
            {
                // Hardware blends at 3/8 and 5/8 rather than DXT1's thirds.
                Store(palette, 2, Blend(r0, r1, 5, 3), Blend(g0, g1, 5, 3), Blend(b0, b1, 5, 3), 255);
                Store(palette, 3, Blend(r0, r1, 3, 5), Blend(g0, g1, 3, 5), Blend(b0, b1, 3, 5), 255);
            }
            else
            {
                Store(palette, 2, Blend(r0, r1, 4, 4), Blend(g0, g1, 4, 4), Blend(b0, b1, 4, 4), 255);
                Store(palette, 3, 0, 0, 0, 0); // transparent
            }
            for (var row = 0; row < 4; row++)
            {
                var indices = bytes[4 + row];
                for (var column = 0; column < 4; column++)
                {
                    var entry = (indices >> (6 - (2 * column))) & 3;
                    image.Set(offsetX + column, offsetY + row,
                        palette[4 * entry], palette[(4 * entry) + 1], palette[(4 * entry) + 2], palette[(4 * entry) + 3]);
                }
            }
        }
    }

    private static (byte R, byte G, byte B) Rgb565(ushort value) =>
        (Expand5((value >> 11) & 0x1F), (byte)((((value >> 5) & 0x3F) << 2) | (((value >> 5) & 0x3F) >> 4)), Expand5(value & 0x1F));

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static byte Blend(byte a, byte b, int weightA, int weightB) => (byte)(((a * weightA) + (b * weightB)) / 8);

    private static void Store(Span<byte> palette, int entry, byte r, byte g, byte b, byte a)
    {
        palette[4 * entry] = r;
        palette[(4 * entry) + 1] = g;
        palette[(4 * entry) + 2] = b;
        palette[(4 * entry) + 3] = a;
    }

    private static int DivideUp(int value, int divisor) => (value + divisor - 1) / divisor;

    /// <summary>The output image seen from one tile's corner; writes outside the image are dropped.</summary>
    private readonly ref struct Target(byte[] rgba, int width, int height, int originX, int originY)
    {
        private readonly byte[] _rgba = rgba;

        public void Set(int x, int y, byte r, byte g, byte b, byte a)
        {
            var px = originX + x;
            var py = originY + y;
            if (px >= width || py >= height)
            {
                return;
            }
            var at = ((py * width) + px) * 4;
            _rgba[at] = r;
            _rgba[at + 1] = g;
            _rgba[at + 2] = b;
            _rgba[at + 3] = a;
        }
    }
}
