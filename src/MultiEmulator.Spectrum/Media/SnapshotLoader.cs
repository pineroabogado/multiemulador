using MultiEmulator.Spectrum.Cpu;
using MultiEmulator.Spectrum.Memory;

namespace MultiEmulator.Spectrum.Media;

public static class SnapshotLoader
{
    public static void LoadSna(byte[] data, Z80Registers r, SpectrumMemory mem, out byte border)
    {
        if (data.Length != 49179 && data.Length != 131103)
            throw new InvalidDataException("Invalid SNA size");

        border = 0;
        r.I = data[0];
        r.L2 = data[1]; r.H2 = data[2];
        r.E2 = data[3]; r.D2 = data[4];
        r.C2 = data[5]; r.B2 = data[6];
        r.F2 = data[7]; r.A2 = data[8];
        r.L = data[9]; r.H = data[10];
        r.E = data[11]; r.D = data[12];
        r.C = data[13]; r.B = data[14];
        r.IY = (ushort)(data[15] | (data[16] << 8));
        r.IX = (ushort)(data[17] | (data[18] << 8));
        r.IFF2 = (data[19] & 0x04) != 0;
        r.IFF1 = r.IFF2;
        r.R = data[20];
        r.F = data[21]; r.A = data[22];
        r.SP = (ushort)(data[23] | (data[24] << 8));
        r.IM = (byte)(data[25] & 0x03);
        border = (byte)(data[26] & 0x07);

        mem.SetMachine(MachineType.Spectrum48);
        for (int i = 0; i < 0xC000; i++)
            mem.Write((ushort)(0x4000 + i), data[27 + i]);

        // RETN from stack
        byte lo = mem.Read(r.SP);
        byte hi = mem.Read((ushort)(r.SP + 1));
        r.SP += 2;
        r.PC = (ushort)(lo | (hi << 8));
        r.Halted = false;

        if (data.Length == 131103)
        {
            mem.SetMachine(MachineType.Spectrum128);
            byte port7ffd = data[49181];
            // remaining banks...
            mem.ForcePaging(port7ffd, false);
            // 128 SNA layout: after 48K dump comes PC then 7FFD then remaining banks
            r.PC = (ushort)(data[49179] | (data[49180] << 8));
            int offset = 49183;
            for (int bank = 0; bank < 8; bank++)
            {
                if (bank == 2 || bank == 5 || bank == (port7ffd & 7))
                    continue;
                for (int i = 0; i < 0x4000 && offset < data.Length; i++)
                    mem.WriteRamAbsolute(bank, i, data[offset++]);
            }
        }
    }

