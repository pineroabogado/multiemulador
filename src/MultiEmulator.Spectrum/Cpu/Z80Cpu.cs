using System;

namespace MultiEmulator.Spectrum.Cpu;

/// <summary>
/// Intérprete de la CPU Z80 apto para ZX Spectrum.
/// Implementa el juego de instrucciones completo (principal, CB, ED, DD, FD, DDCB, FDCB),
/// incluyendo los opcodes indocumentados más habituales (IXH/IXL/IYH/IYL, SLL, copia en DDCB).
/// Las T-states se devuelven por instrucción en <see cref="Step"/> y se acumulan en
/// <see cref="Z80Registers.TStates"/>. No modela contención de memoria (eso es cosa del bus).
/// </summary>
public sealed class Z80Cpu
{
    // Máscaras de flags (equivalentes a Z80Flags, como byte para evitar casts).
    private const int FC = (int)Z80Flags.C;
    private const int FN = (int)Z80Flags.N;
    private const int FPV = (int)Z80Flags.PV;
    private const int FX3 = (int)Z80Flags.X3;
    private const int FH = (int)Z80Flags.H;
    private const int FX5 = (int)Z80Flags.X5;
    private const int FZ = (int)Z80Flags.Z;
    private const int FS = (int)Z80Flags.S;

    // Tablas: S,Z,X3,X5 del valor; y lo mismo + paridad en PV.
    private static readonly byte[] Sz53 = new byte[256];
    private static readonly byte[] Sz53P = new byte[256];
    private static readonly bool[] Parity = new bool[256];

    static Z80Cpu()
    {
        for (int i = 0; i < 256; i++)
        {
            int bits = 0;
            for (int b = 0; b < 8; b++)
                if ((i & (1 << b)) != 0) bits++;
            bool even = (bits & 1) == 0;
            Parity[i] = even;

            int f = (i & (FS | FX3 | FX5)) | (i == 0 ? FZ : 0);
            Sz53[i] = (byte)f;
            Sz53P[i] = (byte)(f | (even ? FPV : 0));
        }
    }

    private readonly Func<ushort, byte> _readMem;
    private readonly Action<ushort, byte> _writeMem;
    private readonly Func<ushort, byte> _readPort;
    private readonly Action<ushort, byte> _writePort;
    private readonly Z80Registers _r = new();

    private int _t;          // T-states de la instrucción en curso
    private int _mode;       // 0 = sin prefijo, 1 = DD (IX), 2 = FD (IY)
    private bool _justEI;    // la instrucción actual es EI

    /// <summary>
    /// Contención de memoria ULA: recibe la dirección y el T-state absoluto
    /// (frame + parcial de la instrucción) y devuelve T-states de espera.
    /// </summary>
    public Func<ushort, int, int>? ContendMemory { get; set; }

    /// <summary>
    /// Contención I/O early/late (Fuse/ZEsarUX). (port, partialT) → delay.
    /// Necesaria para el beeper (OUT FE) y el ritmo de Arkanoid/Sidewize.
    /// </summary>
    public Func<ushort, int, int>? ContendPortEarly { get; set; }
    public Func<ushort, int, int>? ContendPortLate { get; set; }

    public Z80Cpu(
        Func<ushort, byte> readMem,
        Action<ushort, byte> writeMem,
        Func<ushort, byte> readPort,
        Action<ushort, byte> writePort)
    {
        _readMem = readMem ?? throw new ArgumentNullException(nameof(readMem));
        _writeMem = writeMem ?? throw new ArgumentNullException(nameof(writeMem));
        _readPort = readPort ?? throw new ArgumentNullException(nameof(readPort));
        _writePort = writePort ?? throw new ArgumentNullException(nameof(writePort));
        Reset();
    }

    public Z80Registers Registers => _r;

    /// <summary>T-states acumulados de la instrucción en curso (para contención).</summary>
    public int PartialTStates => _t;

    public void Reset()
    {
        _r.Reset();
        _mode = 0;
        _t = 0;
        _justEI = false;
    }

    // ------------------------------------------------------------------
    // Acceso a memoria / pila
    // ------------------------------------------------------------------

    private void ApplyContention(ushort a)
    {
        var contend = ContendMemory;
        if (contend == null) return;
        // T absoluto ≈ T del frame al inicio de Step + parcial de esta instrucción.
        // El host pasa frameT + PartialTStates vía cierre; aquí solo sumamos el delay.
        int delay = contend(a, _t);
        if (delay > 0)
            _t += delay;
    }

    private void ApplyPortEarly(ushort port)
    {
        var c = ContendPortEarly;
        if (c == null) { _t += 1; return; }
        _t += c(port, _t);
    }

