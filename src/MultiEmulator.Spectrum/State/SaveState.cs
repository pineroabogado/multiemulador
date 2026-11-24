using System.Text.Json;
using MultiEmulator.Spectrum.Audio;
using MultiEmulator.Spectrum.Cpu;
using MultiEmulator.Spectrum.Memory;
using MultiEmulator.Spectrum.Ula;

namespace MultiEmulator.Spectrum.State;

public sealed class SaveStateData
{
    public MachineType Machine { get; set; }
    public byte[] Registers { get; set; } = Array.Empty<byte>();
    public byte[][] RamBanks { get; set; } = Array.Empty<byte[]>();
    public byte[]? Ram48 { get; set; }
    public byte Last7Ffd { get; set; }
    public bool PagingDisabled { get; set; }
    public byte Border { get; set; }
    public bool EarBit { get; set; }
    public byte[] AyRegs { get; set; } = Array.Empty<byte>();
    public int TapePosition { get; set; } = -1;
    public string? TapeSource { get; set; }
}

public static class SaveStateService
{
    public static SaveStateData Capture(
        MachineType machine,
        Z80Registers r,
        SpectrumMemory mem,
        SpectrumUla ula,
        Ay8912 ay,
        int tapePos,
        string? tapeSource)
    {
        var banks = new byte[8][];
        for (int i = 0; i < 8; i++)
        {
            banks[i] = new byte[0x4000];
            Buffer.BlockCopy(mem.RamBank(i), 0, banks[i], 0, 0x4000);
        }
        var ram48 = new byte[0xC000];
        Buffer.BlockCopy(mem.Ram48, 0, ram48, 0, 0xC000);

        var regs = new byte[32];
        regs[0] = r.A; regs[1] = r.F;
        regs[2] = r.B; regs[3] = r.C;
        regs[4] = r.D; regs[5] = r.E;
        regs[6] = r.H; regs[7] = r.L;
        regs[8] = r.A2; regs[9] = r.F2;
        regs[10] = r.B2; regs[11] = r.C2;
        regs[12] = r.D2; regs[13] = r.E2;
        regs[14] = r.H2; regs[15] = r.L2;
        regs[16] = r.I; regs[17] = r.R;
        BitConverter.TryWriteBytes(regs.AsSpan(18), r.IX);
        BitConverter.TryWriteBytes(regs.AsSpan(20), r.IY);
        BitConverter.TryWriteBytes(regs.AsSpan(22), r.SP);
        BitConverter.TryWriteBytes(regs.AsSpan(24), r.PC);
        regs[26] = (byte)(r.IFF1 ? 1 : 0);
        regs[27] = (byte)(r.IFF2 ? 1 : 0);
        regs[28] = r.IM;
        regs[29] = (byte)(r.Halted ? 1 : 0);

        var ayCopy = new byte[16];
        Buffer.BlockCopy(ay.Registers, 0, ayCopy, 0, 16);

        return new SaveStateData
        {
            Machine = machine,
            Registers = regs,
            RamBanks = banks,
            Ram48 = ram48,
            Last7Ffd = mem.Last7Ffd,
            PagingDisabled = mem.PagingDisabled,
            Border = ula.Border,
            EarBit = ula.EarBit,
            AyRegs = ayCopy,
            TapePosition = tapePos,
            TapeSource = tapeSource
        };
    }

    public static void Restore(
        SaveStateData s,
        Z80Registers r,
        SpectrumMemory mem,
        SpectrumUla ula,
        Ay8912 ay)
    {
        mem.SetMachine(s.Machine);
        mem.ResetRam();
        for (int i = 0; i < 8 && i < s.RamBanks.Length; i++)
            Buffer.BlockCopy(s.RamBanks[i], 0, mem.RamBank(i), 0, Math.Min(0x4000, s.RamBanks[i].Length));
        if (s.Ram48 is { Length: > 0 })
            Buffer.BlockCopy(s.Ram48, 0, mem.Ram48, 0, Math.Min(0xC000, s.Ram48.Length));
        mem.ForcePaging(s.Last7Ffd, s.PagingDisabled);

        var regs = s.Registers;
        r.A = regs[0]; r.F = regs[1];
        r.B = regs[2]; r.C = regs[3];
        r.D = regs[4]; r.E = regs[5];
        r.H = regs[6]; r.L = regs[7];
        r.A2 = regs[8]; r.F2 = regs[9];
        r.B2 = regs[10]; r.C2 = regs[11];
        r.D2 = regs[12]; r.E2 = regs[13];
        r.H2 = regs[14]; r.L2 = regs[15];
        r.I = regs[16]; r.R = regs[17];
        r.IX = BitConverter.ToUInt16(regs, 18);
        r.IY = BitConverter.ToUInt16(regs, 20);
        r.SP = BitConverter.ToUInt16(regs, 22);
        r.PC = BitConverter.ToUInt16(regs, 24);
        r.IFF1 = regs[26] != 0;
        r.IFF2 = regs[27] != 0;
        r.IM = regs[28];
        r.Halted = regs[29] != 0;
        r.EIDeferred = false;

        ula.Border = s.Border;
        ula.EarBit = s.EarBit;

        ay.Reset();
        for (int i = 0; i < 16 && i < s.AyRegs.Length; i++)
        {
            ay.SelectRegister((byte)i);
            ay.WriteData(s.AyRegs[i]);
        }
    }

    public static async Task SaveToFileAsync(string path, SaveStateData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, data);
    }

    public static async Task<SaveStateData> LoadFromFileAsync(string path)
    {
        await using var fs = File.OpenRead(path);
        return (await JsonSerializer.DeserializeAsync<SaveStateData>(fs))
               ?? throw new InvalidDataException("Invalid save state");
    }
}
