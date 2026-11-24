using MultiEmulator.Spectrum.Cpu;
using MultiEmulator.Spectrum.Memory;

namespace MultiEmulator.Spectrum.Disk;

/// <summary>
/// Carga práctica de discos TR-DOS: autorun del catálogo (boot → RUN) sin VG93 completo.
/// Opcionalmente intercepta USR 15616/15619 para LOAD/RUN desde el TRD montado.
/// </summary>
public sealed class TrDosLoader
{
    public const ushort TrDosEntry0 = 0x3D00;
    public const ushort TrDosEntry3 = 0x3D03; // USR 15619

    // System variables (48K) — mismos offsets que TapeLoader instantáneo
    private const ushort AddrVars = 23627;
    private const ushort AddrProg = 23635;
    private const ushort AddrNxtlin = 23637;
    private const ushort AddrELine = 23641;
    private const ushort AddrKCur = 23643;
    private const ushort AddrWorksp = 23649;
    private const ushort AddrStkbot = 23651;
    private const ushort AddrStktop = 23653;

    private const ushort ProgDefault = 0x5CCB;
    private const ushort SysIy = 0x5C3A;

    private TrdImage? _disk;

    public TrdImage? Disk => _disk;
    public string? LastLoadedName { get; set; }

    public void Insert(TrdImage disk) => _disk = disk;

    public void Eject()
    {
        _disk = null;
        LastLoadedName = null;
    }

    /// <summary>Carga el fichero de autorun en una máquina ya en BASIC listo.</summary>
    public void AutoBootReady(SpectrumMachine machine)
    {
        if (_disk == null)
            throw new InvalidOperationException("No hay disco TRD insertado");

        var entry = _disk.ResolveAutoRunEntry();
        LastLoadedName = entry.Name;
        RunEntry(machine, entry);
    }

    public void RunEntry(SpectrumMachine machine, TrdFileEntry entry)
    {
        if (_disk == null)
            throw new InvalidOperationException("No hay disco TRD insertado");

        byte[] data = _disk.ReadFile(entry);
        LastLoadedName = entry.Name;

        if (entry.Type == TrdFileType.Code)
        {
            LoadCode(machine, data, entry.LoadAddress);
            return;
        }

        // BASIC (y "BASIC" crackeado con USR embebido — típico en releases TR-DOS)
        ushort prog = ReadU16(machine.Memory, AddrProg);
        if (prog < 0x5C00 || prog > 0x8000)
            prog = ProgDefault;

        int logicalLen = entry.LengthBytes > 0
            ? Math.Min(entry.LengthBytes, data.Length)
            : data.Length;
        LoadBasicBlob(machine, data, prog, logicalLen);
        if (TryFindRandomizeUsr(data.AsSpan(0, Math.Min(data.Length, Math.Max(logicalLen, 64))), out ushort usr)
            && usr >= 0x4000)
        {
            JumpTo(machine, usr);
            return;
        }

        // Sin USR usable: ejecutar desde LINE-NEW (como cinta instantánea)
        ushort autoLine = entry.AutoStartLine;
        if (autoLine == 0 || autoLine >= 0x8000)
            autoLine = 0;
        WriteU16(machine.Memory, 23618, autoLine); // NEWPPC
        machine.Memory.Write(23620, 0);             // NSPPC
        var r = machine.Registers;
        r.Halted = false;
        r.IFF1 = true;
        r.IFF2 = true;
        r.EIDeferred = false;
        r.IM = 1;
        r.IY = SysIy;
        r.PC = 0x1B9E; // LINE-NEW
    }

    /// <summary>
    /// HLE: si PC está en la entrada TR-DOS y hay disco, ejecuta el REM: comando del BASIC.
    /// </summary>
    public bool TryTrapEntry(SpectrumMachine machine)
    {
        if (_disk == null)
            return false;

        ushort pc = machine.Registers.PC;
        if (pc is not (TrDosEntry0 or TrDosEntry3 or 0x3D13))
            return false;

        if (!TryFindRemCommand(machine.Memory, out string command))
            return false;

        return ExecuteCommand(machine, command);
    }

    private bool ExecuteCommand(SpectrumMachine machine, string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return false;

        string upper = command.ToUpperInvariant();
        bool isRun = upper.StartsWith("RUN", StringComparison.Ordinal);
        bool isLoad = upper.StartsWith("LOAD", StringComparison.Ordinal);
        if (!isRun && !isLoad)
            return false;

        if (!TryExtractQuotedName(command, out string name))
            return false;

        var entry = _disk!.FindFile(name);
        if (entry == null)
            return false;

        if (isRun || entry.Type == TrdFileType.Basic)
        {
            RunEntry(machine, entry);
            return true;
        }

        ushort addr = entry.LoadAddress;
        if (TryParseCodeAddress(command, out ushort overrideAddr))
            addr = overrideAddr;
        byte[] data = _disk.ReadFile(entry);
        for (int i = 0; i < data.Length; i++)
            machine.Memory.Write((ushort)(addr + i), data[i]);
        // Volver a BASIC tras LOAD CODE
        machine.Registers.PC = 0x1B76;
        machine.Registers.IFF1 = true;
        machine.Registers.IFF2 = true;
        return true;
    }