    private void ApplyPortLate(ushort port)
    {
        var c = ContendPortLate;
        if (c == null) { _t += 2; return; }
        _t += c(port, _t);
    }

    private byte IoRead(ushort port)
    {
        ApplyPortEarly(port);
        ApplyPortLate(port);
        byte v = _readPort(port);
        _t += 1;
        return v;
    }

    private void IoWrite(ushort port, byte value)
    {
        ApplyPortEarly(port);
        ApplyPortLate(port);
        _writePort(port, value);
        _t += 1;
    }

    private byte Read(ushort a)
    {
        ApplyContention(a);
        return _readMem(a);
    }

    private void Write(ushort a, byte v)
    {
        ApplyContention(a);
        _writeMem(a, v);
    }

    private ushort Read16(ushort a) => (ushort)(Read(a) | (Read((ushort)(a + 1)) << 8));

    private void Write16(ushort a, ushort v)
    {
        Write(a, (byte)v);
        Write((ushort)(a + 1), (byte)(v >> 8));
    }

    private void IncR() => _r.R = (byte)((_r.R & 0x80) | ((_r.R + 1) & 0x7F));

    private byte FetchOp()
    {
        IncR();
        return Read(_r.PC++);
    }

    private byte Fetch8() => Read(_r.PC++);

    private ushort Fetch16()
    {
        byte lo = Read(_r.PC++);
        byte hi = Read(_r.PC++);
        return (ushort)(lo | (hi << 8));
    }

    private void Push(ushort v)
    {
        _r.SP--;
        Write(_r.SP, (byte)(v >> 8));
        _r.SP--;
        Write(_r.SP, (byte)v);
    }

    private ushort Pop()
    {
        byte lo = Read(_r.SP++);
        byte hi = Read(_r.SP++);
        return (ushort)(lo | (hi << 8));
    }

    // ------------------------------------------------------------------
    // Registros (con soporte de prefijo índice)
    // ------------------------------------------------------------------

    /// <summary>HL, IX o IY según el prefijo activo.</summary>
    private ushort HLx
    {
        get => _mode == 0 ? _r.HL : _mode == 1 ? _r.IX : _r.IY;
        set
        {
            if (_mode == 0) _r.HL = value;
            else if (_mode == 1) _r.IX = value;
            else _r.IY = value;
        }
    }

    /// <summary>Lectura plana de registro 8 bits (6 = (HL), sin índice).</summary>
    private byte GetR(int i)
    {
        switch (i)
        {
            case 0: return _r.B;
            case 1: return _r.C;
            case 2: return _r.D;
            case 3: return _r.E;
            case 4: return _r.H;
            case 5: return _r.L;
            case 6: return Read(_r.HL);
            default: return _r.A;
        }
    }

    private void SetR(int i, byte v)
    {
        switch (i)
        {
            case 0: _r.B = v; break;
            case 1: _r.C = v; break;
            case 2: _r.D = v; break;
            case 3: _r.E = v; break;
            case 4: _r.H = v; break;
            case 5: _r.L = v; break;
            case 6: Write(_r.HL, v); break;
            default: _r.A = v; break;
        }
    }

    /// <summary>Registro 8 bits con H/L sustituidos por IXH/IXL/IYH/IYL bajo prefijo. No válido para 6.</summary>
    private byte GetRx(int i)
    {
        if (_mode != 0)
        {
            if (i == 4) return (byte)(HLx >> 8);
            if (i == 5) return (byte)HLx;
        }
        return GetR(i);
    }

    private void SetRx(int i, byte v)
    {
        if (_mode != 0)
        {
            if (i == 4) { HLx = (ushort)((HLx & 0x00FF) | (v << 8)); return; }
            if (i == 5) { HLx = (ushort)((HLx & 0xFF00) | v); return; }
        }
        SetR(i, v);
    }

    private ushort GetRp(int p) => p switch
    {
        0 => _r.BC,
        1 => _r.DE,
        2 => HLx,
        _ => _r.SP,
    };

    private void SetRp(int p, ushort v)
    {
        switch (p)
        {
            case 0: _r.BC = v; break;
            case 1: _r.DE = v; break;
            case 2: HLx = v; break;
            default: _r.SP = v; break;
        }
    }

    private ushort GetRp2(int p) => p switch
    {
        0 => _r.BC,
        1 => _r.DE,
        2 => HLx,
        _ => _r.AF,
    };

    private void SetRp2(int p, ushort v)
    {
        switch (p)
        {
            case 0: _r.BC = v; break;
            case 1: _r.DE = v; break;
            case 2: HLx = v; break;
            default: _r.AF = v; break;
        }
    }

