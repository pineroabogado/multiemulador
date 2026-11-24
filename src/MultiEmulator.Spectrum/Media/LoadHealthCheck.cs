using MultiEmulator.Spectrum.Tape;

namespace MultiEmulator.Spectrum.Media;

public enum LoadHealthVerdict
{
    Healthy = 0,
    Unhealthy = 1,
    /// <summary>Carga aún en curso (turbo/EAR); no reintentar todavía.</summary>
    StillLoading = 2,
}

public sealed class LoadHealthResult
{
    public LoadHealthVerdict Verdict { get; init; }
    public string Reason { get; init; } = "";
    public ushort FinalPc { get; init; }
    public bool FinalIff1 { get; init; }
    public int FramesRun { get; init; }
    public int DistinctPcs { get; init; }
    public int ScreenDelta { get; init; }
    public int TapeDelta { get; init; }
}

/// <summary>
/// Health-check post-carga: ejecuta N frames y detecta cuelgues típicos
/// (IFF off + bucle; Searching/Loading; cinta idle; snapshot mid-load).
/// Heurísticas generales — sin ifs por título.
/// </summary>
public static class LoadHealthCheck
{
    /// <summary>Frames por defecto (~1–2 s a 50 Hz si el host no warpea).</summary>
    public const int DefaultSampleFrames = 80;

