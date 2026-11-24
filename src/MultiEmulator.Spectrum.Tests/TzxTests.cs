using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Tape;

namespace MultiEmulator.Spectrum.Tests;

public class TzxTests
{
    private static string ArkanoidTzxZip =>
        @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\Arkanoid.tzx.zip";

    [Fact]
    public void LoadTzx_Arkanoid_ParsesDataBlocksIncluding14()
    {
        if (!File.Exists(ArkanoidTzxZip)) return;

        var (name, data) = ArchiveMedia.ExtractPreferredMedia(ArkanoidTzxZip);
        Assert.EndsWith(".tzx", name, StringComparison.OrdinalIgnoreCase);

        var tape = TapeImage.LoadTzx(data);
        Assert.True(tape.Blocks.Count >= 4, $"blocks={tape.Blocks.Count}");
        Assert.Contains(tape.Blocks, b => b.TzxId == 0x10);
        Assert.Contains(tape.Blocks, b => b.TzxId == 0x14);
        Assert.True(tape.Blocks.Count(b => b.TzxId == 0x14) >= 1);
    }

    [Fact]
    public void LoadTzx_Arkanoid_FallbackLoad_AdvancesTape()
    {
        if (!TestRomPaths.TryRead48(out var rom) || !File.Exists(ArkanoidTzxZip)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        MediaLoader.OpenPath(m, ArkanoidTzxZip);
        Assert.NotNull(m.Tape.Tape);
        Assert.True(m.Tape.Tape!.Blocks.Count > 0);
        // Sin USR parseable → fallback LOAD "" tras editor (turbo vía EAR).
        Assert.False(m.Tape.UsedInstantLoad);

        for (int i = 0; i < 600; i++)
            m.RunFrame();

        Assert.True(m.Tape.Tape.Position > 0, $"tape did not advance; pos={m.Tape.Tape.Position} blocks={m.Tape.Tape.Blocks.Count}");
    }
}
