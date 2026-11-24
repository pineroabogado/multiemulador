using MultiEmulator.Spectrum.Audio;
using MultiEmulator.Spectrum.Cpu;
using MultiEmulator.Spectrum.Disk;
using MultiEmulator.Spectrum.Input;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Memory;
using MultiEmulator.Spectrum.Poke;
using MultiEmulator.Spectrum.State;
using MultiEmulator.Spectrum.Tape;
using MultiEmulator.Spectrum.Ula;

namespace MultiEmulator.Spectrum;

public sealed class SpectrumMachine
{
    private readonly SpectrumMemory _memory = new();
    private readonly SpectrumUla _ula = new();
    private readonly Ay8912 _ay = new();
    private readonly BeeperBuffer _beeper = new();
    private readonly KeyboardMatrix _keyboard = new();
    private readonly JoystickState _joystick = new();
    private readonly Z80Cpu _cpu;
    private readonly TapeLoader _tapeLoader;
    private readonly TrDosLoader _trDos = new();
    private readonly BetaDisk _beta = new();
    private int _frameTStates;
    private int _tStatesThisFrame;
    private byte _aySelectPort;
    private int _suppressIrqFrames;
    private bool _pendingArkanoidVsyncPatch;
    /// <summary>
    /// Arkanoid: tras el wait 848F (+ margen del doble CALL), el floating bus
    /// devuelve FF el resto del frame → 1 bucle/frame sin matar el beeper.
    /// </summary>
    private bool _arkanoidVsyncThrottle;
    private int _arkanoidFbArmT = -1;
    private bool _arkanoidFbLocked;
    private bool _traceEarBit;
    /// <summary>T de la instrucción ya enviados al beeper (antes de un OUT FE a mitad).</summary>
    private int _beeperPartialDone;

    public SpectrumMachine()
    {
        _cpu = new Z80Cpu(ReadMem, WriteMem, ReadPort, WritePort);
        // Contención ULA: el callback recibe (addr, tParcialInstrucción).
        _cpu.ContendMemory = (addr, partialT) =>
        {
            if (addr < 0x4000 || addr >= 0x8000)
                return 0;
            // 128K: además banks contendidas vía 7FFD — aproximación: siempre
            // contender $4000–$7FFF (bank 5/7 display + bank 5 contended).
            bool is128 = MachineType == MachineType.Spectrum128;
            return SpectrumUla.MemoryContentionDelay(_tStatesThisFrame + partialT, is128);
        };
        // Fuse/ZEsarUX ula_contend_port_*: OUT FE del beeper y floating bus.
        _cpu.ContendPortEarly = (port, partialT) =>
            SpectrumUla.PortContentionEarly(
                port, _tStatesThisFrame + partialT, MachineType == MachineType.Spectrum128);
        _cpu.ContendPortLate = (port, partialT) =>
            SpectrumUla.PortContentionLate(
                port, _tStatesThisFrame + partialT, MachineType == MachineType.Spectrum128);
        _tapeLoader = new TapeLoader(_memory, _cpu.Registers);
        SetMachine(MachineType.Spectrum48);
    }

    public Z80Registers Registers => _cpu.Registers;
    public SpectrumMemory Memory => _memory;
    public SpectrumUla Ula => _ula;
    public Ay8912 Ay => _ay;
    public KeyboardMatrix Keyboard => _keyboard;
    public JoystickState Joystick => _joystick;
    public TapeLoader Tape => _tapeLoader;
    public TrDosLoader TrDos => _trDos;
    public BetaDisk Beta => _beta;
    public MachineType MachineType { get; private set; } = MachineType.Spectrum48;
    public bool Paused { get; set; }

    /// <summary>
    /// Obsoleto: el vsync de Arkanoid usa <see cref="ArkanoidVsyncThrottle"/>.
    /// </summary>
    public bool PendingArkanoidVsyncPatch
    {
        get => _pendingArkanoidVsyncPatch;
        set => _pendingArkanoidVsyncPatch = value;
    }

    /// <summary>
    /// Arkanoid: 1 sync útil/frame. Tras salir del wait 848F deja ~900 T para el
    /// doble CALL (8E8C); luego el FB queda en FF hasta el próximo frame para que
    /// un segundo bucle espere de verdad (CPU en el wait, beeper del primer bucle OK).
    /// </summary>
    public bool ArkanoidVsyncThrottle
    {
        get => _arkanoidVsyncThrottle;
        set => _arkanoidVsyncThrottle = value;
    }

