namespace MultiEmulator.Spectrum.Media;

/// <summary>
/// Decodifica pantallas Spectrum .SCR (6912 bytes: 6144 bitmap + 768 atributos) a ARGB.
/// </summary>
public static class ScrDecoder
{
    public const int Width = 256;
    public const int Height = 192;
    public const int ScrSize = 6912;

    // Misma paleta que SpectrumUla (AARRGGBB).
    private static readonly uint[] Palette =
    {
        0xFF000000, 0xFF0000D7, 0xFFD70000, 0xFFD700D7,
        0xFF00D700, 0xFF00D7D7, 0xFFD7D700, 0xFFD7D7D7,
        0xFF000000, 0xFF0000FF, 0xFFFF0000, 0xFFFF00FF,
        0xFF00FF00, 0xFF00FFFF, 0xFFFFFF00, 0xFFFFFFFF
    };

    public static bool LooksLikeScr(ReadOnlySpan<byte> data) =>
        data.Length >= ScrSize && data.Length <= ScrSize + 128;

    /// <summary>Devuelve buffer AARRGGBB de 256×192.</summary>
    public static uint[] ToArgb(ReadOnlySpan<byte> scr)
    {
        if (scr.Length < ScrSize)
            throw new ArgumentException("SCR incompleto (se esperan 6912 bytes).", nameof(scr));

        var pixels = new uint[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            // Offset Spectrum: ((y&0xC0)<<5) | ((y&7)<<8) | ((y&0x38)<<2)
            int bitmapOffset = ((y & 0xC0) << 5) | ((y & 0x07) << 8) | ((y & 0x38) << 2);
            int attrOffset = 6144 + (y / 8) * 32;

            for (int xByte = 0; xByte < 32; xByte++)
            {
                byte bits = scr[bitmapOffset + xByte];
                byte attr = scr[attrOffset + xByte];
                int ink = attr & 0x07;
                int paper = (attr >> 3) & 0x07;
                bool bright = (attr & 0x40) != 0;
                // Flash: fase «apagada» (ink/paper) para thumbs estáticos.

                int inkIdx = ink + (bright ? 8 : 0);
                int paperIdx = paper + (bright ? 8 : 0);
                uint inkC = Palette[inkIdx];
                uint paperC = Palette[paperIdx];

                int rowBase = y * Width + xByte * 8;
                for (int b = 0; b < 8; b++)
                {
                    bool on = (bits & (0x80 >> b)) != 0;
                    pixels[rowBase + b] = on ? inkC : paperC;
                }
            }
        }

        return pixels;
    }
}
