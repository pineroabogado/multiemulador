namespace MultiEmulator.Spectrum.Disk;

/// <summary>
/// Beta 128 + WD1793 mínimo para imágenes .trd (lectura).
/// Paginación mágica TR-DOS: M1 en $3D00–$3DFF entra; M1 ≥ $4000 sale.
/// </summary>
public sealed class BetaDisk
{
    private const int SectorsPerTrack = 16;
    private const int SectorSize = 256;
    private const int Sides = 2;
    private const int TracksPerSide = 80;
    private const int TrackSize = SectorsPerTrack * SectorSize;

    private const byte StBusy = 0x01;
    private const byte StDrq = 0x02;
    private const byte StTrack0 = 0x04;
    private const byte StRecordNotFound = 0x10;
    private const byte StWriteProtect = 0x40;
    private const byte StNotReady = 0x80;

    private enum CmdType { None, ReadSector }

    private byte _status;
    private byte _track;
    private byte _sector = 1;
    private byte _data;
    private CmdType _command;
    private byte _system;
    private byte _side;
    private byte _drive;
    private readonly byte[] _sectorBuf = new byte[SectorSize];
    private int _bufPos;
    private bool _intrq;
    private bool _drq;
    private byte[]? _disk;

    public bool Enabled { get; set; }
    public bool TrDosPaged { get; private set; }
    public bool HasDisk => _disk != null;
    public int SectorReadCount { get; private set; }
    public int SectorFailCount { get; private set; }
    public string LastIo { get; private set; } = "";

    public void ForcePaged(bool paged) => TrDosPaged = paged && Enabled;

    public void Insert(byte[] trdImage)
    {
        // Rellenar a tamaño DS 80 pistas si viene recortada (p.ej. SuperTetris).
        int full = TracksPerSide * Sides * TrackSize;
        if (trdImage.Length >= full)
        {
            _disk = trdImage;
        }
        else
        {
            var padded = new byte[full];
            Buffer.BlockCopy(trdImage, 0, padded, 0, trdImage.Length);
            _disk = padded;
        }
    }

    public void Eject()
    {
        _disk = null;
        TrDosPaged = false;
        _command = CmdType.None;
        _drq = false;
        _intrq = false;
        _status = 0;
    }

    public void Reset()
    {
        TrDosPaged = false;
        _status = 0;
        _track = 0;
        _sector = 1;
        _data = 0;
        _command = CmdType.None;
        _system = 0;
        _side = 0;
        _drive = 0;
        _bufPos = 0;
        _intrq = false;
        _drq = false;
    }

    public void OnM1(ushort addr)
    {
        if (!Enabled) return;
        if (!TrDosPaged)
        {
            if (addr is >= 0x3D00 and <= 0x3DFF)
                TrDosPaged = true;
        }
        else if (addr >= 0x4000)
        {
            TrDosPaged = false;
        }
    }

    public bool ClaimsPort(ushort port)
    {
        if (!Enabled || !TrDosPaged) return false;
        return (port & 0xFF) is 0x1F or 0x3F or 0x5F or 0x7F or 0xFF;
    }

    public byte ReadPort(ushort port)
    {
        return (port & 0xFF) switch
        {
            0x1F => ReadStatus(),
            0x3F => _track,
            0x5F => _sector,
            0x7F => ReadData(),
            0xFF => ReadSystem(),
            _ => 0xFF
        };
    }

    public void WritePort(ushort port, byte value)
    {
        switch (port & 0xFF)
        {
            case 0x1F:
                ExecuteCommand(value);
                break;
            case 0x3F:
                _track = value;
                break;
            case 0x5F:
                _sector = value;
                break;
            case 0x7F:
                _data = value;
                break;
            case 0xFF:
                _system = value;
                _drive = (byte)(value & 0x03);
                _side = (byte)((value & 0x10) != 0 ? 1 : 0);
                break;
        }
    }

    private byte ReadStatus()
    {
        _intrq = false;
        byte s = _status;
        if (_disk == null)
            s |= StNotReady;
        return s;
    }

    private byte ReadSystem()
    {
        byte val = 0x3F;
        if (_intrq) val |= 0x80;
        if (_drq) val |= 0x40;
        return val;
    }

    private byte ReadData()
    {
        if (_command == CmdType.ReadSector && _drq)
        {
            byte b = _bufPos < _sectorBuf.Length ? _sectorBuf[_bufPos] : (byte)0;
            _bufPos++;
            if (_bufPos >= SectorSize)
            {
                _drq = false;
                _status = (byte)(_status & ~(StBusy | StDrq));
                _intrq = true;
                _command = CmdType.None;
            }
            return b;
        }
        return _data;
    }

    private void ExecuteCommand(byte cmd)
    {
        _intrq = false;
        switch (cmd & 0xF0)
        {
            case 0x00: // Restore
                _track = 0;
                _status = _disk != null ? StTrack0 : StNotReady;
                _intrq = true;
                _command = CmdType.None;
                break;
            case 0x10: // Seek
                _track = _data;
                _status = (byte)(_track == 0 ? StTrack0 : 0);
                if (_disk == null) _status |= StNotReady;
                _intrq = true;
                _command = CmdType.None;
                break;
            case 0x20:
            case 0x30: // Step
                _status = (byte)(_track == 0 ? StTrack0 : 0);
                _intrq = true;
                _command = CmdType.None;
                break;
            case 0x40:
            case 0x50: // Step in
                if (_track < 79) _track++;
                _status = 0;
                _intrq = true;
                _command = CmdType.None;
                break;
            case 0x60:
            case 0x70: // Step out
                if (_track > 0) _track--;
                _status = (byte)(_track == 0 ? StTrack0 : 0);
                _intrq = true;
                _command = CmdType.None;
                break;
            case 0x80:
            case 0x90: // Read sector
                if (TryLoadSector())
                {
                    _bufPos = 0;
                    _drq = true;
                    _status = StBusy | StDrq;
                    _command = CmdType.ReadSector;
                    SectorReadCount++;
                    LastIo = $"RD cyl={_track} side={_side} sec={_sector}";
                }
                else
                {
                    _status = StRecordNotFound;
                    _intrq = true;
                    _command = CmdType.None;
                    SectorFailCount++;
                    LastIo = $"RNF cyl={_track} side={_side} sec={_sector}";
                }
                break;
            case 0xA0:
            case 0xB0: // Write sector — solo lectura
                _status = StWriteProtect;
                _intrq = true;
                _command = CmdType.None;
                break;
            case 0xC0: // Read address
                _data = _track;
                _status = 0;
                _intrq = true;
                _command = CmdType.None;
                break;
            case 0xD0: // Force interrupt
                _status = (byte)(_status & ~StBusy);
                _drq = false;
                _command = CmdType.None;
                if ((cmd & 0x0F) != 0) _intrq = true;
                break;
            default:
                _status = 0;
                _intrq = true;
                _command = CmdType.None;
                break;
        }
    }

    private bool TryLoadSector()
    {
        if (_disk == null || _drive != 0) return false;
        // WD1793 sector 1..16 → índice 0..15
        int secIndex = _sector - 1;
        if (_track >= TracksPerSide || secIndex is < 0 or >= SectorsPerTrack || _side > 1)
            return false;

        int linearTrack = _track * Sides + _side;
        int offset = linearTrack * TrackSize + secIndex * SectorSize;
        if (offset < 0 || offset + SectorSize > _disk.Length)
            return false;

        Buffer.BlockCopy(_disk, offset, _sectorBuf, 0, SectorSize);
        return true;
    }
}