    /// <summary>Dirección del operando (HL) o (IX/IY+d). Con índice consume d y 8 T-states extra.</summary>
    private ushort MemAddr()
    {
        if (_mode == 0) return _r.HL;
        sbyte d = (sbyte)Fetch8();
        _t += 8;
        return (ushort)((_mode == 1 ? _r.IX : _r.IY) + d);
    }

    private bool Cond(int cc)
    {
        int f = _r.F;
        switch (cc)
        {
            case 0: return (f & FZ) == 0;
            case 1: return (f & FZ) != 0;
            case 2: return (f & FC) == 0;
            case 3: return (f & FC) != 0;
            case 4: return (f & FPV) == 0;
            case 5: return (f & FPV) != 0;
            case 6: return (f & FS) == 0;
            default: return (f & FS) != 0;
        }
    }

    // ------------------------------------------------------------------
    // ALU 8 bits
    // ------------------------------------------------------------------

    private void Add8(byte v, bool carry)
    {
        int a = _r.A;
        int res = a + v + (carry ? 1 : 0);
        _r.F = (byte)(Sz53[res & 0xFF]
            | ((a ^ v ^ res) & FH)
            | ((~(a ^ v) & (a ^ res) & 0x80) != 0 ? FPV : 0)
            | ((res >> 8) & 1));
        _r.A = (byte)res;
    }

    private void Sub8(byte v, bool carry)
    {
        int a = _r.A;
        int res = a - v - (carry ? 1 : 0);
        _r.F = (byte)(Sz53[res & 0xFF]
            | FN
            | ((a ^ v ^ res) & FH)
            | (((a ^ v) & (a ^ res) & 0x80) != 0 ? FPV : 0)
            | ((res >> 8) & 1));
        _r.A = (byte)res;
    }

    private void Cp8(byte v)
    {
        int a = _r.A;
        int res = a - v;
        _r.F = (byte)((Sz53[res & 0xFF] & (FS | FZ))
            | (v & (FX3 | FX5))
            | FN
            | ((a ^ v ^ res) & FH)
            | (((a ^ v) & (a ^ res) & 0x80) != 0 ? FPV : 0)
            | ((res >> 8) & 1));
    }

    private void And8(byte v)
    {
        _r.A &= v;
        _r.F = (byte)(Sz53P[_r.A] | FH);
    }

    private void Xor8(byte v)
    {
        _r.A ^= v;
        _r.F = Sz53P[_r.A];
    }

    private void Or8(byte v)
    {
        _r.A |= v;
        _r.F = Sz53P[_r.A];
    }

    private void Alu(int op, byte v)
    {
        switch (op)
        {
            case 0: Add8(v, false); break;
            case 1: Add8(v, (_r.F & FC) != 0); break;
            case 2: Sub8(v, false); break;
            case 3: Sub8(v, (_r.F & FC) != 0); break;
            case 4: And8(v); break;
            case 5: Xor8(v); break;
            case 6: Or8(v); break;
            default: Cp8(v); break;
        }
    }

    private byte Inc8(byte v)
    {
        int res = (v + 1) & 0xFF;
        _r.F = (byte)((_r.F & FC)
            | Sz53[res]
            | ((res & 0x0F) == 0 ? FH : 0)
            | (res == 0x80 ? FPV : 0));
        return (byte)res;
    }

    private byte Dec8(byte v)
    {
        int res = (v - 1) & 0xFF;
        _r.F = (byte)((_r.F & FC)
            | Sz53[res]
            | FN
            | ((v & 0x0F) == 0 ? FH : 0)
            | (v == 0x80 ? FPV : 0));
        return (byte)res;
    }

    // ------------------------------------------------------------------
    // ALU 16 bits
    // ------------------------------------------------------------------

    private ushort Add16(ushort hl, ushort v)
    {
        int res = hl + v;
        _r.F = (byte)((_r.F & (FS | FZ | FPV))
            | ((res >> 8) & (FX3 | FX5))
            | (((hl ^ v ^ res) >> 8) & FH)
            | ((res >> 16) & 1));
        return (ushort)res;
    }

    private ushort Adc16(ushort hl, ushort v)
    {
        int res = hl + v + (_r.F & FC);
        _r.F = (byte)(((res >> 8) & (FS | FX3 | FX5))
            | ((res & 0xFFFF) == 0 ? FZ : 0)
            | (((hl ^ v ^ res) >> 8) & FH)
            | ((~(hl ^ v) & (hl ^ res) & 0x8000) != 0 ? FPV : 0)
            | ((res >> 16) & 1));
        return (ushort)res;
    }