    public static void LoadZ80(byte[] data, Z80Registers r, SpectrumMemory mem, out byte border, out MachineType machine)
    {
        machine = MachineType.Spectrum48;
        border = 0;
        r.Reset();
        mem.ResetRam();

        r.A = data[0];
        r.F = data[1];
        r.C = data[2]; r.B = data[3];
        r.L = data[4]; r.H = data[5];
        r.PC = (ushort)(data[6] | (data[7] << 8));
        r.SP = (ushort)(data[8] | (data[9] << 8));
        r.I = data[10];
        r.R = (byte)((data[11] & 0x7F) | ((data[12] & 0x01) << 7));
        byte flags12 = data[12];
        if (flags12 == 255) flags12 = 1;
        border = (byte)((flags12 >> 1) & 0x07);
        bool compressed = (flags12 & 0x20) != 0;
        r.E = data[13]; r.D = data[14];
        r.C2 = data[15]; r.B2 = data[16];
        r.E2 = data[17]; r.D2 = data[18];
        r.L2 = data[19]; r.H2 = data[20];
        r.A2 = data[21]; r.F2 = data[22];
        r.IY = (ushort)(data[23] | (data[24] << 8));
        r.IX = (ushort)(data[25] | (data[26] << 8));
        r.IFF1 = data[27] != 0;
        r.IFF2 = data[28] != 0;
        r.IM = (byte)(data[29] & 0x03);

        int dataOffset = 30;
        if (r.PC == 0)
        {
            // v2/v3
            int headerLen = data[30] | (data[31] << 8);
            dataOffset = 32 + headerLen;
            r.PC = (ushort)(data[32] | (data[33] << 8));
            byte hw = data[34];
            byte port7ffd = data[35];
            if (headerLen > 23)
            {
                // v3
            }

            machine = hw switch
            {
                0 or 1 => MachineType.Spectrum48,
                3 or 4 or 5 or 6 => MachineType.Spectrum128,
                _ => hw >= 3 ? MachineType.Spectrum128 : MachineType.Spectrum48
            };
            // In v2: 3=48+IF1, 4=128; v3 differs slightly. Use port presence.
            if (headerLen >= 1 && (hw == 4 || hw == 5 || hw == 6 || (headerLen > 23 && hw >= 4)))
                machine = MachineType.Spectrum128;
            if (headerLen == 23 && hw == 3) machine = MachineType.Spectrum128; // v2
            if (headerLen > 23 && hw == 4) machine = MachineType.Spectrum128;

            mem.SetMachine(machine);
            if (machine == MachineType.Spectrum128)
                mem.ForcePaging(port7ffd, false);

            while (dataOffset + 3 <= data.Length)
            {
                int blockLen = data[dataOffset] | (data[dataOffset + 1] << 8);
                int page = data[dataOffset + 2];
                dataOffset += 3;
                if (blockLen == 0xFFFF)
                {
                    // uncompressed 16K
                    blockLen = 0x4000;
                    LoadZ80Page(mem, machine, page, data.AsSpan(dataOffset, blockLen), false);
                    dataOffset += blockLen;
                }
                else
                {
                    if (dataOffset + blockLen > data.Length) break;
                    LoadZ80Page(mem, machine, page, data.AsSpan(dataOffset, blockLen), true);
                    dataOffset += blockLen;
                }
            }
        }
        else
        {
            // v1
            mem.SetMachine(MachineType.Spectrum48);
            var ram = compressed
                ? DecompressZ80(data.AsSpan(dataOffset), 0xC000)
                : data.AsSpan(dataOffset, Math.Min(0xC000, data.Length - dataOffset)).ToArray();
            for (int i = 0; i < ram.Length && i < 0xC000; i++)
                mem.Write((ushort)(0x4000 + i), ram[i]);
        }

        r.Halted = false;
        r.EIDeferred = false;
    }

    private static void LoadZ80Page(SpectrumMemory mem, MachineType machine, int pageId, ReadOnlySpan<byte> src, bool compressed)
    {
        byte[] page = compressed ? DecompressZ80(src, 0x4000) : src[..Math.Min(0x4000, src.Length)].ToArray();
        if (machine == MachineType.Spectrum48)
        {
            int dest = pageId switch
            {
                8 => 0x4000,
                4 => 0x8000,
                5 => 0xC000,
                _ => -1
            };
            if (dest < 0) return;
            for (int i = 0; i < page.Length && i < 0x4000; i++)
                mem.Write((ushort)(dest + i), page[i]);
        }
        else
        {
            // 128: pageId is bank number + 3 for RAM banks 0-7 typically page = bank+3
            int bank = pageId - 3;
            if (bank is >= 0 and <= 7)
            {
                for (int i = 0; i < page.Length && i < 0x4000; i++)
                    mem.WriteRamAbsolute(bank, i, page[i]);
            }
        }
    }

    private static byte[] DecompressZ80(ReadOnlySpan<byte> src, int maxLen)
    {
        var dst = new List<byte>(maxLen);
        int i = 0;
        while (i < src.Length && dst.Count < maxLen)
        {
            if (i + 4 <= src.Length && src[i] == 0x00 && src[i + 1] == 0xED && src[i + 2] == 0xED && src[i + 3] == 0x00)
                break;
            if (i + 4 <= src.Length && src[i] == 0xED && src[i + 1] == 0xED)
            {
                int count = src[i + 2];
                byte val = src[i + 3];
                for (int n = 0; n < count && dst.Count < maxLen; n++)
                    dst.Add(val);
                i += 4;
            }
            else
            {
                dst.Add(src[i++]);
            }
        }
        return dst.ToArray();
    }
}
