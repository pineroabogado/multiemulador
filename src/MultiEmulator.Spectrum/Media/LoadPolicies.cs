using MultiEmulator.Spectrum.Cpu;
using MultiEmulator.Spectrum.Tape;

namespace MultiEmulator.Spectrum.Media;

/// <summary>
/// Políticas centrales de carga (IFF tras USR, Fast Load con turbo, etc.).
/// Evita ifs por título: se aplican por señales del medio / máquina.
/// Alineado con Spectaculator (FlashLoad + Fast Load + AutoLoad).
/// </summary>
public static class LoadPolicies
{
    /// <summary>
    /// Paridad con RANDOMIZE USR desde BASIC: IFF activas.
    /// Muchos stubs de juego no hacen EI; con DI el menú queda sordo a IRQ/teclado.
    /// </summary>
    public static void EnsureInterruptsAfterUsr(Z80Registers regs)
    {
        regs.Halted = false;
        regs.EIDeferred = false;
        regs.IFF1 = true;
        regs.IFF2 = true;
        if (regs.IM == 0)
            regs.IM = 1;
    }

    /// <summary>
    /// Entrada tras FlashLoad→USR (Spectaculator): IFF ON + un frame sin VBlank IRQ
    /// para que el stub pueda ejecutar DI antes de la interrupción (carga genérica).
    /// </summary>
    public static void ApplyFlashLoadUsrEntry(SpectrumMachine machine)
    {
        EnsureInterruptsAfterUsr(machine.Registers);
        machine.SuppressNextFrameIrq();
    }

    /// <summary>
    /// Si la cinta tiene bloques turbo/EAR restantes, asegura Fast Load (Spectaculator).
    /// </summary>
    public static void EnsureFastLoadForTurbo(TapeLoader tape)
    {
        if (tape.HasPendingEarBlocks() || tape.TapeHasTurboBlocks())
            tape.FastLoad = true;
    }

    /// <summary>Fast Load + warp de frames (p.ej. Flying Shark TZX turbo).</summary>
    public static void EnsureFastLoadWarp(TapeLoader tape)
    {
        tape.FastLoad = true;
        if (tape.FastLoadWarpFrames < 64)
            tape.FastLoadWarpFrames = 64;
    }

    /// <summary>Aplica ForceMachine48/128 desde el registro de soluciones (si hay).</summary>
    public static void ApplySolutionMachineHint(SpectrumMachine machine, LoadSolutionHint? hint)
    {
        if (hint == null) return;
        if (hint.HasTag(LoadSolutionTags.ForceMachine128))
            machine.SetMachine(MachineType.Spectrum128);
        else if (hint.HasTag(LoadSolutionTags.ForceMachine48))
            machine.SetMachine(MachineType.Spectrum48);
    }

    /// <summary>
    /// Arkanoid: activa sync 1×/frame (FB locked tras wait+doble CALL).
    /// Mantiene contención I/O Fuse; no parchea RAM.
    /// </summary>
    public static void ApplyFloatingBusVsyncLatch(SpectrumMachine machine, bool enabled = true)
    {
        machine.Ula.LatchFloatingBusOncePerFrame = false;
        machine.PendingArkanoidVsyncPatch = false;
        machine.ArkanoidVsyncThrottle = enabled;
    }

    /// <summary>
    /// Obsoleto: el trampolín EBA4 rompía el doble CALL 848F y el FX.
    /// Se mantiene por si hay estado pendiente; ya no se activa desde el catálogo.
    /// </summary>
    public static bool TryApplyArkanoidVsyncEdgePatch(SpectrumMachine machine)
    {
        var mem = machine.Memory;
        if (mem.Read(0x848F) == 0xF3 && mem.Read(0x8490) == 0xC3
            && mem.Read(0x8491) == 0xA4 && mem.Read(0x8492) == 0xEB)
            return true;

        if (mem.Read(0x848F) != 0xF3 || mem.Read(0x8490) != 0x01
            || mem.Read(0x8491) != 0xFF || mem.Read(0x8492) != 0x28)
            return false;

        mem.Write(0x848F, 0xF3);
        mem.Write(0x8490, 0xC3);
        mem.Write(0x8491, 0xA4);
        mem.Write(0x8492, 0xEB);

        byte[] routine =
        [
            0x01, 0xFF, 0x28,
            0x1E, 0x30,
            0xED, 0x78,
            0x3C,
            0x20, 0x0B,
            0x1D,
            0x20, 0xF8,
            0xED, 0x78,
            0x3C,
            0x28, 0xFB,
            0xC3, 0x98, 0x84,
            0x1E, 0x30,
            0x18, 0xEC
        ];
        for (int i = 0; i < routine.Length; i++)
            mem.Write((ushort)(0xEBA4 + i), routine[i]);
        return true;
    }

    /// <summary>Nombre corto de estrategia para UI / metadatos.</summary>
    public static string StrategyDisplayName(LoadStrategyKind kind) => kind switch
    {
        LoadStrategyKind.Default => "por defecto",
        LoadStrategyKind.PreferTzx => "preferir TZX",
        LoadStrategyKind.ForceTap => "forzar TAP (HLE)",
        LoadStrategyKind.ForceTzx => "forzar TZX",
        LoadStrategyKind.SiblingTape => "cinta hermana",
        LoadStrategyKind.SkipSnapshot => "omitir snapshot",
        LoadStrategyKind.CascadeNext => "siguiente formato",
        _ => kind.ToString()
    };
}

/// <summary>Estrategia de apertura de medio (reintento automático).</summary>
public enum LoadStrategyKind
{
    Default = 0,
    PreferTzx = 1,
    ForceTap = 2,
    ForceTzx = 3,
    SiblingTape = 4,
    SkipSnapshot = 5,
    CascadeNext = 6,
}
