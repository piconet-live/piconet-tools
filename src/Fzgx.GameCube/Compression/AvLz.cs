// The decoder is ported from GameCube.AmusementVision (LzDecoder.cs, Lz.cs),
// Copyright Raphael Tetreault and the GxUtils authors, GPL-3.0.
// https://github.com/RaphaelTetreault/GameCube.AmusementVision
// The encoder is original to this project.

using System.Buffers.Binary;

namespace Fzgx.GameCube.Compression;

/// <summary>
/// Amusement Vision's LZ compression (<c>.lz</c> files in F-Zero GX and
/// Super Monkey Ball): an 8-byte little-endian header followed by LZSS data.
/// </summary>
/// <remarks>
/// Header: compressed size, then uncompressed size. F-Zero GX stores the
/// compressed size alone; Super Monkey Ball and F-Zero AX include the 8
/// header bytes. LZSS: 4096-byte ring buffer filled with zeros, writing from
/// position 4078; each flag byte, least significant bit first, marks eight
/// items as a literal byte (1) or a two-byte match (0) of 3 to 18 bytes whose
/// 12-bit ring position is the first byte plus the high nibble of the second.
/// </remarks>
public static class AvLz
{
    private const int RingSize = 4096;
    private const int MaxMatch = 18;
    private const int MinMatch = 3;
    private const int RingStart = RingSize - MaxMatch;
    private const int HeaderSize = 8;

    /// <summary>Decompresses a whole <c>.lz</c> file.</summary>
    /// <exception cref="InvalidDataException">The header or data is inconsistent.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize)
        {
            throw new InvalidDataException("Too short for an LZ header.");
        }
        var sizeField = BinaryPrimitives.ReadInt32LittleEndian(file);
        var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(file[4..]);
        var compressedSize = sizeField == file.Length ? sizeField - HeaderSize // header counted (SMB, AX)
            : sizeField == file.Length - HeaderSize ? sizeField                  // header not counted (GX)
            : throw new InvalidDataException($"LZ size field {sizeField} does not match the file length {file.Length}.");
        if (uncompressedSize < 0)
        {
            throw new InvalidDataException($"LZ uncompressed size {uncompressedSize} is negative.");
        }

        var output = Decode(file.Slice(HeaderSize, compressedSize), uncompressedSize);
        return output.Length == uncompressedSize
            ? output
            : throw new InvalidDataException($"LZ data produced {output.Length} bytes, header says {uncompressedSize}.");
    }

    /// <summary>
    /// Compresses <paramref name="data"/> into a complete F-Zero GX <c>.lz</c>
    /// file. Always decompresses back to the input; it is not byte-identical
    /// to the game's own encoder.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var body = Encode(data);
        var file = new byte[HeaderSize + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(file, body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), data.Length);
        body.CopyTo(file, HeaderSize);
        return file;
    }

    private static byte[] Decode(ReadOnlySpan<byte> input, int expectedSize)
    {
        var output = new List<byte>(expectedSize);
        var ring = new byte[RingSize];
        var ringPosition = RingStart;
        var position = 0;
        var flags = 0;

        while (position < input.Length)
        {
            // Eight flag bits per flag byte; the 0x100 marker counts them.
            flags >>= 1;
            if ((flags & 0x100) == 0)
            {
                flags = input[position++] | 0xFF00;
            }
            if (position >= input.Length)
            {
                break; // a trailing flag byte with nothing after it
            }

            if ((flags & 1) != 0)
            {
                var literal = input[position++];
                output.Add(literal);
                ring[ringPosition++ % RingSize] = literal;
            }
            else
            {
                if (position + 1 >= input.Length)
                {
                    throw new InvalidDataException($"LZ match truncated at offset {position}.");
                }
                var index = ((input[position + 1] & 0xF0) << 4) | input[position];
                var count = (input[position + 1] & 0x0F) + MinMatch;
                position += 2;
                for (var i = 0; i < count; i++)
                {
                    var value = ring[(index + i) % RingSize];
                    output.Add(value);
                    ring[ringPosition++ % RingSize] = value;
                }
            }
        }
        return [.. output];
    }

    /// <summary>
    /// Greedy LZSS: at each step the longest match in the ring buffer that
    /// the decoder will reproduce, else a literal. The whole window is
    /// searched, skipping positions whose byte differs from the next one.
    /// </summary>
    private static byte[] Encode(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length + (data.Length / 8) + 1);
        // Mirror of the decoder's ring buffer: the decoder starts with zeros.
        var ring = new byte[RingSize];
        var ringPosition = RingStart;
        var position = 0;

        while (position < data.Length)
        {
            var flagIndex = output.Count;
            output.Add(0);
            for (var bit = 0; bit < 8 && position < data.Length; bit++)
            {
                var (index, length) = LongestMatch(ring, ringPosition, data[position..]);
                if (length >= MinMatch)
                {
                    output.Add((byte)index);
                    output.Add((byte)(((index >> 4) & 0xF0) | (length - MinMatch)));
                }
                else
                {
                    output[flagIndex] |= (byte)(1 << bit);
                    output.Add(data[position]);
                    length = 1;
                }
                for (var i = 0; i < length; i++)
                {
                    ring[ringPosition++ % RingSize] = data[position++];
                }
            }
        }
        return [.. output];
    }

    /// <summary>
    /// The longest prefix of <paramref name="ahead"/> that copying from a ring
    /// position reproduces, simulating the decoder so overlapping copies (a
    /// match that reads bytes it is writing) are handled exactly.
    /// </summary>
    private static (int Index, int Length) LongestMatch(byte[] ring, int ringPosition, ReadOnlySpan<byte> ahead)
    {
        var limit = Math.Min(MaxMatch, ahead.Length);
        var bestIndex = 0;
        var bestLength = 0;
        Span<byte> written = stackalloc byte[MaxMatch];
        var first = ahead.Length > 0 ? ahead[0] : (byte)0;
        var here = ringPosition % RingSize;
        for (var index = 0; index < RingSize && bestLength < limit; index++)
        {
            // A copy's first byte always comes from the ring (nothing is written yet): skip positions that cannot start one.
            if (ring[index] != first)
            {
                continue;
            }
            // Nor, when three bytes are left, one shorter than a match (it would be a literal anyway): the
            // second and third bytes come from the ring, or from what the copy has just written.
            if (limit >= MinMatch && (CopiedByte(ring, here, ahead, index + 1) != ahead[1] || CopiedByte(ring, here, ahead, index + 2) != ahead[2]))
            {
                continue;
            }
            var length = 0;
            while (length < limit)
            {
                var source = (index + length) % RingSize;
                // Bytes this copy has already written to the ring override older contents.
                var distance = (source - (ringPosition % RingSize) + RingSize) % RingSize;
                var value = distance < length ? written[distance] : ring[source];
                if (value != ahead[length])
                {
                    break;
                }
                written[length++] = value;
            }
            if (length > bestLength)
            {
                bestIndex = index;
                bestLength = length;
            }
        }
        return (bestIndex, bestLength);
    }

    /// <summary>
    /// The byte a copy writing at <paramref name="here"/> reads at ring
    /// position <paramref name="source"/>, within its first bytes: one it
    /// has written itself (the bytes ahead, before it) or the ring's.
    /// </summary>
    private static byte CopiedByte(byte[] ring, int here, ReadOnlySpan<byte> ahead, int source)
    {
        var distance = ((source % RingSize) - here + RingSize) % RingSize;
        return distance < MinMatch - 1 ? ahead[distance] : ring[source % RingSize];
    }
}