    private ushort Sbc16(ushort hl, ushort v)
    {
        int res = hl - v - (_r.F & FC);
        _r.F = (byte)(((res >> 8) & (FS | FX3 | FX5))
            | ((res & 0xFFFF) == 0 ? FZ : 0)
            | FN
            | (((hl ^ v ^ res) >> 8) & FH)
            | (((hl ^ v) & (hl ^ res) & 0x8000) != 0 ? FPV : 0)
            | ((res >> 16) & 1));
        return (ushort)res;
    }

    // ------------------------------------------------------------------
    // Rotaciones, desplazamientos y BIT
    // ------------------------------------------------------------------

    private byte RotShift(int op, byte v)
    {
        int res, c;
        switch (op)
        {
            case 0: // RLC
                c = v >> 7;
                res = ((v << 1) | c) & 0xFF;
                break;
            case 1: // RRC
                c = v & 1;
                res = (v >> 1) | (c << 7);
                break;
            case 2: // RL
                c = v >> 7;
                res = ((v << 1) | (_r.F & FC)) & 0xFF;
                break;
            case 3: // RR
                c = v & 1;
                res = (v >> 1) | ((_r.F & FC) << 7);
                break;
            case 4: // SLA
                c = v >> 7;
                res = (v << 1) & 0xFF;
                break;
            case 5: // SRA
                c = v & 1;
                res = (v >> 1) | (v & 0x80);
                break;
            case 6: // SLL (indocumentada)
                c = v >> 7;
                res = ((v << 1) | 1) & 0xFF;
                break;
            default: // SRL
                c = v & 1;
                res = v >> 1;
                break;
        }
        _r.F = (byte)(Sz53P[res] | c);
        return (byte)res;
    }

    private void Bit(int n, byte v, byte xy)
    {
        bool zero = (v & (1 << n)) == 0;
        _r.F = (byte)((_r.F & FC)
            | FH
            | (xy & (FX3 | FX5))
            | (zero ? (FZ | FPV) : 0)
            | (n == 7 && !zero ? FS : 0));
    }

    private void Daa()
    {
        int a = _r.A;
        int f = _r.F;
        int corr = 0;
        int cf = f & FC;
        if ((f & FH) != 0 || (a & 0x0F) > 9) corr |= 0x06;
        if (cf != 0 || a > 0x99) { corr |= 0x60; cf = FC; }
        bool n = (f & FN) != 0;
        int hf = n
            ? (((f & FH) != 0 && (a & 0x0F) < 6) ? FH : 0)
            : ((a & 0x0F) > 9 ? FH : 0);
        a = (n ? a - corr : a + corr) & 0xFF;
        _r.A = (byte)a;
        _r.F = (byte)(Sz53P[a] | (f & FN) | hf | cf);
    }

    // ------------------------------------------------------------------
    // Ejecución
    // ------------------------------------------------------------------

    /// <summary>Ejecuta una instrucción (o un ciclo de HALT) y devuelve las T-states consumidas.</summary>
    public int Step()
    {
        var r = _r;
        _t = 0;
        _justEI = false;
        bool pendingEI = r.EIDeferred;

        if (r.Halted)
        {
            IncR();
            _t = 4;
        }
        else
        {
            Execute();
        }

        // EI: las interrupciones se habilitan tras la instrucción siguiente.
        if (pendingEI && r.EIDeferred && !_justEI)
        {
            r.EIDeferred = false;
            r.IFF1 = r.IFF2 = true;
        }

        r.TStates += _t;
        return _t;
    }

    /// <summary>
    /// Interrupción enmascarable. Devuelve las T-states consumidas (0 si no fue aceptada).
    /// <paramref name="dataBus"/> es el valor presente en el bus (0xFF en el Spectrum).
    /// </summary>
    public int Interrupt(byte dataBus = 0xFF)
    {
        var r = _r;
        if (!r.IFF1 || r.EIDeferred) return 0;

        r.Halted = false;
        r.IFF1 = r.IFF2 = false;
        IncR();
        Push(r.PC);

        int t;
        switch (r.IM)
        {
            case 2:
                r.PC = Read16((ushort)((r.I << 8) | dataBus));
                t = 19;
                break;
            case 1:
                r.PC = 0x38;
                t = 13;
                break;
            default: // IM 0: se ejecuta la instrucción del bus (RST); otra cosa se trata como RST 38h
                r.PC = (dataBus & 0xC7) == 0xC7 ? (ushort)(dataBus & 0x38) : (ushort)0x38;
                t = 13;
                break;
        }

        r.TStates += t;
        return t;
    }