    /// <summary>
    /// Diagnóstico de FX: cuenta T en el wait 848F, flancos EAR y sitios de OUT (FE).
    /// Inactivo por defecto; el coste es un bool en el bucle de frame.
    /// </summary>
    public bool BeeperTrace { get; set; }

    public int TraceEarEdges { get; private set; }
    public int TraceFeWrites { get; private set; }
    public long TraceWaitT { get; private set; }
    public long TraceRunT { get; private set; }
    public Dictionary<int, int> TraceFeSites { get; } = new();

    public void ResetBeeperTrace()
    {
        TraceEarEdges = 0;
        TraceFeWrites = 0;
        TraceWaitT = 0;
        TraceRunT = 0;
        TraceFeSites.Clear();
        _traceEarBit = _ula.EarBit;
    }

    public void SetMachine(MachineType type)
    {
        MachineType = type;
        _memory.SetMachine(type);
        _frameTStates = type == MachineType.Spectrum128
            ? SpectrumUla.FrameTStates128
            : SpectrumUla.FrameTStates48;
        _beeper.SetFrameTStates(_frameTStates);
        Reset();
    }

    public void LoadSystemRoms(byte[]? rom48, byte[]? rom128)
    {
        if (rom48 != null) _memory.LoadRom48(rom48);
        if (rom128 != null) _memory.LoadRom128(rom128);
    }

    public void LoadTrDosRom(byte[] rom)
    {
        _memory.LoadTrDosRom(rom);
        _beta.Enabled = true;
    }

    public void Reset()
    {
        // Cancelar auto-LOAD "" pendiente y soltar teclas (evita Break / "?"L tras reset+catálogo).
        _tapeLoader.CancelAutoLoad();
        _cpu.Reset();
        _memory.ResetRam();
        _memory.TrDosPaged = false;
        _beta.Reset();
        _ula.Reset();
        _ay.Reset();
        _keyboard.Reset();
        _joystick.Reset();
        _tStatesThisFrame = 0;
        _suppressIrqFrames = 0;
        _pendingArkanoidVsyncPatch = false;
        _arkanoidVsyncThrottle = false;
        _arkanoidFbArmT = -1;
        _arkanoidFbLocked = false;
        _beeperPartialDone = 0;
        _cpu.Registers.PC = 0;
        _cpu.Registers.SP = 0xFFFF;
        _cpu.Registers.I = 0x3F;
        _cpu.Registers.IM = 1;
        _beeper.Reset();
        _tapeLoader.ClearPendingKey();
    }

    /// <summary>
    /// Arranque instantáneo de cinta sin teclear LOAD "": espera sysvars BASIC,
    /// flashea Program+CODE y salta a USR (o LINE). Turbo/huérfanos → EAR/trampa.
    /// </summary>
    public bool TryInstantTapeStart()
    {
        if (_tapeLoader.Tape == null)
            return false;
        if (!_tapeLoader.CanInstantFlashStart())
            return false;

        _tapeLoader.Tape.IsPlaying = false;
        _keyboard.ReleaseAll();
        _tapeLoader.CancelAutoLoad();

        if (!IsBasicReady())
        {
            for (int i = 0; i < 300; i++)
            {
                RunFrame();
                if (i >= 50 && IsBasicReady())
                    break;
            }
        }

        if (!IsBasicReady())
            return false;

        _keyboard.ReleaseAll();
        _tapeLoader.ClearPendingKey();
        if (!_tapeLoader.TryInstantFlashStart())
            return false;

        // Spectaculator FlashLoad: el stub USR suele empezar con DI (p.ej. Arkanoid @FF37).
        // Si el siguiente RunFrame dispara IRQ de VBlank antes de ejecutar ese DI,
        // el cargador cae en ROM (PC=02AB) con IFF=0. Un frame sin IRQ deja correr el DI.
        // IFF quedan ON (política EnsureInterruptsAfterUsr / 1942).
        SuppressNextFrameIrq();
        return true;
    }

