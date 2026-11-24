namespace MultiEmulator.Spectrum.Audio;

/// <summary>
/// Beeper estilo Fuse/RVM/Spectaculator:
/// - 4 niveles ULA (EAR bit4 + MIC bit3)
/// - generación interna a 192 kHz (ciclo→sample denso)
/// - paso bajo + DC block al decimar a 44,1 kHz
/// </summary>
public sealed class BeeperBuffer
{
    public const int HostRate = 44100;
    public const int InternalRate = 192000;

    // Amplitudes relativas de los 4 voltajes ULA (Spectaculator/Fuse-like), luego * MasterGain.
    private static readonly float[] UlaLevels =
    {
        0.00f, // EAR=0 MIC=0
        0.24f, // EAR=0 MIC=1
        0.80f, // EAR=1 MIC=0
        1.00f  // EAR=1 MIC=1
    };

    private const float MasterGain = 0.42f;
    private const float LowPassAlpha = 0.12f; // suave al bajar de 192k → 44.1k
    private const float DcAlpha = 0.998f;

    private readonly float[] _ring;
    private int _write;
    private int _read;

    private double _tPerInternal;   // T-states por sample interno (192 kHz)
    private double _internalAccum;
    private double _hostAccum;      // samples internos por sample host
    private readonly double _internalsPerHost = (double)InternalRate / HostRate;

    private float _target;          // nivel ULA actual (-ish bipolar via DC)
    private float _slewed;
    private float _lp;
    private float _dc;
    private float _hostHold;

    private int _cpuHz = 3_500_000; // 48K; 128K ≈ 3_546_900

    public BeeperBuffer(int capacityHostSamples = HostRate / 2)
    {
        _ring = new float[capacityHostSamples];
        SetFrameTStates(69888);
    }

    public void SetFrameTStates(int frameTStates)
    {
        // 50 Hz * frameTStates = T/s ≈ CPU Hz
        _cpuHz = Math.Max(1, frameTStates * 50);
        _tPerInternal = (double)_cpuHz / InternalRate;
        if (_tPerInternal < 0.01) _tPerInternal = 0.01;
    }

    public void Reset()
    {
        _write = _read = 0;
        _internalAccum = _hostAccum = 0;
        _target = _slewed = _lp = _dc = _hostHold = 0;
        Array.Clear(_ring);
    }

    /// <summary>Avanza T-states con bits de altavoz EAR (bit4) y MIC (bit3).</summary>
    public void Advance(int tStates, bool earOut, bool micOut)
    {
        if (tStates <= 0) return;
        int levelIndex = (earOut ? 2 : 0) | (micOut ? 1 : 0);
        // Centrar en torno a 0: (nivel - 0.5) para menos DC duro
        _target = (UlaLevels[levelIndex] - 0.50f) * 2f * MasterGain;

        _internalAccum += tStates;
        while (_internalAccum >= _tPerInternal)
        {
            _internalAccum -= _tPerInternal;
            EmitInternalSample();
        }
    }

    private void EmitInternalSample()
    {
        // Slew + LPF a tasa alta (anti-alias antes de decimar)
        _slewed += (_target - _slewed) * 0.55f;
        _lp += (_slewed - _lp) * LowPassAlpha;
        float ac = _lp - _dc;
        _dc = _dc * DcAlpha + _lp * (1f - DcAlpha);
        _hostHold = ac;

        _hostAccum += 1.0;
        while (_hostAccum >= _internalsPerHost)
        {
            _hostAccum -= _internalsPerHost;
            PushHost(_hostHold);
        }
    }

    private void PushHost(float sample)
    {
        int next = (_write + 1) % _ring.Length;
        if (next == _read)
            _read = (_read + 1) % _ring.Length;
        _ring[_write] = Math.Clamp(sample, -1f, 1f);
        _write = next;
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
}