    /// <summary>Interrupción no enmascarable. Devuelve las T-states consumidas.</summary>
    public int Nmi()
    {
        var r = _r;
        r.Halted = false;
        r.IFF2 = r.IFF1;
        r.IFF1 = false;
        r.EIDeferred = false;
        IncR();
        Push(r.PC);
        r.PC = 0x66;
        r.TStates += 11;
        return 11;
    }

    private void Execute()
    {
        _mode = 0;
        byte op = FetchOp();

        while (op == 0xDD || op == 0xFD)
        {
            _mode = op == 0xDD ? 1 : 2;
            _t += 4;
            op = FetchOp();
        }

        if (op == 0xED)
        {
            _mode = 0; // los prefijos índice no afectan a ED
            ExecED(FetchOp());
        }
        else if (op == 0xCB)
        {
            if (_mode == 0) ExecCB(FetchOp());
            else ExecIndexedCB();
        }
        else
        {
            ExecMain(op);
        }
    }

    // ------------------------------------------------------------------
    // Juego principal (con DD/FD aplicado vía _mode)
    // ------------------------------------------------------------------

    private void ExecMain(byte op)
    {
        var r = _r;
        int x = op >> 6;
        int y = (op >> 3) & 7;
        int z = op & 7;
        int p = y >> 1;
        bool q = (y & 1) != 0;

        switch (x)
        {
            case 0:
                switch (z)
                {
                    case 0:
                        switch (y)
                        {
                            case 0: _t += 4; break;                       // NOP
                            case 1: r.ExchangeAF(); _t += 4; break;       // EX AF,AF'
                            case 2:                                       // DJNZ d
                            {
                                sbyte d = (sbyte)Fetch8();
                                r.B--;
                                if (r.B != 0) { r.PC = (ushort)(r.PC + d); _t += 13; }
                                else _t += 8;
                                break;
                            }
                            case 3:                                       // JR d
                            {
                                sbyte d = (sbyte)Fetch8();
                                r.PC = (ushort)(r.PC + d);
                                _t += 12;
                                break;
                            }
                            default:                                      // JR cc,d
                            {
                                sbyte d = (sbyte)Fetch8();
                                if (Cond(y - 4)) { r.PC = (ushort)(r.PC + d); _t += 12; }
                                else _t += 7;
                                break;
                            }
                        }
                        break;

                    case 1:
                        if (!q) { SetRp(p, Fetch16()); _t += 10; }        // LD rp,nn
                        else { HLx = Add16(HLx, GetRp(p)); _t += 11; }    // ADD HL,rp
                        break;

                    case 2:
                        switch (y)
                        {
                            case 0: Write(r.BC, r.A); _t += 7; break;
                            case 1: r.A = Read(r.BC); _t += 7; break;
                            case 2: Write(r.DE, r.A); _t += 7; break;
                            case 3: r.A = Read(r.DE); _t += 7; break;
                            case 4: Write16(Fetch16(), HLx); _t += 16; break;
                            case 5: HLx = Read16(Fetch16()); _t += 16; break;
                            case 6: Write(Fetch16(), r.A); _t += 13; break;
                            default: r.A = Read(Fetch16()); _t += 13; break;
                        }
                        break;

                    case 3: // INC/DEC rp
                        SetRp(p, (ushort)(GetRp(p) + (q ? -1 : 1)));
                        _t += 6;
                        break;

                    case 4: // INC r
                        if (y == 6)
                        {
                            ushort a = MemAddr();
                            Write(a, Inc8(Read(a)));
                            _t += 11;
                        }
                        else { SetRx(y, Inc8(GetRx(y))); _t += 4; }
                        break;

                    case 5: // DEC r
                        if (y == 6)
                        {
                            ushort a = MemAddr();
                            Write(a, Dec8(Read(a)));
                            _t += 11;
                        }
                        else { SetRx(y, Dec8(GetRx(y))); _t += 4; }
                        break;

                    case 6: // LD r,n
                        if (y == 6)
                        {
                            if (_mode == 0)
                            {
                                Write(r.HL, Fetch8());
                                _t += 10;
                            }
                            else
                            {
                                sbyte d = (sbyte)Fetch8();
                                ushort a = (ushort)((_mode == 1 ? r.IX : r.IY) + d);
                                Write(a, Fetch8());
                                _t += 15;
                            }
                        }
                        else { SetRx(y, Fetch8()); _t += 7; }
                        break;

                    default: // acumulador y flags
                        switch (y)
                        {
                            case 0: // RLCA
                            {
                                int c = r.A >> 7;
                                r.A = (byte)((r.A << 1) | c);
                                r.F = (byte)((r.F & (FS | FZ | FPV)) | (r.A & (FX3 | FX5)) | c);
                                break;
                            }
                            case 1: // RRCA
                            {
                                int c = r.A & 1;
                                r.A = (byte)((r.A >> 1) | (c << 7));
                                r.F = (byte)((r.F & (FS | FZ | FPV)) | (r.A & (FX3 | FX5)) | c);
                                break;
                            }
                            case 2: // RLA
                            {
                                int c = r.A >> 7;
                                r.A = (byte)((r.A << 1) | (r.F & FC));
                                r.F = (byte)((r.F & (FS | FZ | FPV)) | (r.A & (FX3 | FX5)) | c);
                                break;
                            }
                            case 3: // RRA
                            {
                                int c = r.A & 1;
                                r.A = (byte)((r.A >> 1) | ((r.F & FC) << 7));
                                r.F = (byte)((r.F & (FS | FZ | FPV)) | (r.A & (FX3 | FX5)) | c);
                                break;
                            }
                            case 4: Daa(); break;
                            case 5: // CPL
                                r.A = (byte)~r.A;
                                r.F = (byte)((r.F & (FS | FZ | FPV | FC)) | (r.A & (FX3 | FX5)) | FH | FN);
                                break;
                            case 6: // SCF
                                r.F = (byte)((r.F & (FS | FZ | FPV)) | (r.A & (FX3 | FX5)) | FC);
                                break;
                            default: // CCF
                                r.F = (byte)((r.F & (FS | FZ | FPV))
                                    | (r.A & (FX3 | FX5))
                                    | ((r.F & FC) != 0 ? FH : FC));
                                break;
                        }
                        _t += 4;
                        break;
                }
                break;

            case 1:
                if (op == 0x76) // HALT
                {
                    r.Halted = true;
                    _t += 4;
                }
                else if (y == 6) // LD (HL)/(IX+d),r
                {
                    ushort a = MemAddr();
                    Write(a, GetR(z));
                    _t += 7;
                }
                else if (z == 6) // LD r,(HL)/(IX+d)
                {
                    ushort a = MemAddr();
                    SetR(y, Read(a));
                    _t += 7;
                }
                else
                {
                    SetRx(y, GetRx(z));
                    _t += 4;
                }
                break;

            case 2:
                if (z == 6)
                {
                    Alu(y, Read(MemAddr()));
                    _t += 7;
                }
                else
                {
                    Alu(y, GetRx(z));
                    _t += 4;
                }
                break;

            default: // x == 3
                switch (z)
                {
                    case 0: // RET cc
                        if (Cond(y)) { r.PC = Pop(); _t += 11; }
                        else _t += 5;
                        break;

                    case 1:
                        if (!q)
                        {
                            SetRp2(p, Pop());
                            _t += 10;
                        }
                        else
                        {
                            switch (p)
                            {
                                case 0: r.PC = Pop(); _t += 10; break;           // RET
                                case 1: r.ExchangeRegisterSets(); _t += 4; break;      // EXX
                                case 2: r.PC = HLx; _t += 4; break;              // JP (HL)
                                default: r.SP = HLx; _t += 6; break;             // LD SP,HL
                            }
                        }
                        break;

                    case 2: // JP cc,nn
                    {
                        ushort nn = Fetch16();
                        if (Cond(y)) r.PC = nn;
                        _t += 10;
                        break;
                    }

                    case 3:
                        switch (y)
                        {
                            case 0: r.PC = Fetch16(); _t += 10; break; // JP nn
                            case 2: // OUT (n),A
                            {
                                byte n = Fetch8();
                                // Opcode+n = 7T; I/O = early+late+1 (Fuse ula_contend_port_*).
                                _t += 7;
                                IoWrite((ushort)((r.A << 8) | n), r.A);
                                break;
                            }
                            case 3: // IN A,(n)
                            {
                                byte n = Fetch8();
                                // Opcode+n = 7T; floating bus se muestrea tras early+late.
                                _t += 7;
                                r.A = IoRead((ushort)((r.A << 8) | n));
                                break;
                            }
                            case 4: // EX (SP),HL
                            {
                                ushort v = Read16(r.SP);
                                Write16(r.SP, HLx);
                                HLx = v;
                                _t += 19;
                                break;
                            }
                            case 5: // EX DE,HL
                            {
                                ushort t = r.DE;
                                r.DE = r.HL;
                                r.HL = t;
                                _t += 4;
                                break;
                            }
                            case 6: // DI
                                r.IFF1 = r.IFF2 = false;
                                r.EIDeferred = false;
                                _t += 4;
                                break;
                            case 7: // EI
                                r.EIDeferred = true;
                                _justEI = true;
                                _t += 4;
                                break;
                            default: // 0xCB: tratado en Execute
                                _t += 4;
                                break;
                        }
                        break;

                    case 4: // CALL cc,nn
                    {
                        ushort nn = Fetch16();
                        if (Cond(y)) { Push(r.PC); r.PC = nn; _t += 17; }
                        else _t += 10;
                        break;
                    }

                    case 5:
                        if (!q) { Push(GetRp2(p)); _t += 11; } // PUSH rp2
                        else
                        {
                            // p == 0 -> CALL nn; (DD/ED/FD tratados en Execute)
                            ushort nn = Fetch16();
                            Push(r.PC);
                            r.PC = nn;
                            _t += 17;
                        }
                        break;

                    case 6: // ALU A,n
                        Alu(y, Fetch8());
                        _t += 7;
                        break;

                    default: // RST
                        Push(r.PC);
                        r.PC = (ushort)(y * 8);
                        _t += 11;
                        break;
                }
                break;
        }
    }

