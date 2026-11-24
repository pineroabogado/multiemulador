using MultiEmulator.Spectrum.Cpu;
using MultiEmulator.Spectrum.Input;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Memory;

namespace MultiEmulator.Spectrum.Tape;

/// <summary>
/// Carga de cinta: trampa ROM LD-BYTES, carga instantánea (flash CODE/USR sin teclear LOAD "")
/// y auto-LOAD "" vía LAST_K solo como fallback.
/// </summary>
public sealed class TapeLoader
{
    private const ushort AddrLastK = 23560;
    private const ushort AddrFlags = 23611;
    private const ushort AddrErrNr = 23610;
    private const ushort AddrNewppc = 23618;
    private const ushort AddrNsppc = 23620;
    private const ushort AddrVars = 23627;
    private const ushort AddrProg = 23635;
    private const ushort AddrNxtlin = 23637;
    private const ushort AddrELine = 23641;
    private const ushort AddrKCur = 23643;
    private const ushort AddrWorksp = 23649;
    private const ushort AddrStkbot = 23651;
    private const ushort AddrStkend = 23653;
    /// <summary>System variable RAMTOP (48K/128K). No confundir con STKEND (23653).</summary>
    private const ushort AddrRamtop = 23732;
    private const ushort SysIy = 0x5C3A;
    private const ushort LineNew = 0x1B9E;

    private readonly SpectrumMemory _memory;
    private readonly Z80Registers _regs;
    private TapeImage? _tape;
    private readonly TapePulsePlayer _pulses = new();
    private readonly TapeTransport _transport;

    private int _autoIndex = -1; // -1 idle, -2 done
    private int _autoTimer;
    private byte[] _autoKeys = Array.Empty<byte>();
    private bool _waitingConsume;

    public TapeLoader(SpectrumMemory memory, Z80Registers regs)
    {
        _memory = memory;
        _regs = regs;
        _transport = new TapeTransport(_pulses);
    }

    public TapeImage? Tape => _tape;
    public TapePulsePlayer Pulses => _pulses;
    public TapeTransport Transport => _transport;
    /// <summary>Si true, Insert puede disparar LAST_K (desactivado por defecto).</summary>
    public bool AutoLoad { get; set; }
    /// <summary>Frames de espera antes de inyectar LOAD "" (fallback, tras editor listo).</summary>
    public int AutoLoadDelayFrames { get; set; } = 10;
    /// <summary>
    /// Equivalente Spectaculator «Fast Loading» (zxtape.dll: Plugins\ZX Tape\Fast Load):
    /// el host acelera CPU+EAR juntos mientras hay pulsos turbo / cargador en RAM.
    /// FlashLoad (bloques 0x10) ya se hace en <see cref="TryInstantFlashStart"/> / trampa ROM.
    /// </summary>
    public bool FastLoad { get; set; } = true;
    /// <summary>Frames emulados por tick de host cuando <see cref="IsFastLoadActive"/>.</summary>
    public int FastLoadWarpFrames { get; set; } = 64;
    public bool EarLevel => _pulses.Level;
    public bool MuteBeeper => _transport.MuteAudio || IsFastLoadActive;
    /// <summary>True si la última apertura usó carga instantánea (sin teclear LOAD "").</summary>
    public bool UsedInstantLoad { get; private set; }

    /// <summary>
    /// True mientras hay pulsos EAR reales (turbo/tono). Solo entonces el host warpea
    /// CPU+cinta juntos (como Spectaculator Fast Loading). No warpea el juego ya cargado
    /// aunque queden bloques en la cinta.
    /// </summary>
    public bool IsFastLoadActive =>
        FastLoad && _tape is { IsPlaying: true } && _pulses.IsActive;

    public void Insert(TapeImage tape, bool startAutoLoad = false)
    {
        UsedInstantLoad = false;
        _tape = tape;
        _tape.Rewind();
        _tape.IsPlaying = true;
        _pulses.Attach(tape);
        _transport.Attach(tape);
        CancelAutoLoad();
        if (startAutoLoad && AutoLoad)
            BeginAutoLoadCommand(AutoLoadDelayFrames);
    }

    public void Eject()
    {
        _tape = null;
        _transport.Attach(null);
        CancelAutoLoad();
        UsedInstantLoad = false;
    }