    /// <summary>
    /// Omite la IRQ de VBlank de los próximos <paramref name="frames"/> frames (entrada flash→USR).
    /// </summary>
    public void SuppressNextFrameIrq(int frames = 1) =>
        _suppressIrqFrames = Math.Max(_suppressIrqFrames, Math.Max(1, frames));

    /// <summary>
    /// Prepara el editor BASIC (pasa la ©) para el fallback LAST_K LOAD "".
    /// </summary>
    public void EnsureBasicEditorReady()
    {
        _keyboard.ReleaseAll();
        _tapeLoader.ClearPendingKey();
        bool sawCopyright = false;
        for (int i = 0; i < 500; i++)
        {
            ushort pc = _cpu.Registers.PC;
            if (pc is >= 0x11C0 and <= 0x11F0)
            {
                sawCopyright = true;
                TryInjectKey(0x0D); // Enter para pasar ©
            }
            else if (pc == 0x15E6 && (sawCopyright || i > 80))
            {
                _tapeLoader.ClearPendingKey();
                _keyboard.ReleaseAll();
                return;
            }
            RunFrame();
        }
        _tapeLoader.ClearPendingKey();
        _keyboard.ReleaseAll();
    }

    private void TryInjectKey(byte code)
    {
        byte flags = _memory.Read(23611);
        if ((flags & 0x20) != 0)
            return;
        _memory.Write(23560, code);
        _memory.Write(23611, (byte)(flags | 0x20));
    }

    private bool IsBasicReady()
    {
        // Tras NEW/boot: RAMTOP (23732) y PROG en zona típica 48K (vale © / press any key).
        ushort ramtop = (ushort)(_memory.Read(23732) | (_memory.Read(23733) << 8));
        ushort prog = (ushort)(_memory.Read(23635) | (_memory.Read(23636) << 8));
        return ramtop >= 0x5D00 && prog >= 0x5C00 && _cpu.Registers.PC > 0x00A0;
    }

