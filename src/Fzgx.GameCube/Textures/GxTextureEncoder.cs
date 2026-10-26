using System.Buffers.Binary;

namespace Fzgx.GameCube.Textures;

/// <summary>
/// Encodes RGBA images into GameCube texture formats, the inverse of
/// <see cref="GxTextureDecoder"/>: the same tile order, and for CMPR the
/// hardware's palette (blends at 5/8 and 3/8).
/// </summary>
public static class GxTextureEncoder
{
    /// <summary>True if <paramref name="format"/> can be encoded.</summary>
    public static bool CanEncode(GxTextureFormat format) => format is GxTextureFormat.I4 or GxTextureFormat.CMPR;

    /// <summary>
    /// Encodes one mip level of <paramref name="width"/> x <paramref name="height"/>
    /// pixels (4 bytes each, RGBA); the result is padded to whole tiles, as
    /// <see cref="GxTextureDecoder.LevelSize"/> gives.
    /// </summary>
    /// <exception cref="NotSupportedException">The format is not one <see cref="CanEncode"/> accepts.</exception>
    public static byte[] Encode(GxTextureFormat format, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException($"{width}x{height} needs {width * height * 4} bytes of RGBA, got {rgba.Length}.", nameof(rgba));
        }
        var output = new byte[GxTextureDecoder.LevelSize(format, width, height)];
        var image = new Source(rgba, width, height);
        switch (format)
        {
            case GxTextureFormat.I4:
                EncodeI4(image, output);
                break;
            case GxTextureFormat.CMPR:
                EncodeCmpr(image, output);
                break;
            default:
                throw new NotSupportedException($"Texture format {format} cannot be encoded.");
        }
        return output;
    }

    /// <summary>The image at half size, each pixel the rounded mean of a 2x2 block (for the next mip level).</summary>
    public static byte[] Halve(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var (w, h) = (Math.Max(1, width / 2), Math.Max(1, height / 2));
        var output = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0;
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var sx = Math.Min(width - 1, (2 * x) + dx);
                            var sy = Math.Min(height - 1, (2 * y) + dy);
                            sum += rgba[(((sy * width) + sx) * 4) + channel];
                        }
                    }
                    output[(((y * w) + x) * 4) + channel] = (byte)((sum + 2) / 4);
                }
            }
        }
        return output;
    }

    /// <summary>I4: 8x8 tiles, rows of 4 bytes, two pixels a byte (the first in the high nibble); the red channel's intensity.</summary>
    private static void EncodeI4(Source image, byte[] output)
    {
        var at = 0;
        for (var tileY = 0; tileY < image.Height; tileY += 8)
        {
            for (var tileX = 0; tileX < image.Width; tileX += 8)
            {
                for (var y = 0; y < 8; y++)
                {
                    for (var x = 0; x < 8; x += 2)
                    {
                        output[at++] = (byte)((Nibble(image, tileX + x, tileY + y) << 4) | Nibble(image, tileX + x + 1, tileY + y));
                    }
                }
            }
        }
    }

    private static int Nibble(Source image, int x, int y) =>
        x < image.Width && y < image.Height ? (image.Get(x, y).R + 8) * 15 / 255 : 0;

    /// <summary>
    /// CMPR: 8x8 tiles of four 4x4 blocks (top-left, top-right, bottom-left,
    /// bottom-right). Each block takes its two most different colours as end
    /// points, in four-colour order (first above second), and each pixel the
    /// nearest of the four palette entries.
    /// </summary>
    private static void EncodeCmpr(Source image, byte[] output)
    {
        var at = 0;
        Span<(int R, int G, int B)> block = stackalloc (int, int, int)[16];
        Span<(int R, int G, int B)> palette = stackalloc (int, int, int)[4];
        for (var tileY = 0; tileY < image.Height; tileY += 8)
        {
            for (var tileX = 0; tileX < image.Width; tileX += 8)
            {
                for (var sub = 0; sub < 4; sub++)
                {
                    var (bx, by) = (tileX + ((sub % 2) * 4), tileY + ((sub / 2) * 4));
                    for (var i = 0; i < 16; i++)
                    {
                        var p = image.Get(Math.Min(image.Width - 1, bx + (i % 4)), Math.Min(image.Height - 1, by + (i / 4)));
                        block[i] = (p.R, p.G, p.B);
                    }
                    var (a, b, farthest) = (0, 0, -1);
                    for (var i = 0; i < 16; i++)
                    {
                        for (var j = i + 1; j < 16; j++)
                        {
                            var d = Distance(block[i], block[j]);
                            if (d > farthest)
                            {
                                (a, b, farthest) = (i, j, d);
                            }
                        }
                    }
                    var c0 = To565(block[a]);
                    var c1 = To565(block[b]);
                    if (c0 < c1)
                    {
                        (c0, c1) = (c1, c0);
                    }
                    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), c0);
                    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + 2), c1);
                    var entries = 1;
                    palette[0] = From565(c0);
                    if (c0 != c1)
                    {
                        var p1 = From565(c1);
                        palette[1] = p1;
                        palette[2] = Blend(palette[0], p1, 5, 3);
                        palette[3] = Blend(palette[0], p1, 3, 5);
                        entries = 4;
                    }
                    for (var row = 0; row < 4; row++)
                    {
                        var indices = 0;
                        for (var column = 0; column < 4; column++)
                        {
                            var best = 0;
                            for (var e = 1; e < entries; e++)
                            {
                                if (Distance(block[(row * 4) + column], palette[e]) < Distance(block[(row * 4) + column], palette[best]))
                                {
                                    best = e;
                                }
                            }
                            indices |= best << (6 - (2 * column));
                        }
                        output[at + 4 + row] = (byte)indices;
                    }
                    at += 8;
                }
            }
        }
    }

    private static int Distance((int R, int G, int B) p, (int R, int G, int B) q) =>
        ((p.R - q.R) * (p.R - q.R)) + ((p.G - q.G) * (p.G - q.G)) + ((p.B - q.B) * (p.B - q.B));

    private static ushort To565((int R, int G, int B) c) => (ushort)(((c.R >> 3) << 11) | ((c.G >> 2) << 5) | (c.B >> 3));

    private static (int R, int G, int B) From565(ushort value)
    {
        int r = (value >> 11) & 0x1F, g = (value >> 5) & 0x3F, b = value & 0x1F;
        return ((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
    }

    private static (int R, int G, int B) Blend((int R, int G, int B) a, (int R, int G, int B) b, int weightA, int weightB) =>
        (((a.R * weightA) + (b.R * weightB)) / 8, ((a.G * weightA) + (b.G * weightB)) / 8, ((a.B * weightA) + (b.B * weightB)) / 8);

    private readonly ref struct Source(ReadOnlySpan<byte> rgba, int width, int height)
    {
        private readonly ReadOnlySpan<byte> _rgba = rgba;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public (byte R, byte G, byte B, byte A) Get(int x, int y)
        {
            var at = ((y * Width) + x) * 4;
            return (_rgba[at], _rgba[at + 1], _rgba[at + 2], _rgba[at + 3]);
        }
    }
}
