using MultiEmulator.Spectrum.Tape;
using MultiEmulator.Spectrum.Ula;

namespace MultiEmulator.Spectrum.Video;

/// <summary>
/// Efecto VHS suave en shuttle (FF/REW): se nota el modo sin tapar el juego.
/// Intensidad ligera según SpeedFactor; FF cian suave / REW ámbar suave.
/// </summary>
public static class VhsTransportEffect
{
    private static readonly Random Rng = new();
    private static uint[]? _scratch;
    private static int _frame;
    private static int _roll;

    public static void Apply(Span<uint> pixels, TapeTransport transport)
    {
        if (!transport.IsShuttle) return;
        int w = SpectrumUla.ScreenWidth;
        int h = SpectrumUla.ScreenHeight;
        if (pixels.Length < w * h) return;

        if (_scratch == null || _scratch.Length < pixels.Length)
            _scratch = new uint[pixels.Length];
        pixels.CopyTo(_scratch);

        _frame++;
        int speed = Math.Clamp(transport.SpeedFactor, 2, 8);
        bool rew = transport.Mode == TapeTransportMode.Rewind;
        // ×2≈0.25 … ×8≈1.0, luego se escala con factores bajos.
        float intensity = speed / 8f;

        // Rolling mínimo (1 px cada 2–4 frames según velocidad).
        if ((_frame & (speed >= 8 ? 1 : 3)) == 0)
            _roll = (_roll + (rew ? -1 : 1) + h) % h;

        // Jitter horizontal 0–1 px (solo a ×4+).
        int jitterAmp = speed >= 4 ? 1 : 0;

        for (int y = 0; y < h; y++)
        {
            int srcY = (y + _roll) % h;
            int jitter = jitterAmp == 0 ? 0 : (Rng.Next(3) - 1);
            if (rew) jitter = -jitter;

            int srcRow = srcY * w;
            // Tracking: líneas finas y poco frecuentes.
            int trackPeriod = 56 - speed * 2; // ~52…40
            bool trackingBand = ((y + _frame * (rew ? -1 : 1)) % trackPeriod) == 0;
            // Nieve rara.
            bool snowPixelRow = Rng.NextDouble() < 0.004 + intensity * 0.008;
            // Scanlines muy ligeras (solo cada 2ª línea, −8 %).
            bool softScan = (y & 1) != 0;

            for (int x = 0; x < w; x++)
            {
                int sx = x + jitter;
                if ((uint)sx >= (uint)w) sx = (sx % w + w) % w;
                uint c = _scratch[srcRow + sx];

                // Skew mínimo solo en la línea de tracking (1 px).
                if (trackingBand)
                {
                    int skx = Math.Clamp(sx + (rew ? -1 : 1), 0, w - 1);
                    c = _scratch[srcRow + skx];
                }

                int r = (int)((c >> 16) & 0xFF);
                int g = (int)((c >> 8) & 0xFF);
                int b = (int)(c & 0xFF);

                // Desaturación suave (máx ~18 % a ×8).
                int luma = (r * 30 + g * 59 + b * 11) / 100;
                float sat = 1f - intensity * 0.18f;
                r = (int)(luma + (r - luma) * sat);
                g = (int)(luma + (g - luma) * sat);
                b = (int)(luma + (b - luma) * sat);

                // Tint sutil: FF cian / REW ámbar.
                if (rew)
                {
                    r = Math.Min(255, r + (int)(10 * intensity));
                    g = Math.Min(255, g + (int)(4 * intensity));
                    b = Math.Max(0, b - (int)(6 * intensity));
                }
                else
                {
                    r = Math.Max(0, r - (int)(4 * intensity));
                    g = Math.Min(255, g + (int)(4 * intensity));
                    b = Math.Min(255, b + (int)(10 * intensity));
                }

                if (softScan)
                {
                    r = r * 92 / 100;
                    g = g * 92 / 100;
                    b = b * 92 / 100;
                }

                if (trackingBand)
                {
                    // Lift suave, no blanco puro.
                    r = Math.Min(255, r + 18);
                    g = Math.Min(255, g + 18);
                    b = Math.Min(255, b + 22);
                }

                // Nieve puntual muy escasa (mezcla, no reemplazo total).
                if (snowPixelRow && Rng.NextDouble() < 0.04 * intensity)
                {
                    int n = 160 + Rng.Next(96);
                    r = (r * 2 + n) / 3;
                    g = (g * 2 + n) / 3;
                    b = (b * 2 + n) / 3;
                }

                pixels[y * w + x] = 0xFF000000u
                    | ((uint)Clamp(r) << 16)
                    | ((uint)Clamp(g) << 8)
                    | (uint)Clamp(b);
            }
        }
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