    public void CancelAutoLoad()
    {
        _autoIndex = -1;
        _autoTimer = 0;
        _waitingConsume = false;
        _autoKeys = Array.Empty<byte>();
        ClearPendingKey();
    }

    /// <summary>Limpia LAST_K / FLAGS para que no quede Break o tecla colgada tras Reset.</summary>
    public void ClearPendingKey()
    {
        byte flags = _memory.Read(AddrFlags);
        _memory.Write(AddrFlags, (byte)(flags & ~0x20));
        _memory.Write(AddrLastK, 0);
    }

    public void AdvancePulses(int tStates) => _pulses.Advance(tStates);

    public void TickTransport(int frameTStates)
    {
        bool inLdBytes = _regs.PC is >= 0x0556 and <= 0x05E2;
        _transport.Tick(frameTStates, allowRomTrapSkip: !inLdBytes);
    }

    public void PlayTape() => _transport.Play();
    public void CycleFastForward() => _transport.CycleFastForward();
    public void CycleRewind() => _transport.CycleRewind();
    public void RewindOneBlock() => _transport.RewindOneBlock();
    public void RewindToStart() => _transport.RewindToStart();

    public void BeginAutoLoadCommand(int? delayFrames = null)
    {
        BeginKeySequence(new byte[] { 0xEF, 0x22, 0x22, 0x0D }, delayFrames ?? AutoLoadDelayFrames);
    }

    /// <summary>Inyecta una secuencia de códigos LAST_K (tokens/ASCII) con retardo inicial.</summary>
    public void BeginKeySequence(byte[] keys, int delayFrames = 40)
    {
        _autoKeys = keys ?? Array.Empty<byte>();
        _autoIndex = _autoKeys.Length == 0 ? -1 : 0;
        _autoTimer = delayFrames < 1 ? 1 : delayFrames;
        _waitingConsume = false;
        ClearPendingKey();
    }

    public void ApplyAutoType(KeyboardMatrix kb)
    {
        _ = kb;
        if (_autoIndex < 0 || _autoIndex >= _autoKeys.Length)
            return;

        if (_autoTimer > 0)
        {
            _autoTimer--;
            return;
        }

        byte flags = _memory.Read(AddrFlags);
        bool keyPending = (flags & 0x20) != 0;

        if (_waitingConsume)
        {
            if (!keyPending)
            {
                _waitingConsume = false;
                _autoIndex++;
                _autoTimer = 8;
                if (_autoIndex >= _autoKeys.Length)
                    _autoIndex = -2;
            }
            return;
        }

        if (keyPending)
            return;

        _memory.Write(AddrLastK, _autoKeys[_autoIndex]);
        _memory.Write(AddrFlags, (byte)(flags | 0x20));
        _waitingConsume = true;
        _autoTimer = 2;
    }

    /// <summary>
    /// True si la cinta tiene al menos un Program ROM-trap inicial (candidato a flash).
    /// </summary>
    public bool CanInstantFlashStart()
    {
        if (_tape == null || _tape.Blocks.Count < 2)
            return false;
        return TryFindInitialProgram(0, out _, out _, out _);
    }