    public void RunFrame()
    {
        if (Paused) return;

        // Parche RAM obsoleto (edge@EBA4): solo si quedó pendiente de sesiones viejas.
        if (_pendingArkanoidVsyncPatch
            && LoadPolicies.TryApplyArkanoidVsyncEdgePatch(this))
            _pendingArkanoidVsyncPatch = false;

        // Arkanoid en partida: wait DI/LD BC,28FF @848F — activar sync 1× aunque
        // el catálogo no haya aplicado FloatingBusVsyncLatch (carga por fichero).
        if (!_arkanoidVsyncThrottle
            && _memory.Read(0x848F) == 0xF3
            && _memory.Read(0x8490) == 0x01
            && _memory.Read(0x8491) == 0xFF
            && _memory.Read(0x8492) == 0x28)
        {
            _arkanoidVsyncThrottle = true;
        }

        if (_joystick.Type != JoystickType.Kempston)
            _joystick.ApplySinclair(_keyboard);

        _tapeLoader.ApplyAutoType(_keyboard);

        // Beta: paginar TR-DOS antes de la IRQ si PC ya está en $3Dxx
        // (si no, el RST $38 usa la ROM Spectrum y rompe la entrada a DOS).
        _beta.OnM1(_cpu.Registers.PC);
        _memory.TrDosPaged = _beta.TrDosPaged;

        // Speedlock/turbo (EAR): no disparar IRQ de frame mientras hay pulsos.
        // No forzar DI en IFF: Flying Shark (y otros) terminan con HALT+EI;
        // borrar IFF dejaba HALT colgado para siempre (PC=C6A9).
        bool earTurbo = _tapeLoader.Pulses.IsActive;
        bool suppressIrq = _suppressIrqFrames > 0 || earTurbo;
        if (_suppressIrqFrames > 0)
            _suppressIrqFrames--;
        if (!suppressIrq)
        {
            // IRQ al inicio del frame (VBlank), como la ULA real. Antes se
            // disparaba tras Render y no se contabilizaban las T del acknowledge
            // → desfase del multiplexado de sprites (p.ej. Green Beret).
            int irqT = _cpu.Interrupt();
            if (irqT > 0)
                AdvanceSides(irqT);
        }

        // Cada frame: rearmar FB (el lock solo vale dentro del frame actual).
        _arkanoidFbArmT = -1;
        _arkanoidFbLocked = false;

        bool trappedThisFrame = false;
        while (_tStatesThisFrame < _frameTStates)
        {
            if (_tapeLoader.TryTrapLoad())
            {
                trappedThisFrame = true;
                // Flash-load: mantener DI como la ROM (LD-BYTES). Evita que una EI
                // del cargador + IRQ del mismo frame salte a 0x38 a mitad de la carga.
                _cpu.Registers.IFF1 = false;
                _cpu.Registers.IFF2 = false;
                _cpu.Registers.EIDeferred = false;
                AdvanceSides(100);
                continue;
            }

            // HLE solo si no hay ROM TR-DOS real (Beta se encarga con paging mágico).
            if (!_memory.TrDosRomLoaded && _trDos.TryTrapEntry(this))
            {
                trappedThisFrame = true;
                AdvanceSides(100);
                continue;
            }

            // Paginación mágica Beta128 antes del fetch M1.
            _beta.OnM1(_cpu.Registers.PC);
            _memory.TrDosPaged = _beta.TrDosPaged;

            ushort pcBefore = _cpu.Registers.PC;
            bool inWait = pcBefore is >= 0x848F and <= 0x8497;
            // Step() ya acumula Registers.TStates; no volver a sumar aquí.
            int stepT = _cpu.Step();
            if (BeeperTrace)
            {
                TraceRunT += stepT;
                if (inWait)
                    TraceWaitT += stepT;
            }

            AdvanceSides(stepT);

            // Arkanoid: salida del wait → armar lock FB tras margen del doble CALL.
            if (_arkanoidVsyncThrottle && inWait && _cpu.Registers.PC == 0x8498)
            {
                if (_arkanoidFbArmT < 0)
                    _arkanoidFbArmT = _tStatesThisFrame;
            }

            // ~120 T: cabe el doble CALL 8E8C; un bucle de juego no.
            if (_arkanoidVsyncThrottle
                && !_arkanoidFbLocked
                && _arkanoidFbArmT >= 0
                && _tStatesThisFrame - _arkanoidFbArmT > 120)
            {
                _arkanoidFbLocked = true;
            }
        }

        _tStatesThisFrame -= _frameTStates;
        _ula.Render(_memory.ScreenBank);
        _ula.EndFrame();
        _arkanoidFbLocked = false;
        _arkanoidFbArmT = -1;

        // No tocar IFF tras flash-load: el RET de LD-BYTES (05E2) vuelve al caller
        // que puede hacer EI en el mismo frame; un DI aquí soft-lockeaba Green Beret
        // (PC=02B0, IFF=0) y otros loaders BASIC. Tampoco DI al final por EAR turbo
        // (ver comentario al inicio del frame).
        if (trappedThisFrame && !_tapeLoader.Pulses.IsActive)
        {
            // Evitar IRQ inmediata al inicio del siguiente frame justo tras la trampa
            // (cargador con EI + PC aún en ROM). Se consume en el próximo RunFrame.
            _suppressIrqFrames = Math.Max(_suppressIrqFrames, 1);
        }

        // Transporte VHS (FF/REW) — en caliente, tras el frame de CPU/EAR.
        _tapeLoader.TickTransport(_frameTStates);

        // Cinta agotada: dejar de marcar IsPlaying (los pulsos EAR ya no aplican).
        if (_tapeLoader.Tape is { IsPlaying: true } &&
            _tapeLoader.Tape.Position >= _tapeLoader.Tape.Blocks.Count &&
            !_tapeLoader.Pulses.IsActive &&
            _tapeLoader.Transport.Mode == TapeTransportMode.Play)
        {
            _tapeLoader.Tape.IsPlaying = false;
        }
    }

    private void AdvanceSides(int t)
    {
        if (t <= 0) return;
        _tStatesThisFrame += t;
        // Cargador en RAM (no LD-BYTES): emitir EAR también en bloques TAP/0x10.
        ushort pc = _cpu.Registers.PC;
        bool inLdBytes = pc is >= 0x0556 and <= 0x0605;
        _tapeLoader.Pulses.PlayStandardBlocksViaEar = !inLdBytes && pc >= 0x5D00;
        _tapeLoader.AdvancePulses(t);
        _ula.TapeIn = _tapeLoader.EarLevel;

        // Si hubo OUT FE a mitad de instrucción, esos T ya fueron al beeper al nivel anterior.
        int already = _beeperPartialDone;
        _beeperPartialDone = 0;
        int remain = t - already;
        if (remain > 0)
            AdvanceBeeper(remain);
    }

