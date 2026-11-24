using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Input;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

/// <summary>
/// Regresión: Vaus debe permanecer en el display file durante la partida
/// (floating bus muestreado al final del IN; TAP FlashLoad).
/// </summary>
public class ArkanoidPaddleTests
{
    private static readonly string TapZip =
        @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\Arkanoid.tap.zip";

    [Fact]
    public void ArkanoidTap_InGame_PaddlePixelsPersist()
    {
        if (!TestRomPaths.TryRead48(out var rom))
            return;
        if (!File.Exists(TapZip))
            return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        m.Joystick.Type = JoystickType.Kempston;
        for (int i = 0; i < 120; i++)
            m.RunFrame();

        MediaLoader.OpenPath(m, TapZip, RomMediaFormat.Tap);
        LoadPolicies.ApplyFlashLoadUsrEntry(m);
        LoadPolicies.ApplyFloatingBusVsyncLatch(m);
        for (int i = 0; i < 500; i++)
            m.RunFrame();

        bool entered = false;
        for (int round = 0; round < 80 && !entered; round++)
        {
            TapKey(m, 3, 0, 3);
            TapKey(m, 7, 0, 3);
            TapKey(m, 4, 0, 3);
            TapKey(m, 6, 0, 3);
            m.Joystick.Fire = true;
            for (int i = 0; i < 5; i++) m.RunFrame();
            m.Joystick.Fire = false;
            for (int i = 0; i < 10; i++) m.RunFrame();
            if (CountBottomInk(m) > 40)
                entered = true;
        }

        Assert.True(entered, "No se alcanzó partida con pala visible (bottom ink)");

        int ok = 0, bad = 0, maxBottom = 0;
        for (int f = 0; f < 600; f++)
        {
            if ((f % 60) == 0)
            {
                m.Joystick.Left = (f % 120) == 0;
                m.Joystick.Right = (f % 120) != 0;
            }

            m.RunFrame();
            int bottom = CountBottomInk(m);
            if (bottom > maxBottom) maxBottom = bottom;
            if (bottom > 40) ok++;
            else bad++;
        }

        Assert.True(maxBottom >= 80, $"pala demasiado débil maxBottom={maxBottom}");
        Assert.True(
            ok >= 500,
            $"pala ausente demasiados frames ok={ok} bad={bad} max={maxBottom} PC={m.Registers.PC:X4}");
    }

    [Fact]
    public void Registry_AutoFromTzx_OpensFlashableTap()
    {
        if (!TestRomPaths.TryRead48(out var rom))
            return;

        string tzx = Path.Combine(Path.GetDirectoryName(TapZip)!, "Arkanoid.tzx.zip");
        if (!File.Exists(TapZip) || !File.Exists(tzx))
            return;

        ProjectCatalogPaths.SetCatalogDirForTests(null);
        LoadSolutionsStore.Reload(EmulatorPlatform.ZxSpectrum);

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        for (int i = 0; i < 120; i++) m.RunFrame();

        var result = LoadOrchestrator.OpenWithRetry(
            m, tzx, RomMediaFormat.Auto, extraCandidates: [TapZip], healthFrames: 100);
        Assert.True(result.Success, result.StatusMessage);
        Assert.True(
            result.Path.EndsWith("Arkanoid.tap.zip", StringComparison.OrdinalIgnoreCase),
            $"expected TAP via registry, got {result.Path} strat={result.Strategy}");
        Assert.True(m.Tape.UsedInstantLoad);
    }

    private static void TapKey(SpectrumMachine m, int row, int bit, int frames)
    {
        m.Keyboard.SetKey(row, bit, true);
        for (int i = 0; i < frames; i++) m.RunFrame();
        m.Keyboard.SetKey(row, bit, false);
    }

    private static int CountBottomInk(SpectrumMachine m)
    {
        int n = 0;
        var scr = m.Memory.ScreenBank;
        for (int y = 184; y <= 191; y++)
        {
            int pa = ((y & 0xC0) << 5) | ((y & 0x07) << 8) | ((y & 0x38) << 2);
            for (int x = 0; x < 32; x++)
                n += System.Numerics.BitOperations.PopCount(scr[pa + x]);
        }

        return n;
    }
}