    /// <summary>
    /// Carga instantánea sin teclear LOAD "":
    /// flashea el Program inicial y los bloques CODE/SCREEN resolubles (cabecera o
    /// direcciones parseadas del BASIC), salta a USR si existe, o a LINE-NEW.
    /// El resto (turbo / huérfanos sin dirección) queda en la cinta para EAR/trampa.
    /// </summary>
    public bool TryInstantFlashStart()
    {
        CancelAutoLoad();
        UsedInstantLoad = false;
        if (_tape == null || _tape.Blocks.Count < 2)
            return false;

        ushort prog = ReadU16(AddrProg);
        ushort ramtop = ReadU16(AddrRamtop);
        if (prog < 0x5C00 || ramtop < 0x5D00)
            return false;

        _tape.Rewind();
        _pulses.Seek(0);

        if (!TryFindInitialProgram(0, out int headerIndex, out var header, out var data))
            return false;

        // POKE/DATA/READ antes del USR (Flying Shark BioToxin TAP): HLE del
        // loader BASIC (LOAD CODE + DATA→POKE + USR). LINE-NEW puro falla en
        // este entorno; la simulación evita quedarse en Searching.
        if (BasicRequiresInterpreterBeforeUsr(data.Data))
            return TryInstantBasicPrepThenUsr(headerIndex, header, data);

        var codeAddrs = new Queue<ushort>();
        ushort? usr = null;
        ParseBasicLoaderCommands(data.Data, codeAddrs, ref usr);
        if (!usr.HasValue)
        {
            // Buscar USR en Programs ROM-trap siguientes (p.ej. loaders de 2 etapas).
            for (int i = headerIndex + 2; i + 1 < _tape.Blocks.Count; i++)
            {
                var h = _tape.Blocks[i];
                var d = _tape.Blocks[i + 1];
                if (!h.IsRomTrapCandidate || !d.IsRomTrapCandidate)
                    break;
                if (h.Flag == 0x00 && h.Data.Length >= 19 && h.Data[1] == 0 && d.Flag == 0xFF)
                {
                    if (BasicRequiresInterpreterBeforeUsr(d.Data))
                        return TryInstantBasicPrepThenUsr(i, h, d);
                    ParseBasicLoaderCommands(d.Data, codeAddrs, ref usr);
                    i++;
                    if (usr.HasValue) break;
                    continue;
                }
                break;
            }
        }
        if (!usr.HasValue)
            return false;

        if (!FlashProgramIntoBasic(header, data, prog))
            return false;

        // Re-parse tras decidir: cola CODE limpia para el flash real.
        codeAddrs.Clear();
        ushort? usr2 = null;
        ParseBasicLoaderCommands(data.Data, codeAddrs, ref usr2);
        usr = usr2 ?? usr;

        int pos = headerIndex + 2;
        while (pos < _tape.Blocks.Count)
        {
            var block = _tape.Blocks[pos];
            if (!block.IsRomTrapCandidate)
                break;

            if (block.Flag == 0x00 && block.Data.Length >= 19 && pos + 1 < _tape.Blocks.Count)
            {
                var dataBlock = _tape.Blocks[pos + 1];
                if (!dataBlock.IsRomTrapCandidate || dataBlock.Flag != 0xFF)
                    break;

                byte type = block.Data[1];
                ushort headerAddr = (ushort)(block.Data[14] | (block.Data[15] << 8));
                int dataLen = block.Data[12] | (block.Data[13] << 8);

                if (type == 0)
                {
                    if (!FlashProgramIntoBasic(block, dataBlock, prog))
                        break;
                    ParseBasicLoaderCommands(dataBlock.Data, codeAddrs, ref usr);
                    pos += 2;
                    continue;
                }

                if (type is 1 or 2)
                    break;

                ushort dest = headerAddr;
                if (dest == 0 && codeAddrs.Count > 0)
                    dest = codeAddrs.Dequeue();
                else if (codeAddrs.Count > 0 && dest != 0)
                    codeAddrs.TryDequeue(out _);

                if (dest == 0 && dataLen == 6912)
                    dest = 0x4000;

                if (dest == 0)
                    break;

                FlashDataPayload(dataBlock, dest);
                pos += 2;
                continue;
            }

            if (block.Flag == 0xFF && block.Data.Length >= 2)
            {
                if (codeAddrs.Count == 0)
                    break;
                ushort dest = codeAddrs.Dequeue();
                FlashDataPayload(block, dest);
                pos++;
                continue;
            }

            break;
        }

        if (!usr.HasValue)
            return false;

        ApplyClearFromBasic(data.Data);

        _tape.Position = pos;
        _pulses.Seek(pos);
        // Solo seguir en play si quedan bloques (turbo EAR o ROM-trap para LD-BYTES).
        // Arkanoid FlashLoad agota el TAP → IsPlaying false → sin warp en partida.
        _tape.IsPlaying = pos < _tape.Blocks.Count;

        _memory.Write(AddrErrNr, 0xFF);
        // Paridad con RANDOMIZE USR desde BASIC: IFF activas (política central).
        LoadPolicies.EnsureInterruptsAfterUsr(_regs);
        if (_regs.SP < 0x4000)
            _regs.SP = 0xFF4A;
        _regs.PC = usr.Value;
        UsedInstantLoad = true;
        LoadPolicies.EnsureFastLoadForTurbo(this);
        return true;
    }

