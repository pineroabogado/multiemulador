namespace MultiEmulator.Spectrum.Audio;

public sealed class Ay8912
{
    private readonly byte[] _regs = new byte[16];
    private int _selected;
    private int _tonePeriod0, _tonePeriod1, _tonePeriod2;
    private int _toneCount0, _toneCount1, _toneCount2;
    private bool _toneOut0, _toneOut1, _toneOut2;
    private int _noisePeriod, _noiseCount;
    private int _noiseSeed = 1;
    private bool _noiseOut;
    private int _envPeriod, _envCount, _envPos;
    private bool _envHolding, _envAttack;
    private readonly int[] _envShape = new int[16];

    public byte[] Registers => _regs;

    public void Reset()
    {
        Array.Clear(_regs);
        _selected = 0;
        _toneCount0 = _toneCount1 = _toneCount2 = 0;
        _toneOut0 = _toneOut1 = _toneOut2 = false;
        _noiseCount = 0;
        _noiseSeed = 1;
        _noiseOut = false;
        _envCount = 0;
        _envPos = 0;
        _envHolding = false;
        UpdatePeriods();
    }

    public void SelectRegister(byte reg) => _selected = reg & 0x0F;

    public void WriteData(byte value)
    {
        _regs[_selected] = _selected switch
        {
            1 or 3 or 5 => (byte)(value & 0x0F),
            6 => (byte)(value & 0x1F),
            8 or 9 or 10 => (byte)(value & 0x1F),
            13 => (byte)(value & 0x0F),
            _ => value
        };

        if (_selected is 0 or 1 or 2 or 3 or 4 or 5 or 6)
            UpdatePeriods();
        if (_selected == 13)
        {
            _envPos = 0;
            _envHolding = false;
            _envAttack = (_regs[13] & 0x04) != 0;
        }
        if (_selected is 11 or 12)
            _envPeriod = Math.Max(1, (_regs[12] << 8) | _regs[11]);
    }

    public byte ReadData() => _regs[_selected];

    private void UpdatePeriods()
    {
        _tonePeriod0 = Math.Max(1, ((_regs[1] & 0x0F) << 8) | _regs[0]);
        _tonePeriod1 = Math.Max(1, ((_regs[3] & 0x0F) << 8) | _regs[2]);
        _tonePeriod2 = Math.Max(1, ((_regs[5] & 0x0F) << 8) | _regs[4]);
        _noisePeriod = Math.Max(1, _regs[6] & 0x1F);
        _envPeriod = Math.Max(1, (_regs[12] << 8) | _regs[11]);
    }

    public float RenderSample()
    {
        // AY clock is ~1.7734 MHz on 128K; we call this once per host audio sample (~44100)
        // Approximate: ~40 AY clocks per sample
        const int clocks = 40;
        for (int i = 0; i < clocks; i++)
            Tick();

        float mix = 0;
        mix += ChannelLevel(0, _toneOut0);
        mix += ChannelLevel(1, _toneOut1);
        mix += ChannelLevel(2, _toneOut2);
        return mix / 3f;
    }

    private float ChannelLevel(int ch, bool toneOut)
    {
        byte volReg = _regs[8 + ch];
        int volume = (volReg & 0x10) != 0 ? EnvelopeVolume() : volReg & 0x0F;
        bool toneEnable = (_regs[7] & (1 << ch)) == 0;
        bool noiseEnable = (_regs[7] & (1 << (ch + 3))) == 0;
        bool output = true;
        if (toneEnable) output &= toneOut;
        if (noiseEnable) output &= _noiseOut;
        if (!toneEnable && !noiseEnable) output = true;
        if (!output) return 0;
        return volume / 15f;
    }

    private int EnvelopeVolume()
    {
        // simplified envelope
        int shape = _regs[13] & 0x0F;
        int pos = _envPos & 0x1F;
        bool continueEnv = (shape & 0x08) != 0;
        bool attack = (shape & 0x04) != 0;
        bool alternate = (shape & 0x02) != 0;
        bool hold = (shape & 0x01) != 0;

        if (!continueEnv && pos >= 16)
            return 0;

        int level = pos < 16 ? pos : 15 - (pos & 0x0F);
        if (!attack) level = 15 - level;
        if (continueEnv && alternate && ((pos / 16) & 1) == 1)
            level = 15 - level;
        if (continueEnv && hold && pos >= 16)
            level = attack ^ alternate ? 15 : 0;
        return level & 0x0F;
    }

    private void Tick()
    {
        if (++_toneCount0 >= _tonePeriod0) { _toneCount0 = 0; _toneOut0 = !_toneOut0; }
        if (++_toneCount1 >= _tonePeriod1) { _toneCount1 = 0; _toneOut1 = !_toneOut1; }
        if (++_toneCount2 >= _tonePeriod2) { _toneCount2 = 0; _toneOut2 = !_toneOut2; }

        if (++_noiseCount >= _noisePeriod)
        {
            _noiseCount = 0;
            bool bit0 = (_noiseSeed & 1) != 0;
            bool bit3 = (_noiseSeed & 8) != 0;
            _noiseSeed = (_noiseSeed >> 1) | ((bit0 ^ bit3 ? 1 : 0) << 16);
            _noiseOut = (_noiseSeed & 1) != 0;
        }

        if (++_envCount >= _envPeriod)
        {
            _envCount = 0;
            if (!_envHolding)
            {
                _envPos++;
                if ((_regs[13] & 0x09) == 0x09 || (_regs[13] & 0x01) != 0)
                {
                    if (_envPos >= 32)
                        _envHolding = true;
                }
                else if (_envPos >= 64)
                    _envPos = 0;
            }
        }
    }

    public void CopyFrom(Ay8912 other)
    {
        Buffer.BlockCopy(other._regs, 0, _regs, 0, 16);
        _selected = other._selected;
        UpdatePeriods();
    }
}
