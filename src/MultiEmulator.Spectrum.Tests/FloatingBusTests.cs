using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Ula;

namespace MultiEmulator.Spectrum.Tests;

public class FloatingBusTests
{
    [Fact]
    public void MemoryContention_PaperPhase_MatchesFuse65432100()
    {
        // Primer T contendido = firstData - 1; tabla Fuse {5,4,3,2,1,0,0,6}.
        int t0 = SpectrumUla.FloatingBusFirstPaper48 - 1;
        Assert.Equal(5, SpectrumUla.MemoryContentionDelay(t0, false));
        Assert.Equal(4, SpectrumUla.MemoryContentionDelay(t0 + 1, false));
        Assert.Equal(0, SpectrumUla.MemoryContentionDelay(t0 + 5, false));
        Assert.Equal(6, SpectrumUla.MemoryContentionDelay(t0 + 7, false));
        Assert.Equal(0, SpectrumUla.MemoryContentionDelay(t0 - 1, false));
        Assert.Equal(0, SpectrumUla.MemoryContentionDelay(t0 + 128, false)); // borde derecho
    }

    [Fact]
    public void ReadFloatingBus_IdleOutsidePaper_ReturnsFf()
    {
        var ula = new SpectrumUla();
        var screen = new byte[0x1B00];
        screen[0] = 0xA5;
        Assert.Equal(0xFF, ula.ReadFloatingBus(screen, 0, is128K: false));
        // firstData - 1 está en fase idle del ciclo Fuse (fase 1).
        Assert.Equal(0xFF, ula.ReadFloatingBus(screen, SpectrumUla.FloatingBusFirstPaper48 - 1, is128K: false));
    }

    [Fact]
    public void ReadFloatingBus_FirstPaperFetch_MatchesFusePhase()
    {
        var ula = new SpectrumUla();
        var screen = new byte[0x1B00];
        screen[0] = 0x3C;           // bitmap (0,0)
        screen[0x1800] = 0x47;      // attr (0,0)
        screen[1] = 0x5A;           // bitmap (0,1)
        screen[0x1801] = 0x28;      // attr (0,1)

        int t0 = SpectrumUla.FloatingBusFirstPaper48;
        // Fuse: idle, idle, bitmap, attr, bitmap+1, attr+1, idle, idle
        Assert.Equal(0xFF, ula.ReadFloatingBus(screen, t0 - 2, false));
        Assert.Equal(0xFF, ula.ReadFloatingBus(screen, t0 - 1, false));
        Assert.Equal(0x3C, ula.ReadFloatingBus(screen, t0, false));
        Assert.Equal(0x47, ula.ReadFloatingBus(screen, t0 + 1, false));
        Assert.Equal(0x5A, ula.ReadFloatingBus(screen, t0 + 2, false));
        Assert.Equal(0x28, ula.ReadFloatingBus(screen, t0 + 3, false));
        Assert.Equal(0xFF, ula.ReadFloatingBus(screen, t0 + 4, false));
        Assert.Equal(0xFF, ula.ReadFloatingBus(screen, t0 + 5, false));
    }

    [Fact]
    public void ArkanoidTap_StartGame_LeavesFloatingBusWaitLoop()
    {
        if (!TestRomPaths.TryRead48(out var rom))
            return;
        string zip = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JUEGOS", "Arkanoid.tap.zip"));
        if (!File.Exists(zip))
            return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        m.Joystick.Type = JoystickType.Kempston;
        for (int i = 0; i < 120; i++) m.RunFrame();
        MediaLoader.OpenPath(m, zip);

        for (int f = 0; f < 1500; f++)
        {
            m.RunFrame();
            if (m.Tape.Tape is { IsPlaying: false } && m.Tape.Tape.Position >= m.Tape.Tape.Blocks.Count)
                break;
        }

        // Entrada tras multicargador (investigación): rutina que acaba en espera floating bus.
        m.Registers.PC = 0x83A0;
        m.Registers.IFF1 = m.Registers.IFF2 = false;
        m.Registers.Halted = false;

        for (int f = 0; f < 200; f++) m.RunFrame();

        // Disparar inicio (1 / 0 / SPACE / Kempston).
        m.Keyboard.SetKey(3, 0, true); // 1
        for (int i = 0; i < 4; i++) m.RunFrame();
        m.Keyboard.SetKey(3, 0, false);
        m.Joystick.Fire = true;
        for (int i = 0; i < 8; i++) m.RunFrame();
        m.Joystick.Fire = false;

        var pcs = new Dictionary<ushort, int>();
        int waitOnly = 0;
        for (int f = 0; f < 400; f++)
        {
            m.RunFrame();
            ushort pc = m.Registers.PC;
            pcs[pc] = pcs.GetValueOrDefault(pc) + 1;
            // El wait de vsync cruza el fin de frame → PC suele verse en 848F..8497;
            // el fallo real es no salir nunca hacia la lógica de partida.
            if (pc is >= 0x848F and <= 0x8497)
                waitOnly++;
        }

        bool reachedGame = pcs.Keys.Any(pc =>
            pc is (>= 0x9300 and <= 0x94FF) or (>= 0xF000 and <= 0xF0FF));
        Assert.True(
            reachedGame,
            $"Arkanoid no salió del floating-bus wait (waitFrames={waitOnly}, top={string.Join(',', pcs.OrderByDescending(x => x.Value).Take(8).Select(x => $"{x.Key:X4}:{x.Value}"))})");
    }
}
