using MultiEmulator.Spectrum.Audio;

namespace MultiEmulator.Spectrum.Tests;

public class BeeperTests
{
    [Fact]
    public void TogglingEar_ProducesAudibleEnergy()
    {
        var b = new BeeperBuffer();
        b.SetFrameTStates(69888);

        // Asentar filtros Fuse (primer sample del HPF es 0 a propósito).
        b.Advance(69888 * 2, false, false);
        Span<float> discard = stackalloc float[882 * 2];
        b.Read(discard);

        Span<float> silence = stackalloc float[882];
        b.Advance(69888, false, false);
        b.Read(silence);
        double silentE = 0;
        foreach (var s in silence) silentE += s * s;

        for (int i = 0; i < 800; i++)
            b.Advance(175, (i & 1) == 0, false);
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

        b.Advance(69888, true, false);
        Span<float> discard = stackalloc float[882];
        b.Read(discard);

        b.Advance(2000, true, false);
        b.Advance(2000, true, true);
        Span<float> buf = stackalloc float[200];
        b.Read(buf);
        double e = 0;
        foreach (var s in buf) e += Math.Abs(s);
        Assert.True(e > 0.0001, $"expected MIC edge energy, e={e}");
    }

    [Fact]
    public void UltrasonicPwmBurst_IsAudible_AfterSpeakerFilter()
    {
        // Arkanoid-like: ~18 kHz square (≈96 T/edge) en ráfaga corta.
        var b = new BeeperBuffer();
        b.SetFrameTStates(69888);
        b.Advance(69888, false, false);
        Span<float> discard = stackalloc float[882];
        b.Read(discard);

        // 3 frames silencio
        for (int f = 0; f < 3; f++)
        {
            b.Advance(69888, false, false);
            b.Read(discard);
        }

        double silentPeak = 0;
        b.Advance(69888, false, false);
        b.Read(discard);
        foreach (var s in discard)
            silentPeak = Math.Max(silentPeak, Math.Abs(s));

        // Ráfaga ~2 frames a ~18 kHz
        for (int i = 0; i < 1400; i++)
            b.Advance(48, (i & 1) == 0, false);
        Span<float> burst = stackalloc float[882 * 2];
        b.Read(burst);
        double burstPeak = 0;
        double burstE = 0;
        foreach (var s in burst)
        {
            burstE += s * s;
            burstPeak = Math.Max(burstPeak, Math.Abs(s));
        }

        Assert.True(
            burstPeak > silentPeak + 0.02 && burstE > 0.5,
            $"PWM burst should click via speaker HPF: peak={burstPeak} silentPeak={silentPeak} e={burstE}");
    }
}
