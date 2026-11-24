namespace MultiEmulator.Spectrum.Tape;

/// <summary>Modo de transporte de cinta estilo magnetófono / VHS.</summary>
public enum TapeTransportMode
{
    Play,
    FastForward,
    Rewind
}

/// <summary>
/// Control de reproducción/avance/rebobinado sobre <see cref="TapeImage"/> + <see cref="TapePulsePlayer"/>.
/// Velocidades en caliente: ×1 (play), ×2/×4/×8 (FF/REW). El modo funciona aunque no haya cinta
/// (efecto visual); con cinta enrolla pulsos/bloques de verdad.
/// </summary>
public sealed class TapeTransport
{
    public static readonly int[] FastForwardSpeeds = { 2, 4, 8 };

    private readonly TapePulsePlayer _pulses;
    private TapeImage? _tape;
    private int _ffSpeedIndex;

    public TapeTransport(TapePulsePlayer pulses) => _pulses = pulses;

    public TapeTransportMode Mode { get; private set; } = TapeTransportMode.Play;

    /// <summary>×1 en Play; ×2/×4/×8 en FF/REW.</summary>
    public int SpeedFactor { get; private set; } = 1;

    public bool MuteAudio => Mode is TapeTransportMode.FastForward or TapeTransportMode.Rewind;

    public bool IsShuttle => Mode is TapeTransportMode.FastForward or TapeTransportMode.Rewind;

    public string StatusLabel => Mode switch
    {
        TapeTransportMode.FastForward => $"Cinta FF ×{SpeedFactor}",
        TapeTransportMode.Rewind => $"Cinta REW ×{SpeedFactor}",
        _ => "Cinta ▶"
    };

    public void Attach(TapeImage? tape)
    {
        _tape = tape;
        Mode = TapeTransportMode.Play;
        SpeedFactor = 1;
        _ffSpeedIndex = 0;
        _pulses.TurboSpeed = 1;
    }

    public void Play()
    {
        Mode = TapeTransportMode.Play;
        SpeedFactor = 1;
        _pulses.TurboSpeed = 1;
        if (_tape != null && _tape.Position < _tape.Blocks.Count)
            _tape.IsPlaying = true;
    }

    /// <summary>Ciclo VHS: Play→FF×2→FF×4→FF×8→Play. Funciona sin cinta (modo deck).</summary>
    public void CycleFastForward()
    {
        if (Mode != TapeTransportMode.FastForward)
        {
            Mode = TapeTransportMode.FastForward;
            _ffSpeedIndex = 0;
        }
        else
        {
            _ffSpeedIndex++;
            if (_ffSpeedIndex >= FastForwardSpeeds.Length)
            {
                Play();
                return;
            }
        }

        SpeedFactor = FastForwardSpeeds[_ffSpeedIndex];
        _pulses.TurboSpeed = SpeedFactor;
        if (_tape != null && _tape.Position < _tape.Blocks.Count)
            _tape.IsPlaying = true;
    }

    /// <summary>Ciclo REW ×2/×4/×8 → Play. Funciona sin cinta (modo deck).</summary>
    public void CycleRewind()
    {
        if (Mode != TapeTransportMode.Rewind)
        {
            Mode = TapeTransportMode.Rewind;
            _ffSpeedIndex = 0;
        }
        else
        {
            _ffSpeedIndex++;
            if (_ffSpeedIndex >= FastForwardSpeeds.Length)
            {
                Play();
                return;
            }
        }

        SpeedFactor = FastForwardSpeeds[_ffSpeedIndex];
        _pulses.TurboSpeed = 1;
        if (_tape != null && (_tape.Position < _tape.Blocks.Count || _tape.Position > 0))
            _tape.IsPlaying = true;
    }

    public void RewindOneBlock()
    {
        if (_tape == null) { Play(); return; }
        int target = _pulses.HasPartialBlockProgress
            ? _pulses.PlayPosition
            : Math.Max(0, Math.Min(_pulses.PlayPosition, _tape.Blocks.Count) - 1);
        // Si estamos al final (Position == Count), un bloque atrás = Count-1
        if (_tape.Position >= _tape.Blocks.Count)
            target = Math.Max(0, _tape.Blocks.Count - 1);
        Seek(target);
        Play();
    }

    public void RewindToStart()
    {
        if (_tape == null) { Play(); return; }
        Seek(0);
        Play();
    }

    public void Seek(int blockIndex)
    {
        if (_tape == null) return;
        blockIndex = Math.Clamp(blockIndex, 0, _tape.Blocks.Count);
        _tape.Position = blockIndex;
        _pulses.Seek(blockIndex);
        if (blockIndex < _tape.Blocks.Count)
            _tape.IsPlaying = true;
    }

    /// <summary>
    /// Llamar cada frame. FF quema pulsos EAR a TurboSpeed + extra y enrolla bloques;
    /// REW salta bloques atrás a ritmo visible.
    /// </summary>
    public void Tick(int frameTStates, bool allowRomTrapSkip = true)
    {
        if (frameTStates <= 0) return;

        if (Mode == TapeTransportMode.FastForward)
        {
            _pulses.TurboSpeed = SpeedFactor;
            if (_tape == null) return;

            // Quemar pulsos EAR extra (además del Advance normal del frame × TurboSpeed).
            if (_tape.IsPlaying)
                _pulses.Advance(frameTStates * Math.Max(1, SpeedFactor));

            // Enrollar bloques cuando no hay EAR activo (TAP/ROM-trap idle o fin de turbo).
            if (allowRomTrapSkip && !_pulses.IsActive && _tape.Position < _tape.Blocks.Count)
            {
                // ×2 → 1 bloque/frame, ×4 → 2, ×8 → 4 (visible de inmediato).
                int skips = Math.Max(1, SpeedFactor / 2);
                Seek(Math.Min(_tape.Blocks.Count, _tape.Position + skips));
            }
            return;
        }

        if (Mode == TapeTransportMode.Rewind)
        {
            _pulses.TurboSpeed = 1;
            if (_tape == null) return;

            // REW agresivo: varios bloques por frame según velocidad.
            int steps = Math.Max(1, SpeedFactor / 2);
            int pos = Math.Min(_pulses.PlayPosition, _tape.Position);
            if (_tape.Position >= _tape.Blocks.Count)
                pos = _tape.Blocks.Count;

            if (pos <= 0 && !_pulses.HasPartialBlockProgress)
            {
                Seek(0);
                // Mantener REW un instante visual: no forzar Play al instante;
                // el usuario pulsa ▶ o cicla. Pero si ya está en 0, quedarse en 0.
                return;
            }

            int target = _pulses.HasPartialBlockProgress
                ? pos
                : Math.Max(0, pos - steps);
            Seek(target);
        }
    }
}