    // ------------------------------------------------------------------
    // Prefijo CB
    // ------------------------------------------------------------------

    private void ExecCB(byte op)
    {
        int x = op >> 6;
        int y = (op >> 3) & 7;
        int z = op & 7;

        _t += 4; // fetch del prefijo CB
        if (x == 1)
        {
            if (z == 6)
            {
                byte v = Read(_r.HL);
                Bit(y, v, (byte)(_r.H));
                _t += 8;
            }
            else
            {
                byte v = GetR(z);
                Bit(y, v, v);
                _t += 4;
            }
            return;
        }

        byte val = GetR(z);
        switch (x)
        {
            case 0: val = RotShift(y, val); break;
            case 2: val = (byte)(val & ~(1 << y)); break;
            default: val = (byte)(val | (1 << y)); break;
        }
        SetR(z, val);
        _t += z == 6 ? 11 : 4;
    }

    /// <summary>DD CB d op / FD CB d op. El prefijo DD/FD ya se contabilizó (4 T).</summary>
    private void ExecIndexedCB()
    {
        sbyte d = (sbyte)Fetch8();
        byte op = Fetch8(); // no incrementa R
        ushort a = (ushort)((_mode == 1 ? _r.IX : _r.IY) + d);

        int x = op >> 6;
        int y = (op >> 3) & 7;
        int z = op & 7;

        byte v = Read(a);
        if (x == 1)
        {
            Bit(y, v, (byte)(a >> 8));
            _t += 4 + 12;
            return;
        }

        switch (x)
        {
            case 0: v = RotShift(y, v); break;
            case 2: v = (byte)(v & ~(1 << y)); break;
            default: v = (byte)(v | (1 << y)); break;
        }
        Write(a, v);
        if (z != 6) SetR(z, v); // copia indocumentada en registro
        _t += 4 + 15;
    }

