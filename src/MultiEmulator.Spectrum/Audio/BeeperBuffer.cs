namespace MultiEmulator.Spectrum.Audio;

/// <summary>
/// Beeper alineado con Fuse 1.9+ y la literatura de 1-bit Spectrum:
/// 1) Caja (promedio ULA por muestra host) → demodula PWM ultrasónico (~18 kHz Arkanoid).
/// 2) Filtro eléctrico ULA asimétrico (ula_filter.c de Fuse).
/// 3) HPF resonante 750 Hz (speaker_filter.c de Fuse) → piezo interno.
/// El LPF+DC a 192 kHz anterior convertía esa portadora en DC y la borraba.
/// </summary>
public sealed class BeeperBuffer
{
    public const int HostRate = 44100;

    // Niveles EAR/MIC centrados (Fuse mezcla MIC+beeper; aquí 4 voltajes relativos).
    private static readonly double[] UlaLevels =
    {
        -1.00, // EAR=0 MIC=0
        -0.52, // EAR=0 MIC=1
         0.60, // EAR=1 MIC=0
         1.00  // EAR=1 MIC=1
    };

    private const float MasterGain = 0.70f;

    // Fuse ula_filter.h — parámetros congelados de listening tests.
    private const double UlaRiseTau = 36.53558495933805e-6;
    private const double UlaFallTau = 68.86073172982108e-6;

    // Fuse speaker_filter.c
    private const double SpeakerFreq = 750.0;
    private const double SpeakerQ = 0.7071067811865475;

    private readonly float[] _ring;
    private int _write;
    private int _read;

    private double _tPerHost = 3_500_000.0 / HostRate;
    private double _partialT;
    private double _levelSum;

    private double _ulaAlphaRise;
    private double _ulaAlphaFall;
    private double _ulaState;
    private bool _ulaInit;

    private double _spB0, _spB1, _spB2, _spA1, _spA2;
    private double _spZ1, _spZ2;
    private bool _spInit;

    public BeeperBuffer(int capacityHostSamples = HostRate / 2)
    {
        _ring = new float[capacityHostSamples];
        ConfigureFilters(HostRate);
        SetFrameTStates(69888);
    }

    public void SetFrameTStates(int frameTStates)
    {
        int cpuHz = Math.Max(1, frameTStates * 50);
        _tPerHost = (double)cpuHz / HostRate;
        if (_tPerHost < 1.0) _tPerHost = 1.0;
    }

    public void Reset()
    {
        _write = _read = 0;
        _partialT = _levelSum = 0;
        _ulaState = 0;
        _ulaInit = false;
        _spZ1 = _spZ2 = 0;
        _spInit = false;
        Array.Clear(_ring);
    }

    /// <summary>Avanza T-states con bits EAR (bit4) y MIC (bit3).</summary>
    public void Advance(int tStates, bool earOut, bool micOut)
    {
        if (tStates <= 0) return;
        int levelIndex = (earOut ? 2 : 0) | (micOut ? 1 : 0);
        double level = UlaLevels[levelIndex];
        double left = tStates;

        while (left > 0)
        {
            double need = _tPerHost - _partialT;
            double take = left < need ? left : need;
            _levelSum += level * take;
            _partialT += take;
            left -= take;

            if (_partialT + 1e-12 >= _tPerHost)
            {
                double avg = _levelSum / _tPerHost;
                _levelSum = 0;
                _partialT = 0;
                PushHost(ProcessSample(avg));
            }
        }
    }

    public void Read(Span<float> dst)
    {
        for (int i = 0; i < dst.Length; i++)
        {
            if (_read != _write)
            {
                dst[i] = _ring[_read];
                _read = (_read + 1) % _ring.Length;
            }
            else
            {
                dst[i] = 0f;
            }
        }
    }

    private float ProcessSample(double avg)
    {
        double ula = ApplyUla(avg);
        double sp = ApplySpeaker(ula);
        return Math.Clamp((float)(sp * MasterGain), -1f, 1f);
    }

    private double ApplyUla(double input)
    {
        if (!_ulaInit)
        {
            _ulaState = input;
            _ulaInit = true;
            return _ulaState;
        }

        double alpha = input > _ulaState ? _ulaAlphaRise : _ulaAlphaFall;
        _ulaState += alpha * (input - _ulaState);
        return _ulaState;
    }

    private double ApplySpeaker(double input)
    {
        if (!_spInit)
        {
            // Fuse: arrancar sin click artificial al primer nivel estable.
            _spZ1 = -_spB0 * input;
            _spZ2 = _spB2 * input;
            _spInit = true;
            return 0.0;
        }

        double output = _spB0 * input + _spZ1;
        _spZ1 = _spB1 * input - _spA1 * output + _spZ2;
        _spZ2 = _spB2 * input - _spA2 * output;
        return output;
    }

    private void PushHost(float sample)
    {
        int next = (_write + 1) % _ring.Length;
        if (next == _read)
            _read = (_read + 1) % _ring.Length;
        _ring[_write] = sample;
        _write = next;
    }

    private void ConfigureFilters(int sampleRate)
    {
        // Fuse: -expm1(-1/(rate*tau)) ≡ 1 - exp(-1/(rate*tau))
        _ulaAlphaRise = 1.0 - Math.Exp(-1.0 / (sampleRate * UlaRiseTau));
        _ulaAlphaFall = 1.0 - Math.Exp(-1.0 / (sampleRate * UlaFallTau));

        double omega = 2.0 * Math.PI * SpeakerFreq / sampleRate;
        double alpha = Math.Sin(omega) / (2.0 * SpeakerQ);
        double cosine = Math.Cos(omega);
        double a0 = 1.0 + alpha;
        _spB0 = (1.0 + cosine) / (2.0 * a0);
        _spB1 = -(1.0 + cosine) / a0;
        _spB2 = _spB0;
        _spA1 = -2.0 * cosine / a0;
        _spA2 = (1.0 - alpha) / a0;
    }
}
