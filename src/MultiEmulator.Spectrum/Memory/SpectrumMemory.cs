namespace MultiEmulator.Spectrum.Memory;

public sealed class SpectrumMemory
{
    private readonly byte[] _rom48 = new byte[0x4000];
    private readonly byte[] _rom128_0 = new byte[0x4000];
    private readonly byte[] _rom128_1 = new byte[0x4000];
    private readonly byte[] _romTrDos = new byte[0x4000];
    private readonly byte[] _ram48 = new byte[0xC000]; // 48K contiguous from 0x4000
    private readonly byte[][] _ram = new byte[8][];
    private MachineType _machine = MachineType.Spectrum48;
    private byte _last7Ffd;
    private bool _pagingDisabled;
    private bool _trDosRomLoaded;
    private bool _trDosPaged;

    public SpectrumMemory()
    {
        for (int i = 0; i < 8; i++)
            _ram[i] = new byte[0x4000];
    }

    public MachineType Machine => _machine;
    public byte Last7Ffd => _last7Ffd;
    public bool TrDosRomLoaded => _trDosRomLoaded;
    public bool TrDosPaged
    {
        get => _trDosPaged;
        set => _trDosPaged = value && _trDosRomLoaded;
    }

    /// <summary>Display file: 48K from start of RAM; 128K bank 5 or 7.</summary>
    public byte[] ScreenBank =>
        _machine == MachineType.Spectrum48
            ? _ram48
            : _ram[(_last7Ffd & 0x08) != 0 ? 7 : 5];

    public byte[] RamBank(int bank) => _ram[bank & 7];
    public byte[] Ram48 => _ram48;
    public bool ContendedBankActive => (_last7Ffd & 0x07) >= 4;

    public void SetMachine(MachineType machine)
    {
        _machine = machine;
        _last7Ffd = 0;
        _pagingDisabled = false;
    }

    public void LoadRom48(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < 0x4000) throw new ArgumentException("48K ROM must be 16384 bytes");
        rom[..0x4000].CopyTo(_rom48);
    }

    public void LoadRom128(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < 0x8000) throw new ArgumentException("128K ROM must be 32768 bytes");
        rom[..0x4000].CopyTo(_rom128_0);
        rom.Slice(0x4000, 0x4000).CopyTo(_rom128_1);
    }

    public void LoadTrDosRom(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < 0x4000) throw new ArgumentException("TR-DOS ROM must be 16384 bytes");
        rom[..0x4000].CopyTo(_romTrDos);
        _trDosRomLoaded = true;
    }

    public void ResetRam()
    {
        Array.Clear(_ram48);
        foreach (var bank in _ram)
            Array.Clear(bank);
        _last7Ffd = 0;
        _pagingDisabled = false;
    }

    public void Write7Ffd(byte value)
    {
        if (_machine != MachineType.Spectrum128 || _pagingDisabled)
            return;
        _last7Ffd = value;
        if ((value & 0x20) != 0)
            _pagingDisabled = true;
    }

    public byte Read(ushort addr)
    {
        if (addr < 0x4000)
            return ReadRom(addr);

        if (_machine == MachineType.Spectrum48)
            return _ram48[addr - 0x4000];

        return addr switch
        {
            < 0x8000 => _ram[5][addr - 0x4000],
            < 0xC000 => _ram[2][addr - 0x8000],
            _ => _ram[_last7Ffd & 0x07][addr - 0xC000]
        };
    }

    public void Write(ushort addr, byte value)
    {
        if (addr < 0x4000)
            return;

        if (_machine == MachineType.Spectrum48)
        {
            _ram48[addr - 0x4000] = value;
            return;
        }

        switch (addr)
        {
            case < 0x8000:
                _ram[5][addr - 0x4000] = value;
                break;
            case < 0xC000:
                _ram[2][addr - 0x8000] = value;
                break;
            default:
                _ram[_last7Ffd & 0x07][addr - 0xC000] = value;
                break;
        }
    }

    public void WriteRamAbsolute(int bank, int offset, byte value)
    {
        _ram[bank & 7][offset & 0x3FFF] = value;
        // Keep 48K view in sync when loading 48K snaps into bank layout
        if (_machine == MachineType.Spectrum48 && bank <= 2)
        {
            int baseAddr = bank * 0x4000;
            _ram48[baseAddr + (offset & 0x3FFF)] = value;
        }
    }

    public byte ReadRamAbsolute(int bank, int offset) => _ram[bank & 7][offset & 0x3FFF];

    private byte ReadRom(ushort addr)
    {
        if (_trDosPaged && _trDosRomLoaded)
            return _romTrDos[addr];

        if (_machine == MachineType.Spectrum48)
            return _rom48[addr];

        bool rom1 = (_last7Ffd & 0x10) != 0;
        return rom1 ? _rom128_1[addr] : _rom128_0[addr];
    }

    public void ForcePaging(byte last7ffd, bool disabled)
    {
        _last7Ffd = last7ffd;
        _pagingDisabled = disabled;
    }

    public bool PagingDisabled => _pagingDisabled;
}
