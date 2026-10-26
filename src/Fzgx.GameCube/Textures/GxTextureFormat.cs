namespace Fzgx.GameCube.Textures;

/// <summary>GameCube GX texture formats, by their hardware ID.</summary>
public enum GxTextureFormat
{
    /// <summary>4-bit intensity, 8x8 tiles.</summary>
    I4 = 0,

    /// <summary>8-bit intensity, 8x4 tiles.</summary>
    I8 = 1,

    /// <summary>4-bit alpha and intensity, 8x4 tiles.</summary>
    IA4 = 2,

    /// <summary>8-bit alpha and intensity, 4x4 tiles.</summary>
    IA8 = 3,

    /// <summary>16-bit colour without alpha, 4x4 tiles.</summary>
    RGB565 = 4,

    /// <summary>16-bit colour with optional 3-bit alpha, 4x4 tiles.</summary>
    RGB5A3 = 5,

    /// <summary>32-bit colour, 4x4 tiles stored as AR then GB halves.</summary>
    RGBA8 = 6,

    /// <summary>4-bit palette index (not supported for decoding).</summary>
    CI4 = 8,

    /// <summary>8-bit palette index (not supported for decoding).</summary>
    CI8 = 9,

    /// <summary>14-bit palette index (not supported for decoding).</summary>
    CI14X2 = 10,

    /// <summary>DXT1-style compression, 8x8 tiles of four 4x4 blocks.</summary>
    CMPR = 14,
}
