using MultiEmulator.Spectrum.Media;

namespace MultiEmulator.Spectrum.Tests;

public class ScrDecoderTests
{
    [Fact]
    public void LooksLikeScr_AcceptsExact6912()
    {
        Assert.True(ScrDecoder.LooksLikeScr(new byte[6912]));
        Assert.False(ScrDecoder.LooksLikeScr(new byte[100]));
    }

    [Fact]
    public void ToArgb_DoesNotThrow_OnFullScr()
    {
        var scr = new byte[ScrDecoder.ScrSize];
        // Un byte de tinta blanca brillante en (0,0) y attr blanco.
        scr[0] = 0xFF;
        scr[6144] = 0x47; // ink 7 + bright

        var pixels = ScrDecoder.ToArgb(scr);
        Assert.Equal(ScrDecoder.Width * ScrDecoder.Height, pixels.Length);
        Assert.Equal(0xFFFFFFFF, pixels[0]); // ink white bright
    }

    [Fact]
    public void ToArgb_LastScanline_StaysInsideBitmap()
    {
        var scr = new byte[ScrDecoder.ScrSize];
        // Forzar lectura de la última fila (y=191) sin IndexOutOfRange.
        var pixels = ScrDecoder.ToArgb(scr);
        Assert.Equal(0xFF000000u, pixels[^1]); // paper black
    }

    [Fact]
    public void ToArgb_GalenCachedScr_DecodesWithoutThrow()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MULTIEMULADOR", "Spectrum", "media", "0006345", "load.scr");
        if (!File.Exists(path))
            return;

        byte[] data = File.ReadAllBytes(path);
        Assert.True(ScrDecoder.LooksLikeScr(data));
        var pixels = ScrDecoder.ToArgb(data.AsSpan(0, ScrDecoder.ScrSize));
        int nonBlack = pixels.Count(c => c != 0xFF000000);
        Assert.True(nonBlack > 100, $"Galen.scr debería tener contenido visible; nonBlack={nonBlack}");
    }
}
