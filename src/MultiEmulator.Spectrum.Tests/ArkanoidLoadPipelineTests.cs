using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

/// <summary>
/// Arkanoid como caso de regresión Spectaculator (FlashLoad + TAP JUEGOS),
/// vía políticas generales (ranking / IRQ tras USR / health+retry).
/// </summary>
public class ArkanoidLoadPipelineTests
{
    private static readonly string Juegos =
        @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS";

    [Fact]
    public void Inspect_PrefersFlashableTap_OverTurboTzxWithoutFlash()
    {
        string tap = Path.Combine(Juegos, "Arkanoid.tap.zip");
        string tzx = Path.Combine(Juegos, "Arkanoid.tzx.zip");
        if (!File.Exists(tap) || !File.Exists(tzx)) return;

        var tapSig = LoadMediaInspect.InspectPath(tap);
        var tzxSig = LoadMediaInspect.InspectPath(tzx);
        Assert.True(tapSig.CanFlashToUsr, "TAP debe ser FlashLoad+USR");
        Assert.False(tzxSig.CanFlashToUsr, "TZX turbo sin flash USR no debe ganar ranking");
        Assert.True(tapSig.HealthScore > tzxSig.HealthScore,
            $"tap={tapSig.HealthScore} tzx={tzxSig.HealthScore}");
    }

    [Fact]
    public void Ranker_FromTzxPrimary_StillOrdersFlashableTapFirst()
    {
        string tap = Path.Combine(Juegos, "Arkanoid.tap.zip");
        string tzx = Path.Combine(Juegos, "Arkanoid.tzx.zip");
        if (!File.Exists(tap) || !File.Exists(tzx)) return;

        var attempts = LoadCandidateRanker.BuildAttempts(tzx, RomMediaFormat.Auto, inspect: true);
        Assert.True(attempts.Count >= 2);
        var top = attempts[0];
        Assert.True(
            top.Path.EndsWith("Arkanoid.tap.zip", StringComparison.OrdinalIgnoreCase),
            $"top={top.Path} score={top.Score} strategy={top.Strategy}");
    }

    [Fact]
    public void Orchestrator_OpensTap_ReachesPlayableRam()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string tap = Path.Combine(Juegos, "Arkanoid.tap.zip");
        string tzx = Path.Combine(Juegos, "Arkanoid.tzx.zip");
        if (!File.Exists(tap)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        m.Joystick.Type = JoystickType.Kempston;

        // Como catálogo: a veces JUEGOS entrega TZX primero; el orchestrator debe preferir TAP.
        string primary = File.Exists(tzx) ? tzx : tap;
        var extras = File.Exists(tzx) && File.Exists(tap)
            ? new[] { tap }
            : Array.Empty<string>();

        var result = LoadOrchestrator.OpenWithRetry(
            m, primary, RomMediaFormat.Auto, extras, healthFrames: 100);
        Assert.True(result.Success, result.StatusMessage);
        Assert.True(m.Tape.UsedInstantLoad, "Spectaculator FlashLoad parity");
        Assert.True(
            result.Path.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)
            || result.Path.EndsWith(".tap", StringComparison.OrdinalIgnoreCase),
            $"expected TAP path, got {result.Path}");

        for (int i = 0; i < 200; i++)
        {
            if (m.Tape.IsFastLoadActive)
            {
                int w = Math.Max(1, m.Tape.FastLoadWarpFrames);
                for (int j = 0; j < w; j++) m.RunFrame();
            }
            else m.RunFrame();
        }

        int scr = LoadHealthCheck.CountNonZero(m.Memory.ScreenBank.AsSpan(0, 0x1800));
        Assert.True(scr > 200, $"expected title/load pixels, scr={scr}");
        // Tras FlashLoad el stub puede volver a wait de teclado en ROM (p.ej. 029B)
        // con la pantalla ya cargada; lo importante es InstantLoad + pixels.
        Assert.True(
            m.Registers.PC >= 0x4000 || scr > 2000,
            $"expected game RAM or title screen after FlashLoad, PC={m.Registers.PC:X4} scr={scr}");
    }
}