    // ------------------------------------------------------------------
    // Prefijo ED
    // ------------------------------------------------------------------

    private void ExecED(byte op)
    {
        var r = _r;
        int x = op >> 6;
        int y = (op >> 3) & 7;
        int z = op & 7;
        int p = y >> 1;
        bool q = (y & 1) != 0;

        if (x == 1)
        {
            switch (z)
            {
                case 0: // IN r,(C)
                {
                    // ED+op = 8T; I/O Fuse early+late+1 (Arkanoid IN 28FFh / beeper).
                    _t += 8;
                    byte v = IoRead(r.BC);
                    r.F = (byte)((r.F & FC) | Sz53P[v]);
                    if (y != 6) SetR(y, v);
                    break;
                }
                case 1: // OUT (C),r
                    _t += 8;
                    IoWrite(r.BC, y == 6 ? (byte)0 : GetR(y));
                    break;
                case 2: // SBC/ADC HL,rp
                    r.HL = q ? Adc16(r.HL, GetRp(p)) : Sbc16(r.HL, GetRp(p));
                    _t += 15;
                    break;
                case 3: // LD (nn),rp / LD rp,(nn)
                {
                    ushort a = Fetch16();
                    if (!q) Write16(a, GetRp(p));
                    else SetRp(p, Read16(a));
                    _t += 20;
                    break;
                }
                case 4: // NEG
                {
                    byte v = r.A;
                    r.A = 0;
                    Sub8(v, false);
                    _t += 8;
                    break;
                }
                case 5: // RETN / RETI
                    r.IFF1 = r.IFF2;
                    r.PC = Pop();
                    _t += 14;
                    break;
                case 6: // IM
                    switch (y & 3)
                    {
                        case 2: r.IM = 1; break;
                        case 3: r.IM = 2; break;
                        default: r.IM = 0; break;
                    }
                    _t += 8;
                    break;
                default:
                    switch (y)
                    {
                        case 0: r.I = r.A; _t += 9; break; // LD I,A
                        case 1: r.R = r.A; _t += 9; break; // LD R,A
                        case 2: // LD A,I
                            r.A = r.I;
                            r.F = (byte)((r.F & FC) | Sz53[r.A] | (r.IFF2 ? FPV : 0));
                            _t += 9;
                            break;
                        case 3: // LD A,R
                            r.A = r.R;
                            r.F = (byte)((r.F & FC) | Sz53[r.A] | (r.IFF2 ? FPV : 0));
                            _t += 9;
                            break;
                        case 4: // RRD
                        {
                            byte t = Read(r.HL);
                            Write(r.HL, (byte)((r.A << 4) | (t >> 4)));
                            r.A = (byte)((r.A & 0xF0) | (t & 0x0F));
                            r.F = (byte)((r.F & FC) | Sz53P[r.A]);
                            _t += 18;
                            break;
                        }
                        case 5: // RLD
                        {
                            byte t = Read(r.HL);
                            Write(r.HL, (byte)((t << 4) | (r.A & 0x0F)));
                            r.A = (byte)((r.A & 0xF0) | (t >> 4));
                            r.F = (byte)((r.F & FC) | Sz53P[r.A]);
                            _t += 18;
                            break;
                        }
                        default: _t += 8; break; // NOP
                    }
                    break;
            }
        }
        else if (x == 2 && z <= 3 && y >= 4)
        {
            ExecBlock(y, z);
        }
        else
        {
            _t += 8; // NOP (ED indefinido)
        }
    }

