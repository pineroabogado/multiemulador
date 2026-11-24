namespace MultiEmulator.Spectrum.Tape;

/// <summary>
/// Reproductor EAR inspirado en Fuse/libspectrum + RVM:
/// - Bordes con nivel absoluto (no solo toggle)
/// - Pausas = silencio (nivel bajo), sin flanco espurio
/// - Bloques ROM (0x10/TAP) no se reproducen por EAR si la trampa LD-BYTES los carga
/// - Aceleración opcional (TurboSpeed / transporte FF) en bloques turbo y, si se pide, en todos los pulsos
/// </summary>
public sealed class TapePulsePlayer
{
    private readonly Queue<(int dur, bool level)> _edges = new();
    private bool _level;
    private int _remaining;
    private TapeImage? _tape;
    private int _playPos = -1;
    private bool _blockQueued;

    /// <summary>
    /// Multiplicador de T-states consumidos por pulso.
    /// En Play suele ser 1; el transporte FF lo pone a 2/4/8.
    /// </summary>
    public int TurboSpeed { get; set; } = 1;

    /// <summary>
    /// Si true, TurboSpeed también acelera bloques no-ROM (siempre).
    /// Si false (por defecto histórico), solo acelera bloques turbo EAR.
    /// El transporte FF fuerza aceleración de cualquier bloque con pulsos.
    /// </summary>
    public bool AccelerateRomTrapPulses { get; set; }

    /// <summary>
    /// Si true, los bloques estándar TAP/TZX 0x10 también emiten pulsos EAR
    /// (cargador custom en RAM, p.ej. Flying Shark). Si false, esperan la
    /// trampa LD-BYTES como Fuse cuando el PC está en la ROM de carga.
    /// </summary>
    public bool PlayStandardBlocksViaEar { get; set; }

    public bool Level => _level;

    public int PlayPosition => _playPos;

    /// <summary>True si el bloque actual tiene edges a medias (no estamos al inicio limpio).</summary>
    public bool HasPartialBlockProgress =>
        _blockQueued && (_remaining > 0 || _edges.Count > 0);

    /// <summary>
    /// True solo mientras hay pulsos EAR en cola (edges/remaining).
    /// No anticipar por bloque “queued” sin edges: eso dejaba el host en warp ×64
    /// con el juego ya en marcha (PlayStandardBlocksViaEar + PC en RAM).
    /// </summary>
    public bool IsActive =>
        _tape is { IsPlaying: true } && (_remaining > 0 || _edges.Count > 0);

    public void Attach(TapeImage tape)
    {
        _tape = tape;
        Seek(Math.Max(0, tape.Position));
    }

    public void Seek(int blockIndex)
    {
        if (_tape == null) return;
        blockIndex = Math.Clamp(blockIndex, 0, _tape.Blocks.Count);
        _playPos = blockIndex;
        _tape.Position = blockIndex;
        _edges.Clear();
        _remaining = 0;
        _level = false;
        _blockQueued = false;
        EnsureQueued();
    }

    public void SyncPositionFromTape()
    {
        if (_tape == null) return;
        if (_tape.Position != _playPos)
        {
            // Hacia delante (trampa ROM) o atrás (REW/Seek): realinear cola de pulsos.
            _playPos = _tape.Position;
            _edges.Clear();
            _remaining = 0;
            _blockQueued = false;
            _level = false;
            EnsureQueued();
        }
    }

    public void Advance(int tStates)
    {
        if (_tape == null || !_tape.IsPlaying || tStates <= 0) return;
        SyncPositionFromTape();

        bool accelerate =
            TurboSpeed > 1 &&
            _blockQueued &&
            _playPos >= 0 &&
            _playPos < _tape.Blocks.Count &&
            (!_tape.Blocks[_playPos].IsRomTrapCandidate || AccelerateRomTrapPulses);

        if (accelerate)
            tStates *= TurboSpeed;

        int left = tStates;
        while (left > 0)
        {
            if (_remaining <= 0)
            {
                if (_edges.Count == 0)
                {
                    if (_blockQueued)
                    {
                        _playPos++;
                        if (_tape.Position < _playPos)
                            _tape.Position = _playPos;
                        _blockQueued = false;
                    }
                    if (!EnsureQueued())
                    {
                        _level = false;
                        return;
                    }
                }
                if (_edges.Count == 0) return;
                var (dur, level) = _edges.Dequeue();
                _remaining = Math.Max(1, dur);
                _level = level;
            }

            int step = Math.Min(left, _remaining);
            _remaining -= step;
            left -= step;
        }
    }

    private bool EnsureQueued()
    {
        if (_tape == null) return false;

        while (_playPos >= 0 && _playPos < _tape.Blocks.Count)
        {
            var b = _tape.Blocks[_playPos];

            // Como Fuse: bloques ROM → trampa LD-BYTES, salvo cargador en RAM
            // que lee por EAR (Flying Shark .tap y similares).
            if (b.IsRomTrapCandidate && !PlayStandardBlocksViaEar)
            {
                if (_tape.Position > _playPos)
                {
                    _playPos++;
                    continue;
                }
                // Esperar a que LD-BYTES avance Position.
                return false;
            }

            _edges.Clear();
            QueueBlock(b);
            _blockQueued = true;
            return _edges.Count > 0;
        }
        return false;
    }

    private void QueueBlock(TapeBlock b)
    {
        bool level = _level;
        void Edge(int dur, bool nextLevel)
        {
            level = nextLevel;
            _edges.Enqueue((Math.Max(1, dur), level));
        }
        void Toggle(int dur)
        {
            Edge(dur, !level);
        }

        switch (b.Kind)
        {
            case TapeBlockKind.PureTone:
                // Fuse/libspectrum: pulsos alternos del tono.
                for (int i = 0; i < b.ToneCount; i++)
                    Toggle(b.TonePulse);
                break;

            case TapeBlockKind.PulseSequence:
                foreach (int p in b.Pulses)
                    Toggle(p);
                break;

            case TapeBlockKind.PureData:
                level = QueueDataBits(b, includePilot: false, level);
                break;

            default:
                level = QueueDataBits(b, includePilot: true, level);
                break;
        }

        _level = level;
    }

    private bool QueueDataBits(TapeBlock b, bool includePilot, bool level)
    {
        bool Toggle(int dur)
        {
            level = !level;
            _edges.Enqueue((Math.Max(1, dur), level));
            return level;
        }

        if (includePilot && b.PilotCount > 0)
        {
            for (int i = 0; i < b.PilotCount; i++)
                Toggle(b.PilotPulse);
            Toggle(b.Sync1);
            Toggle(b.Sync2);
        }

        for (int bi = 0; bi < b.Data.Length; bi++)
        {
            int bits = bi == b.Data.Length - 1 ? b.UsedBitsLast : 8;
            if (bits <= 0) bits = 8;
            byte val = b.Data[bi];
            for (int bit = 7; bit >= 8 - bits; bit--)
            {
                int pulse = ((val >> bit) & 1) != 0 ? b.OnePulse : b.ZeroPulse;
                Toggle(pulse);
                Toggle(pulse);
            }
        }

        // Pausa = silencio (nivel bajo), estilo Fuse — sin toggle espurio.
        if (b.PauseMs > 0)
        {
            level = false;
            _edges.Enqueue((Math.Max(1, b.PauseMs * 3500), false));
        }

        return level;
    }
}
