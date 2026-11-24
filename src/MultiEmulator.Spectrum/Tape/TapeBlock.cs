namespace MultiEmulator.Spectrum.Tape;

public enum TapeBlockKind
{
    /// <summary>Datos estándar/turbo/TAP — trampa ROM LD-BYTES.</summary>
    Data,
    /// <summary>TZX 0x12 — tono puro (piloto turbo).</summary>
    PureTone,
    /// <summary>TZX 0x13 — secuencia de pulsos.</summary>
    PulseSequence,
    /// <summary>TZX 0x14 — solo bits (sin piloto); EAR.</summary>
    PureData
}

public sealed class TapeBlock
{
    public TapeBlockKind Kind { get; init; } = TapeBlockKind.Data;
    public byte Flag { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    /// <summary>ID de bloque TZX (0x10/0x11/0x12/0x13/0x14) o 0 para TAP.</summary>
    public byte TzxId { get; init; }

    public int PilotPulse { get; init; } = 2168;
    public int PilotCount { get; init; } = 8063;
    public int Sync1 { get; init; } = 667;
    public int Sync2 { get; init; } = 735;
    public int ZeroPulse { get; init; } = 855;
    public int OnePulse { get; init; } = 1710;
    public int PauseMs { get; init; }
    public int UsedBitsLast { get; init; } = 8;

    /// <summary>0x12: duración de un pulso del tono.</summary>
    public int TonePulse { get; init; }
    /// <summary>0x12: número de pulsos.</summary>
    public int ToneCount { get; init; }
    /// <summary>0x13: lista de duraciones.</summary>
    public int[] Pulses { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Trampa LD-BYTES solo para TAP (TzxId=0) y TZX 0x10.
    /// 0x11/0x12/0x13/0x14 van por EAR (Fuse/ZEsarUX/SpecEmu/RVM).
    /// Los TAP huérfanos los puede leer un cargador en RAM vía EAR si
    /// <see cref="TapePulsePlayer.PlayStandardBlocksViaEar"/> está activo.
    /// </summary>
    public bool IsRomTrapCandidate =>
        Kind == TapeBlockKind.Data && TzxId is 0 or 0x10;
}