    private static void LoadBasicBlob(SpectrumMachine machine, byte[] data, ushort prog, int logicalLen)
    {
        var mem = machine.Memory;
        for (int i = 0; i < data.Length; i++)
            mem.Write((ushort)(prog + i), data[i]);

        ushort vars = (ushort)(prog + logicalLen);
        if (logicalLen <= 0 || logicalLen > data.Length || data[logicalLen - 1] != 0x80)
            mem.Write(vars++, 0x80);
        mem.Write(vars, 0x80);

        ushort eLine = (ushort)(vars + 1);
        mem.Write(eLine, 0x0D);
        mem.Write((ushort)(eLine + 1), 0x80);
        ushort work = (ushort)(eLine + 2);

        WriteU16(mem, AddrProg, prog);
        WriteU16(mem, AddrVars, vars);
        WriteU16(mem, AddrELine, eLine);
        WriteU16(mem, AddrKCur, eLine);
        WriteU16(mem, AddrNxtlin, eLine);
        WriteU16(mem, AddrWorksp, work);
        WriteU16(mem, AddrStkbot, work);
        WriteU16(mem, AddrStktop, work);
    }

    private static void LoadCode(SpectrumMachine machine, byte[] data, ushort address)
    {
        var mem = machine.Memory;
        for (int i = 0; i < data.Length; i++)
            mem.Write((ushort)(address + i), data[i]);
        JumpTo(machine, address);
    }

    private static void JumpTo(SpectrumMachine machine, ushort pc)
    {
        var r = machine.Registers;
        // Como tras USR: HL=BC=dirección; no pisar SP (el de BASIC es válido).
        r.PC = pc;
        r.HL = pc;
        r.BC = pc;
        r.IY = SysIy;
        r.I = 0x3F;
        r.IM = 1;
        r.IFF1 = false;
        r.IFF2 = false;
        r.EIDeferred = false;
        r.Halted = false;
    }

    /// <summary>Detecta RANDOMIZE USR &lt;n&gt; y devuelve n (valor binario tras 0x0E).</summary>
    public static bool TryFindRandomizeUsr(ReadOnlySpan<byte> basic, out ushort address)
    {
        address = 0;
        for (int i = 0; i + 7 < basic.Length; i++)
        {
            if (basic[i] != 0xF9 || basic[i + 1] != 0xC0)
                continue;
            for (int j = i + 2; j + 5 < basic.Length && j < i + 24; j++)
            {
                if (basic[j] != 0x0E)
                    continue;
                address = (ushort)(basic[j + 3] | (basic[j + 4] << 8));
                if (address is TrDosEntry0 or TrDosEntry3 or 0x3D13)
                    break;
                return address != 0;
            }
        }

        return false;
    }

    private static bool TryFindRemCommand(SpectrumMemory mem, out string command)
    {
        command = "";
        ushort prog = ReadU16(mem, AddrProg);
        ushort vars = ReadU16(mem, AddrVars);
        if (vars <= prog || vars - prog > 0x8000)
            return false;

        int len = vars - prog;
        var buf = new byte[len];
        for (int i = 0; i < len; i++)
            buf[i] = mem.Read((ushort)(prog + i));

        for (int i = 0; i + 8 < buf.Length; i++)
        {
            if (buf[i] != 0xC0)
                continue;
            int numAt = -1;
            for (int j = i + 1; j + 5 < buf.Length && j < i + 20; j++)
            {
                if (buf[j] != 0x0E) continue;
                ushort v = (ushort)(buf[j + 3] | (buf[j + 4] << 8));
                if (v is 15616 or 15619 or TrDosEntry0 or TrDosEntry3)
                {
                    numAt = j;
                    break;
                }
            }

            if (numAt < 0)
                continue;

            int rem = -1;
            for (int j = numAt + 6; j < buf.Length && j < numAt + 16; j++)
            {
                if (buf[j] == 0xEA)
                {
                    rem = j + 1;
                    break;
                }
            }

            if (rem < 0)
                continue;

            while (rem < buf.Length && (buf[rem] == (byte)':' || buf[rem] == (byte)' '))
                rem++;

            if (rem < buf.Length && (buf[rem] == 0xF7 || buf[rem] == 0xEF))
            {
                if (TrdImage.TryParseRunOrLoadName(buf.AsSpan(rem), out string n))
                {
                    command = (buf[rem] == 0xF7 ? "RUN \"" : "LOAD \"") + n + "\"";
                    return true;
                }
            }

            int end = rem;
            while (end < buf.Length && buf[end] != 0x0D && buf[end] != 0x80)
                end++;
            command = System.Text.Encoding.ASCII.GetString(buf, rem, end - rem).Trim();
            if (command.Length > 0)
                return true;
        }

        return false;
    }

    private static bool TryExtractQuotedName(string command, out string name)
    {
        name = "";
        int a = command.IndexOf('"');
        if (a < 0) return false;
        int b = command.IndexOf('"', a + 1);
        if (b < 0) return false;
        name = command.Substring(a + 1, b - a - 1).Trim();
        return name.Length > 0;
    }

    private static bool TryParseCodeAddress(string command, out ushort addr)
    {
        addr = 0;
        int code = command.ToUpperInvariant().IndexOf("CODE", StringComparison.Ordinal);
        if (code < 0) return false;
        string rest = command[(code + 4)..].Trim();
        if (rest.Length == 0) return false;
        string token = rest.Split(' ', ',')[0];
        return ushort.TryParse(token, out addr);
    }

    private static void WriteU16(SpectrumMemory mem, ushort addr, ushort value)
    {
        mem.Write(addr, (byte)value);
        mem.Write((ushort)(addr + 1), (byte)(value >> 8));
    }

    private static ushort ReadU16(SpectrumMemory mem, ushort addr) =>
        (ushort)(mem.Read(addr) | (mem.Read((ushort)(addr + 1)) << 8));
}