    private void AdvanceBeeper(int t)
    {
        if (BeeperTrace && _ula.EarBit != _traceEarBit)
        {
            TraceEarEdges++;
            _traceEarBit = _ula.EarBit;
        }

        _beeper.Advance(t, _ula.EarBit, _ula.MicOut);
    }

    public void ReadAudioSamples(Span<float> dst)
    {
        _beeper.Read(dst);
        if (MachineType == MachineType.Spectrum128)
        {
            for (int i = 0; i < dst.Length; i++)
            {
                float ay = _ay.RenderSample() * 0.18f;
                dst[i] = Math.Clamp(dst[i] + ay, -1f, 1f);
            }
        }

        // FF/REW: silenciar para no saturar el beeper con chirps a ×8.
        if (_tapeLoader.MuteBeeper)
            dst.Clear();
    }

    public float MixAudioSample()
    {
        Span<float> one = stackalloc float[1];
        ReadAudioSamples(one);
        return one[0];
    }

    public void LoadSnapshot(byte[] data, string extension)
    {
        extension = extension.ToLowerInvariant();
        if (extension == ".sna")
        {
            SnapshotLoader.LoadSna(data, _cpu.Registers, _memory, out var border);
            _ula.Border = border;
            MachineType = _memory.Machine;
            _frameTStates = MachineType == MachineType.Spectrum128
                ? SpectrumUla.FrameTStates128
                : SpectrumUla.FrameTStates48;
        }
        else if (extension == ".z80")
        {
            SnapshotLoader.LoadZ80(data, _cpu.Registers, _memory, out var border, out var machine);
            _ula.Border = border;
            MachineType = machine;
            _frameTStates = machine == MachineType.Spectrum128
                ? SpectrumUla.FrameTStates128
                : SpectrumUla.FrameTStates48;
        }
        else throw new NotSupportedException($"Snapshot {extension} not supported");
    }

    public void InsertTape(TapeImage tape)
    {
        _trDos.Eject();
        _beta.Eject();
        _beta.Enabled = _memory.TrDosRomLoaded;
        _tapeLoader.Insert(tape);
    }

    /// <summary>
    /// Inserta un .trd. Con ROM TR-DOS: Beta128 + <c>RANDOMIZE USR 15619: REM: RUN</c>.
    /// Sin ROM: flash HLE del fichero de autorun (boot → RUN "…").
    /// </summary>
    public void LoadTrd(byte[] trdData)
    {
        _tapeLoader.Eject();
        _tapeLoader.CancelAutoLoad();
        _keyboard.ReleaseAll();

        if (!_memory.TrDosRomLoaded)
        {
            string? romPath = Services.AppPaths.TryFindSystemRom("trdos.rom");
            if (romPath != null)
                LoadTrDosRom(File.ReadAllBytes(romPath));
        }

        var disk = TrdImage.Load(trdData);
        _trDos.Insert(disk);
        _beta.Insert(trdData);
        _beta.Enabled = _memory.TrDosRomLoaded;

        SetMachine(MachineType.Spectrum48);
        WarmUpToBasicOk();

        _keyboard.ReleaseAll();
        _tapeLoader.ClearPendingKey();

        // Preferir HLE (flash autorun): Beta+TR-DOS ROM queda montado para
        // USR 15619 en caliente, pero el inject LINE-NEW aún no es fiable.
        _trDos.AutoBootReady(this);
    }

    /// <summary>Pasa la pantalla © y deja el editor en MAIN-EXEC ($15E6).</summary>
    private void WarmUpToBasicOk()
    {
        for (int i = 0; i < 120; i++)
            RunFrame();

        for (int i = 0; i < 80; i++)
        {
            ushort pc = _cpu.Registers.PC;
            if (pc == 0x15E6)
                break;
            if (pc is >= 0x0E00 and <= 0x11FF)
                TryInjectKey(0x0D);
            RunFrame();
        }

        for (int i = 0; i < 120 && _cpu.Registers.PC != 0x15E6; i++)
            RunFrame();

        _tapeLoader.ClearPendingKey();
        _keyboard.ReleaseAll();
    }

