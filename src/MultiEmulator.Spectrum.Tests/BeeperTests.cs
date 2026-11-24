using MultiEmulator.Spectrum.Audio;

namespace MultiEmulator.Spectrum.Tests;

public class BeeperTests
{
    [Fact]
    public void TogglingEar_ProducesAudibleEnergy()
    {
        var b = new BeeperBuffer();
        b.SetFrameTStates(69888);

        // Silencio
        b.Advance(69888, false, false);
        Span<float> silence = stackalloc float[882];
        b.Read(silence);
        double silentE = 0;
        foreach (var s in silence) silentE += s * s;

        b.Reset();
        for (int i = 0; i < 400; i++)
            b.Advance(175, (i & 1) == 0, false); // ~10 kHz-ish square a 192k path
        Span<float> tone = stackalloc float[882];
        b.Read(tone);
        double toneE = 0;
        foreach (var s in tone) toneE += s * s;

        Assert.True(toneE > silentE + 0.001, $"toneE={toneE} silentE={silentE}");
    }

    [Fact]
    public void MicBit_ChangesLevel_WithEarHeld()
    {
        var b = new BeeperBuffer();
        b.SetFrameTStates(69888);

        // Transición MIC con EAR fijo → debe haber flanco (energía)
        b.Advance(1000, true, false);
        b.Advance(1000, true, true);
        Span<float> buf = stackalloc float[200];
        b.Read(buf);
        double e = 0;
        foreach (var s in buf) e += Math.Abs(s);
        Assert.True(e > 0.0001, $"expected MIC edge energy, e={e}");
    }
}
