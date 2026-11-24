using MultiEmulator.Spectrum;

namespace MultiEmulator.Spectrum.Tests;

public class MachineSmokeTests
{
    [Fact]
    public void RunFrame_DoesNotThrow_WithBlankRom()
    {
        var m = new SpectrumMachine();
        var rom = new byte[0x4000];
        rom[0] = 0x00; // NOP forever at 0000 after DI would be better but blank NOP loop via JP
        rom[0] = 0xC3;
        rom[1] = 0x00;
        rom[2] = 0x00;
        m.LoadSystemRoms(rom, null);
        m.Reset();
        for (int i = 0; i < 3; i++)
            m.RunFrame();
        Assert.True(m.Ula.Framebuffer.Length == 256 * 192);
    }
}