    /// <summary>Quedan bloques turbo/tono/pulsos (no ROM-trap) por delante del cabezal.</summary>
    public bool HasPendingNonTrapEarBlocks()
    {
        if (_tape == null) return false;
        for (int i = Math.Max(0, _tape.Position); i < _tape.Blocks.Count; i++)
        {
            if (!_tape.Blocks[i].IsRomTrapCandidate)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Aplica CLEAR n del BASIC (RAMTOP) antes del USR — p.ej. 1942 CLEAR 24999.
    /// </summary>
    private void ApplyClearFromBasic(byte[] block)
    {
        if (!TryParseClearAddress(block, out ushort clearAddr))
            return;
        WriteU16(AddrRamtop, clearAddr);
        if (_regs.SP > clearAddr)
            _regs.SP = clearAddr;
    }

    private static bool TryParseClearAddress(byte[] block, out ushort clearAddr)
    {
        clearAddr = 0;
        int i = 1;
        int end = block.Length - 1;
        while (i + 4 < end)
        {
            int lineLen = block[i + 2] | (block[i + 3] << 8);
            i += 4;
            int lineEnd = Math.Min(end, i + lineLen);
            while (i < lineEnd)
            {
                byte b = block[i++];
                if (b == 0x0E) { i += 5; continue; }
                if (b == 0x0D) break;
                if (b != 0xFD) // CLEAR
                    continue;
                if (TryReadBasicNumber(block, ref i, lineEnd, out int n) && n is > 0x5D00 and <= 65535)
                {
                    clearAddr = (ushort)n;
                    return true;
                }
            }
            i = Math.Max(i, lineEnd);
        }
        return false;
    }

    private static void ParseBasicLoaderCommands(byte[] block, Queue<ushort> codeAddrs, ref ushort? usr)
    {
        // block: flag + payload + checksum
        int i = 1;
        int end = block.Length - 1;
        while (i + 4 < end)
        {
            int lineLen = block[i + 2] | (block[i + 3] << 8);
            i += 4;
            int lineEnd = Math.Min(end, i + lineLen);
            while (i < lineEnd)
            {
                byte b = block[i++];
                if (b == 0x0E) { i += 5; continue; }
                if (b == 0x0D) break;

                // LOAD "" …  / LOAD "name" …
                if (b == 0xEF && i < lineEnd && block[i] == 0x22)
                {
                    i++;
                    while (i < lineEnd && block[i] != 0x22) i++;
                    if (i < lineEnd && block[i] == 0x22) i++;
                    if (i < lineEnd && block[i] == 0xAF) // CODE
                    {
                        i++;
                        if (TryReadBasicNumber(block, ref i, lineEnd, out int addr) && addr is > 0 and <= 65535)
                            codeAddrs.Enqueue((ushort)addr);
                    }
                    else if (i < lineEnd && block[i] == 0xAA) // SCREEN$
                    {
                        i++;
                        codeAddrs.Enqueue(0x4000);
                    }
                }

                // RANDOMIZE USR n  /  USR n  (también VAL "n")
                if (b == 0xF9 && i < lineEnd && block[i] == 0xC0)
                {
                    i++;
                    if (TryReadBasicNumber(block, ref i, lineEnd, out int addr) && addr is > 0 and <= 65535)
                        usr = (ushort)addr;
                }
                else if (b == 0xC0)
                {
                    if (TryReadBasicNumber(block, ref i, lineEnd, out int addr) && addr is > 0 and <= 65535)
                        usr = (ushort)addr;
                }
            }
        }
    }

    /// <summary>
    /// HLE BioToxin / loaders con POKE+DATA+READ: flashea bloques CODE del BASIC,
    /// aplica DATA como bytes en RAM y POKEs literales, salta a USR.
    /// El resto de la cinta (turbo / huérfanos) queda para EAR.
    /// </summary>
    private bool TryInstantBasicPrepThenUsr(int headerIndex, TapeBlock header, TapeBlock data)
    {
        _ = header;
        var codeAddrs = new Queue<ushort>();
        var pokePairs = new List<(ushort Addr, byte Value)>();
        var dataBytes = new List<int>();
        ushort? dataBase = null;
        ushort? usr = null;

        ParseBasicPrepCommands(data.Data, codeAddrs, pokePairs, dataBytes, ref dataBase, ref usr);
        if (!usr.HasValue)
            return false;

        int pos = headerIndex + 2;
        // Flashear pares cabecera+datos CODE (y SCREEN$) que el BASIC pediría con LOAD.
        // Si no hay direcciones en el BASIC (LOAD "" CODE sin addr / LOAD a$+…),
        // usar la dirección de la cabecera TAP (1942 Elite, etc.).
        bool useHeadersOnly = codeAddrs.Count == 0;
        while (_tape != null && pos + 1 < _tape.Blocks.Count && (useHeadersOnly || codeAddrs.Count > 0))
        {
            var h = _tape.Blocks[pos];
            var d = _tape.Blocks[pos + 1];
            if (!h.IsRomTrapCandidate || !d.IsRomTrapCandidate)
                break;
            if (h.Flag != 0x00 || h.Data.Length < 19 || d.Flag != 0xFF)
                break;

            byte type = h.Data[1];
            if (type == 0)
                break; // otro Program: dejar al EAR/trampa
            if (type is 1 or 2)
                break;

            ushort headerAddr = (ushort)(h.Data[14] | (h.Data[15] << 8));
            ushort dest = headerAddr;
            if (dest == 0 && codeAddrs.Count > 0)
                dest = codeAddrs.Peek();
            if (codeAddrs.Count > 0)
                codeAddrs.TryDequeue(out _);
            if (dest == 0 && (h.Data[12] | (h.Data[13] << 8)) == 6912)
                dest = 0x4000;
            if (dest == 0)
                break;

            FlashDataPayload(d, dest);
            pos += 2;
        }

        // DATA … → POKE l,a  (hasta centinela 999, típico BioToxin)
        if (dataBase is ushort baseAddr && dataBytes.Count > 0)
        {
            ushort a = baseAddr;
            foreach (int v in dataBytes)
            {
                if (v == 999)
                    break;
                if (v is < 0 or > 255)
                    continue;
                _memory.Write(a, (byte)v);
                a++;
            }
        }

        foreach (var (addr, value) in pokePairs)
            _memory.Write(addr, value);

        ApplyClearFromBasic(data.Data);

        if (_tape != null)
        {
            _tape.Position = pos;
            _pulses.Seek(pos);
            _tape.IsPlaying = pos < _tape.Blocks.Count;
        }

        _memory.Write(AddrErrNr, 0xFF);
        LoadPolicies.EnsureInterruptsAfterUsr(_regs);
        _regs.IY = SysIy;
        if (_regs.SP < 0x4000)
            _regs.SP = 0xFF4A;
        _regs.PC = usr.Value;
        UsedInstantLoad = true;
        LoadPolicies.EnsureFastLoadForTurbo(this);
        return true;
    }

    /// <summary>
    /// Extrae LOAD CODE, POKE addr,val, DATA n,…, LET l=base y USR del BASIC ofuscado.
    /// </summary>
    private static void ParseBasicPrepCommands(
        byte[] block,
        Queue<ushort> codeAddrs,
        List<(ushort Addr, byte Value)> pokePairs,
        List<int> dataBytes,
        ref ushort? dataBase,
        ref ushort? usr)
    {
        int i = 1;
        int end = block.Length - 1;
        while (i + 4 < end)
        {
            int lineLen = block[i + 2] | (block[i + 3] << 8);
            i += 4;
            int lineEnd = Math.Min(end, i + lineLen);
            while (i < lineEnd)
            {
                byte b = block[i++];
                if (b == 0x0E) { i += 5; continue; }
                if (b == 0x0D) break;

                // LOAD ""… / LOAD "name"… CODE [addr]
                if (b == 0xEF && i < lineEnd && block[i] == 0x22)
                {
                    i++;
                    while (i < lineEnd && block[i] != 0x22) i++;
                    if (i < lineEnd && block[i] == 0x22) i++;
                    if (i < lineEnd && block[i] == 0xAF) // CODE
                    {
                        i++;
                        if (TryReadBasicNumber(block, ref i, lineEnd, out int addr) &&
                            addr is > 0 and <= 65535)
                            codeAddrs.Enqueue((ushort)addr);
                    }
                    else if (i < lineEnd && block[i] == 0xAA) // SCREEN$
                    {
                        i++;
                        codeAddrs.Enqueue(0x4000);
                    }
                    continue;
                }

                // POKE addr, val
                if (b == 0xF4)
                {
                    if (TryReadBasicNumber(block, ref i, lineEnd, out int addr) &&
                        addr is >= 0 and <= 65535)
                    {
                        while (i < lineEnd && block[i] == 0x20) i++;
                        if (i < lineEnd && block[i] == (byte)',')
                        {
                            i++;
                            if (TryReadBasicNumber(block, ref i, lineEnd, out int val) &&
                                val is >= 0 and <= 255)
                                pokePairs.Add(((ushort)addr, (byte)val));
                        }
                    }
                    continue;
                }

                // DATA n, n, …
                if (b == 0xE4)
                {
                    while (i < lineEnd)
                    {
                        while (i < lineEnd && (block[i] == 0x20 || block[i] == (byte)','))
                            i++;
                        if (i >= lineEnd || block[i] == 0x0D)
                            break;
                        if (!TryReadBasicNumber(block, ref i, lineEnd, out int n))
                            break;
                        dataBytes.Add(n);
                    }
                    continue;
                }

                // LET l = n  (base del bucle READ/POKE) — token LET=0xF1, o asignación tras letra
                if (b == 0xF1)
                {
                    // LET var = expr — saltar nombre hasta '='
                    while (i < lineEnd && block[i] != (byte)'=' && block[i] != 0x0D)
                        i++;
                    if (i < lineEnd && block[i] == (byte)'=')
                    {
                        i++;
                        if (TryReadBasicNumber(block, ref i, lineEnd, out int baseAddr) &&
                            baseAddr is > 0 and <= 65535)
                            dataBase ??= (ushort)baseAddr;
                    }
                    continue;
                }

                // Asignación corta «l=VAL"32768"» sin LET (BioToxin tras token 0xF1 ya cubierto;
                // también «l=» ASCII si aparece).
                if ((b is >= (byte)'a' and <= (byte)'z') || (b is >= (byte)'A' and <= (byte)'Z'))
                {
                    int save = i;
                    while (i < lineEnd && block[i] != (byte)'=' && block[i] > 0x20 && block[i] != 0x0D)
                        i++;
                    if (i < lineEnd && block[i] == (byte)'=')
                    {
                        i++;
                        if (TryReadBasicNumber(block, ref i, lineEnd, out int baseAddr) &&
                            baseAddr is >= 0x4000 and <= 65535)
                            dataBase ??= (ushort)baseAddr;
                    }
                    else
                        i = save;
                }

                // RANDOMIZE USR / USR
                if (b == 0xF9 && i < lineEnd && block[i] == 0xC0)
                {
                    i++;
                    if (TryReadBasicNumber(block, ref i, lineEnd, out int addr) &&
                        addr is > 0 and <= 65535)
                        usr = (ushort)addr;
                }
                else if (b == 0xC0)
                {
                    if (TryReadBasicNumber(block, ref i, lineEnd, out int addr) &&
                        addr is > 0 and <= 65535)
                        usr = (ushort)addr;
                }
            }
            i = Math.Max(i, lineEnd);
        }
    }

    /// <summary>
    /// True si el BASIC hace POKE/DATA/READ <b>antes</b> del primer USR
    /// (p.ej. Flying Shark BioToxin construye código en 32768).
    /// POKEs de trainer/menú <b>después</b> del USR (1942 Elite) no cuentan:
    /// el flash por cabeceras CODE/SCREEN$ + salto a USR basta.
    /// </summary>
    public static bool BasicRequiresInterpreterBeforeUsr(byte[] block)
    {
        int i = 1;
        int end = block.Length - 1;
        while (i + 4 < end)
        {
            int lineLen = block[i + 2] | (block[i + 3] << 8);
            i += 4;
            int lineEnd = Math.Min(end, i + lineLen);
            while (i < lineEnd)
            {
                byte b = block[i++];
                if (b == 0x0E) { i += 5; continue; }
                if (b == 0x0D) break;
                // Primer USR: lo posterior (trainer POKE) no obliga a HLE.
                if (b == 0xF9 && i < lineEnd && block[i] == 0xC0)
                    return false;
                if (b == 0xC0)
                    return false;
                // POKE, DATA, READ antes del USR.
                if (b is 0xF4 or 0xE4 or 0xE3)
                    return true;
            }
            i = Math.Max(i, lineEnd);
        }
        return false;
    }

    /// <summary>True si el BASIC contiene token USR (0xC0) o RANDOMIZE USR.</summary>
    public static bool BasicContainsUsr(byte[] block)
    {
        int i = 1;
        int end = block.Length - 1;
        while (i + 4 < end)
        {
            int lineLen = block[i + 2] | (block[i + 3] << 8);
            i += 4;
            int lineEnd = Math.Min(end, i + lineLen);
            while (i < lineEnd)
            {
                byte b = block[i++];
                if (b == 0x0E) { i += 5; continue; }
                if (b == 0x0D) break;
                if (b == 0xC0)
                    return true;
                if (b == 0xF9 && i < lineEnd && block[i] == 0xC0)
                    return true;
            }
            i = Math.Max(i, lineEnd);
        }
        return false;
    }

    private static bool TryReadBasicNumber(byte[] block, ref int i, int lineEnd, out int value)
    {
        value = 0;
        while (i < lineEnd && block[i] == 0x20)
            i++;

        // VAL "digits" (TAP de BioToxin: USR VAL "52480")
        if (i < lineEnd && block[i] == 0xB0)
        {
            i++;
            while (i < lineEnd && block[i] == 0x20) i++;
            if (i >= lineEnd || block[i] != 0x22)
                return false;
            i++;
            int start = i;
            while (i < lineEnd && block[i] is >= (byte)'0' and <= (byte)'9')
                i++;
            if (i == start)
                return false;
            int n = 0;
            for (int k = start; k < i; k++)
                n = n * 10 + (block[k] - '0');
            if (i < lineEnd && block[i] == 0x22) i++;
            value = n;
            return true;
        }

        int digitStart = i;
        while (i < lineEnd && block[i] is >= (byte)'0' and <= (byte)'9')
            i++;
        if (i == digitStart)
            return false;
        int num = 0;
        for (int k = digitStart; k < i; k++)
            num = num * 10 + (block[k] - '0');
        if (i < lineEnd && block[i] == 0x0E)
            i += 6; // marcador FP
        value = num;
        return true;
    }

    private bool TryFindInitialProgram(int start, out int headerIndex, out TapeBlock header, out TapeBlock data)
    {
        headerIndex = -1;
        header = null!;
        data = null!;
        if (_tape == null) return false;

        for (int i = start; i + 1 < _tape.Blocks.Count; i++)
        {
            var h = _tape.Blocks[i];
            var d = _tape.Blocks[i + 1];
            if (!h.IsRomTrapCandidate)
                return false;
            if (!d.IsRomTrapCandidate)
                continue;
            if (h.Flag != 0x00 || h.Data.Length < 19 || h.Data[1] != 0)
                continue;
            if (d.Flag != 0xFF || d.Data.Length < 2)
                continue;
            headerIndex = i;
            header = h;
            data = d;
            return true;
        }
        return false;
    }

    private bool FlashProgramIntoBasic(TapeBlock header, TapeBlock data, ushort prog)
    {
        int dataLen = header.Data[12] | (header.Data[13] << 8);
        int progLen = header.Data[16] | (header.Data[17] << 8);
        int payloadAvail = Math.Max(0, data.Data.Length - 2);
        int payload = Math.Min(dataLen, payloadAvail);
        if (payload <= 0)
            return false;
        if (progLen <= 0 || progLen > payload)
            progLen = payload;

        for (int i = 0; i < payload; i++)
            _memory.Write((ushort)(prog + i), data.Data[1 + i]);

        ushort vars = (ushort)(prog + progLen);
        ushort eLine = (ushort)(prog + payload);
        WriteU16(AddrVars, vars);
        WriteU16(AddrELine, eLine);
        WriteU16(AddrKCur, eLine);
        WriteU16(AddrNxtlin, eLine);
        _memory.Write(eLine, 0x0D);
        _memory.Write((ushort)(eLine + 1), 0x80);
        ushort work = (ushort)(eLine + 2);
        WriteU16(AddrWorksp, work);
        WriteU16(AddrStkbot, work);
        WriteU16(AddrStkend, work);
        return true;
    }

    private void FlashDataPayload(TapeBlock data, ushort dest)
    {
        int payload = Math.Max(0, data.Data.Length - 2);
        for (int i = 0; i < payload; i++)
            _memory.Write((ushort)(dest + i), data.Data[1 + i]);
    }

    public bool TryTrapLoad()
    {
        if (_tape == null || !_tape.IsPlaying)
            return false;

        if (_regs.PC is not (0x0556 or 0x056C))
            return false;

        bool doLoad = (_regs.F & 0x01) != 0;
        byte expectedFlag = _regs.A;
        ushort dest = _regs.IX;
        ushort length = _regs.DE;

        if (_tape.Position >= _tape.Blocks.Count)
        {
            if (HasPulseBlocksRemaining())
                return false;
            _tape.IsPlaying = false;
            _regs.F = (byte)(_regs.F & ~0x01);
            _regs.Halted = false;
            _regs.PC = 0x05E2;
            return true;
        }

        var peek = _tape.Blocks[_tape.Position];
        if (!peek.IsRomTrapCandidate)
            return false;

        var block = _tape.NextBlock()!;
        if (block.Data.Length < 2 || block.Flag != expectedFlag)
        {
            _regs.F = (byte)(_regs.F & ~0x01);
            _regs.Halted = false;
            _regs.PC = 0x05E2;
            return true;
        }

        int payloadAvail = Math.Max(0, block.Data.Length - 2);
        int payload = Math.Min(length, payloadAvail);

        if (doLoad)
        {
            for (int i = 0; i < payload; i++)
                _memory.Write((ushort)(dest + i), block.Data[1 + i]);
        }

        byte parity = block.Flag;
        for (int i = 1; i < block.Data.Length; i++)
            parity ^= block.Data[i];
        _regs.H = parity;
        _regs.L = parity;
        _regs.IX = (ushort)(dest + payload);
        _regs.DE = (ushort)(length - payload);
        _regs.A = parity;
        _regs.F = (byte)(_regs.F | 0x01);
        _regs.Halted = false;
        _regs.PC = 0x05E2;
        return true;
    }

    private bool HasPulseBlocksRemaining()
    {
        if (_tape == null) return false;
        for (int i = _tape.Position; i < _tape.Blocks.Count; i++)
        {
            if (!_tape.Blocks[i].IsRomTrapCandidate)
                return true;
        }
        return false;
    }

    /// <summary>Quedan bloques que emiten EAR (turbo/tono/pulsos o TAP vía cargador RAM).</summary>
    public bool HasPendingEarBlocks()
    {
        if (_tape == null) return false;
        for (int i = Math.Max(0, _tape.Position); i < _tape.Blocks.Count; i++)
        {
            var b = _tape.Blocks[i];
            if (!b.IsRomTrapCandidate)
                return true;
            if (_pulses.PlayStandardBlocksViaEar)
                return true;
        }
        return false;
    }

    /// <summary>True si la cinta insertada contiene bloques turbo/tono/pulsos (TZX 0x11–0x14).</summary>
    public bool TapeHasTurboBlocks() =>
        _tape != null && LoadMediaInspect.TapeHasTurboBlocks(_tape);

    /// <summary>
    /// Ejecuta frames con warp Fast Load hasta que el EAR turbo termina (o tope).
    /// Devuelve ticks de host usados. Semántica alineada con EmulatorHost.
    /// </summary>
    public int RunFastLoadBurst(Action runFrame, int maxHostTicks = 400, Func<bool>? until = null)
    {
        if (runFrame == null) throw new ArgumentNullException(nameof(runFrame));
        int ticks = 0;
        int idleTicks = 0;
        for (; ticks < maxHostTicks; ticks++)
        {
            int n = IsFastLoadActive ? Math.Max(1, FastLoadWarpFrames) : 1;
            for (int i = 0; i < n; i++)
                runFrame();
            if (until != null && until())
                break;
            if (IsFastLoadActive)
            {
                idleTicks = 0;
                continue;
            }
            // Pausas cortas entre bloques turbo: seguir un poco a ×1.
            if (HasPendingEarBlocks() && _regs.PC >= 0x5D00 && idleTicks < 8)
            {
                idleTicks++;
                continue;
            }
            break;
        }
        return ticks;
    }

    private ushort ReadU16(ushort addr) =>
        (ushort)(_memory.Read(addr) | (_memory.Read((ushort)(addr + 1)) << 8));

    private void WriteU16(ushort addr, ushort value)
    {
        _memory.Write(addr, (byte)(value & 0xFF));
        _memory.Write((ushort)(addr + 1), (byte)(value >> 8));
    }
}
