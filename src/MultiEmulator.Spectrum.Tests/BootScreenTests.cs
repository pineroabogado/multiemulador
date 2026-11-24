using MultiEmulator.Spectrum;

namespace MultiEmulator.Spectrum.Tests;

public class BootScreenTests
{
    [Fact]
    public void AfterFrames_ScreenMemory_IsNotAllZero_WithRealRom()
    {
        if (!TestRomPaths.TryRead48(out var rom))
            return; // skip if ROM not in project/output
        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        for (int i = 0; i < 100; i++)
            m.RunFrame();

        var screen = m.Memory.ScreenBank;
        int nonZero = 0;
        for (int i = 0; i < 0x1B00; i++)
            if (screen[i] != 0) nonZero++;

        Assert.True(nonZero > 100, $"Expected copyright/screen draws, nonZero={nonZero}, PC={m.Registers.PC:X4}");
    }
}