    public static LoadHealthResult Evaluate(
        SpectrumMachine machine,
        int sampleFrames = DefaultSampleFrames,
        bool allowWarpBurst = true)
    {
        var tape = machine.Tape;
        int framesRun = 0;

        // Si hay turbo EAR activo, acelerar hasta que termine (o tope corto).
        if (allowWarpBurst && tape.IsFastLoadActive)
        {
            framesRun += tape.RunFastLoadBurst(
                () => machine.RunFrame(),
                maxHostTicks: 120,
                until: () => !tape.IsFastLoadActive);
        }

        int screen0 = CountNonZero(machine.Memory.ScreenBank.AsSpan(0, 0x1800));
        int tapePos0 = tape.Tape?.Position ?? -1;
        ushort pc0 = machine.Registers.PC;

        var pcHist = new Dictionary<ushort, int>();
        int haltedIffOff = 0;
        int ldBytesStuck = 0;
        int samePc = 0;
        ushort lastPc = pc0;

        int n = Math.Max(16, sampleFrames);
        for (int i = 0; i < n; i++)
        {
            if (tape.IsFastLoadActive)
            {
                int warp = Math.Max(1, tape.FastLoadWarpFrames);
                for (int w = 0; w < warp; w++)
                    machine.RunFrame();
                framesRun += warp;
            }
            else
            {
                machine.RunFrame();
                framesRun++;
            }

            ushort pc = machine.Registers.PC;
            pcHist[pc] = pcHist.TryGetValue(pc, out int c) ? c + 1 : 1;

            if (pc == lastPc) samePc++;
            else samePc = 0;
            lastPc = pc;

            bool iff = machine.Registers.IFF1;
            if (machine.Registers.Halted && !iff)
                haltedIffOff++;

            if (pc is >= 0x0556 and <= 0x0605)
            {
                int pos = tape.Tape?.Position ?? -1;
                if (pos == tapePos0)
                    ldBytesStuck++;
            }
        }

        int screen1 = CountNonZero(machine.Memory.ScreenBank.AsSpan(0, 0x1800));
        int tapePos1 = tape.Tape?.Position ?? -1;
        int tapeDelta = tapePos1 < 0 || tapePos0 < 0 ? 0 : tapePos1 - tapePos0;
        int screenDelta = Math.Abs(screen1 - screen0);
        ushort finalPc = machine.Registers.PC;
        bool finalIff = machine.Registers.IFF1;
        bool instant = tape.UsedInstantLoad;
        bool screenAlive = screen1 > 200;

        // Solo «StillLoading» para turbo/FastLoad real (Spectaculator Fast Load).
        // Un LOAD "" lento por trampa ROM sin flash no debe aceptarse si hay hermanos flashables.
        if (tape.IsFastLoadActive)
        {
            return Ok(LoadHealthVerdict.StillLoading, "carga turbo/EAR en curso", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
        }
        if (tape.TapeHasTurboBlocks()
            && tape.HasPendingEarBlocks()
            && tape.Tape is { IsPlaying: true }
            && tapeDelta > 0)
        {
            return Ok(LoadHealthVerdict.StillLoading, "carga turbo/EAR en curso", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
        }

        // Instant OK: PC en RAM. IFF puede apagarse luego (floating bus / DI del juego).
        if (instant && finalPc >= 0x4000 && screenAlive)
            return Ok(LoadHealthVerdict.Healthy, "instant OK", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);

        // Instant saltó a USR pero cayó a ROM con IFF off → fallo (p.ej. stub sin EI + crash).
        if (instant && finalPc < 0x4000 && !finalIff && samePc > n / 2)
            return Fail("instant cayó a ROM con IFF off", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);

        // Editor BASIC / © sin flash: carga no arrancó (o la cinta se agotó sin juego).
        bool tapeExhausted = tape.Tape is { } tEx
            && tEx.Position >= tEx.Blocks.Count
            && !tEx.IsPlaying;
        if (!instant
            && IsBasicEditorPc(finalPc)
            && !tape.IsFastLoadActive
            && (tapeDelta == 0 || tapeExhausted)
            && (screenDelta < 16 || screen1 < 400))
        {
            return Fail(
                tapeExhausted ? "cinta agotada; sigue en editor BASIC" : "editor BASIC sin progreso de carga",
                finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
        }

        // HALT + IFF=0 → soft-lock (salvo que ya aceptáramos instant en RAM arriba).
        if (haltedIffOff > n / 2)
            return Fail("HALT con IFF desactivadas", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);

        // IFF off + PC clavado:
        // - En ROM: casi siempre fallo (salvo LD-BYTES con cinta).
        // - En RAM sin pantalla y sin instant: bucle sordo vacío.
        // - En RAM con pantalla tras instant: floating-bus / DI del juego → OK (arriba).
        if (!finalIff && samePc > n * 2 / 3 && !tape.IsFastLoadActive)
        {
            if (finalPc is >= 0x0556 and <= 0x0605 && tapeDelta > 0)
            { /* carga legítima */ }
            else if (finalPc < 0x4000)
                return Fail("IFF off + bucle PC en ROM", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
            else if (!instant && !screenAlive)
                return Fail("IFF off + bucle vacío en RAM", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
        }

        if (ldBytesStuck > n / 2 && tapeDelta == 0)
            return Fail("Searching/Loading sin progreso de cinta", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);

        if (tape.Tape is { IsPlaying: true }
            && tapeDelta == 0
            && !tape.IsFastLoadActive
            && !instant
            && screenDelta < 8
            && pcHist.Count <= 3
            && finalPc < 0x4000)
        {
            return Fail("cinta idle sin progreso (pantalla de carga)", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
        }

        if (tape.Tape == null
            && finalPc < 0x4000
            && LoadMediaInspect.LooksLikeMidLoadPc(finalPc, finalIff)
            && screenDelta < 16)
        {
            return Fail("snapshot mid-load (PC en ROM de carga)", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
        }

        if (tape.Tape == null
            && screenDelta == 0
            && pcHist.Count == 1
            && !finalIff
            && finalPc >= 0x4000
            && !screenAlive)
        {
            return Fail("pantalla congelada + IFF off", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
        }

        return Ok(LoadHealthVerdict.Healthy, "ok", finalPc, finalIff, framesRun, pcHist.Count, screenDelta, tapeDelta);
    }

    /// <summary>PC típicos del editor BASIC / prompt tras NEW.</summary>
    public static bool IsBasicEditorPc(ushort pc) =>
        pc is (>= 0x12A2 and <= 0x12F0)
            or (>= 0x15AF and <= 0x15F0)
            or 0x1B9E // LINE-NEW
            or (>= 0x0F2C and <= 0x0F50); // main-exec / error paths cercanos

    private static LoadHealthResult Ok(
        LoadHealthVerdict v, string reason, ushort pc, bool iff,
        int frames, int distinct, int scr, int tape) =>
        new()
        {
            Verdict = v,
            Reason = reason,
            FinalPc = pc,
            FinalIff1 = iff,
            FramesRun = frames,
            DistinctPcs = distinct,
            ScreenDelta = scr,
            TapeDelta = tape
        };

    private static LoadHealthResult Fail(
        string reason, ushort pc, bool iff,
        int frames, int distinct, int scr, int tape) =>
        Ok(LoadHealthVerdict.Unhealthy, reason, pc, iff, frames, distinct, scr, tape);

    public static int CountNonZero(ReadOnlySpan<byte> s)
    {
        int n = 0;
        foreach (var b in s)
            if (b != 0) n++;
        return n;
    }
}