    public void ApplyPoke(ushort address, byte value) => PokeService.Apply(_memory, address, value);

    public void ApplyPokes(IEnumerable<PokeEntry> entries) => PokeService.Apply(_memory, entries);

    public SaveStateData CaptureState() =>
        SaveStateService.Capture(
            MachineType,
            _cpu.Registers,
            _memory,
            _ula,
            _ay,
            _tapeLoader.Tape?.Position ?? -1,
            _tapeLoader.Tape?.SourcePath);

    public void RestoreState(SaveStateData state)
    {
        MachineType = state.Machine;
        _frameTStates = MachineType == MachineType.Spectrum128
            ? SpectrumUla.FrameTStates128
            : SpectrumUla.FrameTStates48;
        SaveStateService.Restore(state, _cpu.Registers, _memory, _ula, _ay);
        _tStatesThisFrame = 0;
    }

    private byte ReadMem(ushort addr) => _memory.Read(addr);
    private void WriteMem(ushort addr, byte value) => _memory.Write(addr, value);

    private byte ReadPort(ushort port)
    {
        byte lo = (byte)port;
        byte hi = (byte)(port >> 8);

        if ((lo & 1) == 0)
        {
            // ULA
            byte keys = _keyboard.Read(hi);
            return _ula.ReadFe(keys);
        }

        if (_beta.ClaimsPort(port))
            return _beta.ReadPort(port);

        if (lo == 0x1F && _joystick.Type == JoystickType.Kempston)
            return _joystick.ReadKempston();

        if (MachineType == MachineType.Spectrum128)
        {
            if ((port & 0xC002) == 0xC000)
                return _ay.ReadData();
        }

        // Puertos no conectados: floating bus (Arkanoid, Cobra, Sidewize…).
        // Muestrear en el último T del IN (PartialTStates), no en +1.
        // Arkanoid: tras el primer wait(+doble CALL) FB=FF el resto del frame.
        // Las lecturas de haz @84AE suelen ser antes del lock (margen 120 T).
        if (_arkanoidFbLocked)
            return 0xFF;

        int sampleT = FloatingBusIoSampleOffset is int fixedOff
            ? _tStatesThisFrame + fixedOff
            : _tStatesThisFrame + _cpu.PartialTStates;
        return _ula.ReadFloatingBus(
            _memory.ScreenBank,
            sampleT,
            MachineType == MachineType.Spectrum128);
    }

    /// <summary>
    /// Solo diagnóstico: fuerza offset T del floating-bus (1 = pre-pala, lento; null = PartialTStates).
    /// </summary>
    public static int? FloatingBusIoSampleOffset { get; set; }

    private void WritePort(ushort port, byte value)
    {
        byte lo = (byte)port;

        if ((lo & 1) == 0)
        {
            if (BeeperTrace)
            {
                TraceFeWrites++;
                int site = (_cpu.Registers.PC - 2) & 0xFFFF;
                TraceFeSites.TryGetValue(site, out int n);
                TraceFeSites[site] = n + 1;
            }

            // Flanco EAR en el T real del OUT (no al final de la instrucción).
            // Crítico para PWM ~18 kHz de Arkanoid: el duty cycle salía aplastado.
            int partial = _cpu.PartialTStates;
            int pending = partial - _beeperPartialDone;
            if (pending > 0)
                AdvanceBeeper(pending);
            _beeperPartialDone = partial;

            _ula.WriteFe(value);
            return;
        }

        if (_beta.ClaimsPort(port))
        {
            _beta.WritePort(port, value);
            return;
        }

        if (MachineType == MachineType.Spectrum128)
        {
            if ((port & 0x8002) == 0)
            {
                _memory.Write7Ffd(value);
                return;
            }

            // AY: FFFD select, BFFD data (partial decode)
            if ((port & 0xC002) == 0xC000)
            {
                _ay.SelectRegister(value);
                _aySelectPort = value;
                return;
            }
            if ((port & 0xC002) == 0x8000)
            {
                _ay.WriteData(value);
            }
        }
    }
}