    private void IoBlockFlags(byte v, int k)
    {
        _r.F = (byte)(Sz53[_r.B]
            | ((v & 0x80) != 0 ? FN : 0)
            | (k > 255 ? (FH | FC) : 0)
            | (Parity[(k & 7) ^ _r.B] ? FPV : 0));
    }

    private void ExecBlock(int y, int z)
    {
        var r = _r;
        bool inc = (y & 1) == 0;
        bool repeat = y >= 6;
        int delta = inc ? 1 : -1;
        bool again;

        switch (z)
        {
            case 0: // LDI/LDD/LDIR/LDDR
            {
                byte v = Read(r.HL);
                Write(r.DE, v);
                r.HL = (ushort)(r.HL + delta);
                r.DE = (ushort)(r.DE + delta);
                r.BC = (ushort)(r.BC - 1);
                int n = v + r.A;
                r.F = (byte)((r.F & (FS | FZ | FC))
                    | (n & FX3)
                    | ((n << 4) & FX5)
                    | (r.BC != 0 ? FPV : 0));
                again = repeat && r.BC != 0;
                break;
            }
            case 1: // CPI/CPD/CPIR/CPDR
            {
                byte v = Read(r.HL);
                int res = r.A - v;
                int h = (r.A ^ v ^ res) & FH;
                r.HL = (ushort)(r.HL + delta);
                r.BC = (ushort)(r.BC - 1);
                int n = res - (h != 0 ? 1 : 0);
                r.F = (byte)((r.F & FC)
                    | FN
                    | (res & FS)
                    | ((res & 0xFF) == 0 ? FZ : 0)
                    | h
                    | (n & FX3)
                    | ((n << 4) & FX5)
                    | (r.BC != 0 ? FPV : 0));
                again = repeat && r.BC != 0 && (res & 0xFF) != 0;
                break;
            }
            case 2: // INI/IND/INIR/INDR
            {
                byte v = _readPort(r.BC);
                Write(r.HL, v);
                r.B--;
                r.HL = (ushort)(r.HL + delta);
                int k = v + ((r.C + delta) & 0xFF);
                IoBlockFlags(v, k);
                again = repeat && r.B != 0;
                break;
            }
            default: // OUTI/OUTD/OTIR/OTDR
            {
                byte v = Read(r.HL);
                r.B--;
                _writePort(r.BC, v);
                r.HL = (ushort)(r.HL + delta);
                int k = v + r.L;
                IoBlockFlags(v, k);
                again = repeat && r.B != 0;
                break;
            }
        }

        if (again)
        {
            r.PC = (ushort)(r.PC - 2);
            _t += 21;
        }
        else
        {
            _t += 16;
        }
    }
}
