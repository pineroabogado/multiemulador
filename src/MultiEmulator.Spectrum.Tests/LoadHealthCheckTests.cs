using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class LoadHealthCheckTests
{
    [Fact]
    public void LooksLikeMidLoadPc_DetectsLdBytes()
    {
        Assert.True(LoadMediaInspect.LooksLikeMidLoadPc(0x0556, iff1: false));
        Assert.True(LoadMediaInspect.LooksLikeMidLoadPc(0x05E2, iff1: false));
        Assert.False(LoadMediaInspect.LooksLikeMidLoadPc(0x8000, iff1: true));
        Assert.False(LoadMediaInspect.LooksLikeMidLoadPc(0xCD00, iff1: true));
    }

    [Fact]
    public void TryPeekZ80_ReadsPcAndIff()
    {
        // Cabecera Z80 v1 mínima: PC en offset 6, IFF1 en 27.
        var data = new byte[30];
        data[6] = 0x56;
        data[7] = 0x05; // PC = 0x0556
        data[27] = 0;   // IFF1 off
        Assert.True(LoadMediaInspect.TryPeekZ80(data, out ushort pc, out bool iff1));
        Assert.Equal(0x0556, pc);
        Assert.False(iff1);
        Assert.True(LoadMediaInspect.LooksLikeMidLoadPc(pc, iff1));
    }

    [Fact]
    public void CandidateRanker_PrefersTapeSibling_OverMidLoadHint()
    {
        string dir = Path.Combine(Path.GetTempPath(), "me-rank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string tap = Path.Combine(dir, "Game.tap");
            string z80 = Path.Combine(dir, "Game.z80");
            // TAP mínimo (cabecera inválida pero fichero presente).
            File.WriteAllBytes(tap, [0x03, 0x00, 0xFF, 0x00, 0x00]);
            // Z80 mid-load (PC=LD-BYTES, IFF off).
            var snap = new byte[30];
            snap[6] = 0x56;
            snap[7] = 0x05;
            snap[27] = 0;
            File.WriteAllBytes(z80, snap);

            var attempts = LoadCandidateRanker.BuildAttempts(z80, RomMediaFormat.Auto, inspect: true);
            Assert.Contains(attempts, a => a.Path.EndsWith("Game.tap", StringComparison.OrdinalIgnoreCase));
            // El TAP debe puntuar por encima del Z80 mid-load.
            var bestTape = attempts.Where(a => a.Path.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Score).First();
            var bestZ80 = attempts.Where(a => a.Path.EndsWith(".z80", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Score).FirstOrDefault();
            if (bestZ80 != null)
                Assert.True(bestTape.Score > bestZ80.Score, $"tap={bestTape.Score} z80={bestZ80.Score}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HealthCheck_IffOffTightLoop_IsUnhealthy()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        // Bucle cerrado en RAM con IFF=0 (síntoma 1942 Soft Library / flash sin EI).
        m.Memory.Write(0x8000, 0x18); // JR
        m.Memory.Write(0x8001, 0xFE); // -2 → bucle en 8000
        m.Registers.PC = 0x8000;
        m.Registers.IFF1 = false;
        m.Registers.IFF2 = false;
        m.Registers.Halted = false;

        var health = LoadHealthCheck.Evaluate(m, sampleFrames: 40, allowWarpBurst: false);
        Assert.Equal(LoadHealthVerdict.Unhealthy, health.Verdict);
        Assert.Contains("IFF", health.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthCheck_HaltWithIffOff_IsUnhealthy()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        m.Memory.Write(0x9000, 0x76); // HALT
        m.Registers.PC = 0x9000;
        m.Registers.IFF1 = false;
        m.Registers.IFF2 = false;
        m.Registers.Halted = true;

        var health = LoadHealthCheck.Evaluate(m, sampleFrames: 40, allowWarpBurst: false);
        Assert.Equal(LoadHealthVerdict.Unhealthy, health.Verdict);
    }

    [Fact]
    public void Elite1942_Orchestrator_HealthyWithIffOn()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string? zip = Find1942TapZip();
        if (zip is null) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        m.Reset();

        var result = LoadOrchestrator.OpenWithRetry(
            m, zip, RomMediaFormat.Auto, runHealthCheck: true, healthFrames: 60);
        Assert.True(result.Success, result.StatusMessage);
        Assert.True(m.Registers.IFF1, "IFF debe quedar activa tras USR");
        Assert.True(m.Registers.PC >= 0x8000 || m.Tape.UsedInstantLoad);
        Assert.NotEqual(0x6F97, m.Registers.PC);
    }

    [Fact]
    public void FlyingSharkTzx_Orchestrator_HealthyOrStillLoading()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string zip = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\FlyingShark.tzx.zip";
        if (!File.Exists(zip)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        m.Reset();

        var result = LoadOrchestrator.OpenWithRetry(
            m, zip, RomMediaFormat.Auto, runHealthCheck: true, healthFrames: 40);
        Assert.True(result.Success, result.StatusMessage);
        Assert.True(m.Tape.UsedInstantLoad);
        Assert.True(m.Tape.FastLoad);
        Assert.True(
            result.Health is null
            || result.Health.Verdict is LoadHealthVerdict.Healthy or LoadHealthVerdict.StillLoading);
    }

    [Fact]
    public void FlyingSharkTap_PrefersTzxInRanker()
    {
        string tap = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\FlyingShark.tap.zip";
        string tzx = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\FlyingShark.tzx.zip";
        if (!File.Exists(tap) || !File.Exists(tzx)) return;

        var attempts = LoadCandidateRanker.BuildAttempts(tap, RomMediaFormat.Auto, inspect: true);
        Assert.Contains(attempts, a => a.Path.EndsWith("FlyingShark.tzx.zip", StringComparison.OrdinalIgnoreCase));
        // El mejor intento debe ser TZX (hermano o preferido), no ForceTap del TAP BioToxin.
        var top = attempts[0];
        Assert.True(
            top.Path.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase),
            $"top={top.Path} strategy={top.Strategy} score={top.Score}");
        Assert.NotEqual(LoadStrategyKind.ForceTap, top.Strategy);
    }

    [Fact]
    public void LocalCandidates_PrefersTapeOverZ80_ForSameStem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "me-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "1942.z80.zip"), "z");
            File.WriteAllText(Path.Combine(dir, "1942.tap.zip"), "t");
            File.WriteAllText(Path.Combine(dir, "1942.tzx.zip"), "x");

            var entry = new CatalogEntry { Title = "1942", ScFile = "1942" };
            var ranked = LocalJuegosMatcher.FindLocalCandidates(entry, dir, RomMediaFormat.Auto);
            Assert.True(ranked.Count >= 3);
            string top = Path.GetFileName(ranked[0]);
            Assert.True(
                top.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)
                || top.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase),
                $"expected tape first, got {top}");
            Assert.DoesNotContain(ranked.Take(1), p => p.EndsWith(".z80.zip", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Arkanoid_Orchestrator_PrefersFlashableTap_WhenBothPresent()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string tap = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\Arkanoid.tap.zip";
        string tzx = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\Arkanoid.tzx.zip";
        if (!File.Exists(tap) || !File.Exists(tzx)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        m.Reset();

        var result = LoadOrchestrator.OpenWithRetry(
            m, tzx, RomMediaFormat.Auto,
            extraCandidates: [tap],
            runHealthCheck: true,
            healthFrames: 50);
        Assert.True(result.Success, result.StatusMessage);
        Assert.True(m.Tape.UsedInstantLoad || result.Path.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase),
            $"expected flashable path, got {result.Path} instant={m.Tape.UsedInstantLoad}");
        Assert.True(m.Registers.PC >= 0x4000 || m.Tape.IsFastLoadActive || m.Tape.UsedInstantLoad);
    }

    private static string? Find1942TapZip()
    {
        foreach (string zip in new[]
                 {
                     @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\1942.tap.zip",
                     @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\tmp-1942\tosec-tap.zip",
                 })
        {
            if (File.Exists(zip)) return zip;
        }
        return null;
    }
}
