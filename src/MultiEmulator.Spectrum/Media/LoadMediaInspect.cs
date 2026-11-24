using MultiEmulator.Spectrum.Services;
using MultiEmulator.Spectrum.Tape;

namespace MultiEmulator.Spectrum.Media;

/// <summary>Señales de un medio inspeccionado sin emular (preflight).</summary>
public sealed class LoadMediaSignals
{
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "other"; // tap/tzx/z80/sna/…
    public bool HasTurboBlocks { get; init; }
    public bool RequiresInterpreter { get; init; }
    /// <summary>BASIC con USR y sin POKE/DATA previos → FlashLoad+USR viable (Spectaculator).</summary>
    public bool CanFlashToUsr { get; init; }
    public bool LikelyMidLoadSnapshot { get; init; }
    public ushort? SnapshotPc { get; init; }
    public bool? SnapshotIff1 { get; init; }
    /// <summary>Mayor = más saludable para Auto.</summary>
    public int HealthScore { get; init; }
}

/// <summary>
/// Inspección estática de cintas/snapshots para ranking y preflight.
/// Heurísticas generales (no por título).
/// </summary>
public static class LoadMediaInspect
{
    // ROM LD-BYTES / edge-load típicos de captura a mitad de carga.
    private const ushort LdBytesLo = 0x0556;
    private const ushort LdBytesHi = 0x0605;
    private const ushort SaveBytesLo = 0x04C2;
    private const ushort SaveBytesHi = 0x053E;

    public static LoadMediaSignals InspectPath(string path)
    {
        string kind = RomMediaFormatInfo.Classify(path);
        try
        {
            if (ArchiveMedia.IsArchive(path)
                || path.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".z80.zip", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".sna.zip", StringComparison.OrdinalIgnoreCase))
            {
                var (name, data) = ArchiveMedia.ExtractPreferredMediaDeep(path);
                return InspectBytes(path, name, data);
            }

            byte[] file = File.ReadAllBytes(path);
            return InspectBytes(path, Path.GetFileName(path), file);
        }
        catch
        {
            return new LoadMediaSignals
            {
                Path = path,
                Kind = kind,
                HealthScore = RomMediaFormatInfo.UrlScore(RomMediaFormat.Auto, path) - 20
            };
        }
    }

    public static LoadMediaSignals InspectBytes(string path, string fileName, byte[] data)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        string kind = ext switch
        {
            ".tap" => "tap",
            ".tzx" => "tzx",
            ".z80" => "z80",
            ".sna" => "sna",
            ".trd" => "trd",
            ".dsk" => "dsk",
            _ => RomMediaFormatInfo.Classify(path)
        };

        bool turbo = false;
        bool needsInterpreter = false;
        bool canFlashUsr = false;
        bool midLoad = false;
        ushort? snapPc = null;
        bool? snapIff = null;

        if (kind == "tap")
        {
            var tape = TapeImage.LoadTap(data);
            turbo = TapeHasTurboBlocks(tape);
            needsInterpreter = BasicRequiresInterpreter(tape);
            // FlashLoad→USR (Spectaculator): BASIC con USR y sin POKE/DATA previos.
            canFlashUsr = !needsInterpreter && BasicHasUsr(tape);
        }
        else if (kind == "tzx")
        {
            var tape = TapeImage.LoadTzx(data);
            turbo = TapeHasTurboBlocks(tape);
            needsInterpreter = BasicRequiresInterpreter(tape);
            // USR en bloques 0x10: se puede flashear aunque quede turbo EAR después
            // (Flying Shark). Si no hay USR flasheable, demotar frente a TAP hermano.
            canFlashUsr = !needsInterpreter && BasicHasUsr(tape);
        }
        else if (kind == "z80")
        {
            if (TryPeekZ80(data, out ushort pc, out bool iff1))
            {
                snapPc = pc;
                snapIff = iff1;
                midLoad = LooksLikeMidLoadPc(pc, iff1);
            }
        }
        else if (kind == "sna")
        {
            if (TryPeekSna(data, out ushort pc, out bool iff1))
            {
                snapPc = pc;
                snapIff = iff1;
                midLoad = LooksLikeMidLoadPc(pc, iff1);
            }
        }

        int score = RomMediaFormatInfo.UrlScore(RomMediaFormat.Auto, fileName);
        if (kind is "tap" or "tzx")
        {
            if (turbo) score += 8; // Fast Load cubre EAR turbo
            if (needsInterpreter && kind == "tap") score -= 20; // BioToxin-like → TZX hermano
            // Spectaculator FlashLoad+USR manda sobre TZX sin USR flasheable (Arkanoid.tap).
            if (canFlashUsr) score += 60;
            else if (kind == "tzx") score -= 40;
        }
        else if (kind is "z80" or "sna")
        {
            if (midLoad) score -= 40; // Soft Library mid-load: peor que cinta
            else score -= 5; // cinta suele ser más fiable en Auto
        }

        return new LoadMediaSignals
        {
            Path = path,
            Kind = kind,
            HasTurboBlocks = turbo,
            RequiresInterpreter = needsInterpreter,
            CanFlashToUsr = canFlashUsr,
            LikelyMidLoadSnapshot = midLoad,
            SnapshotPc = snapPc,
            SnapshotIff1 = snapIff,
            HealthScore = score
        };
    }

    public static bool TapeHasTurboBlocks(TapeImage tape)
    {
        foreach (var b in tape.Blocks)
        {
            if (b.Kind is TapeBlockKind.PureTone or TapeBlockKind.PulseSequence or TapeBlockKind.PureData)
                return true;
            if (b.TzxId is 0x11 or 0x12 or 0x13 or 0x14)
                return true;
            // Turbo data: pilot/zero/one fuera de estándar ROM.
            if (b.Kind == TapeBlockKind.Data && b.TzxId == 0x11)
                return true;
            if (b.Kind == TapeBlockKind.Data
                && b.TzxId != 0
                && b.TzxId != 0x10
                && (b.PilotPulse != 2168 || b.ZeroPulse != 855 || b.OnePulse != 1710))
                return true;
        }
        return false;
    }

    public static bool LooksLikeMidLoadPc(ushort pc, bool iff1)
    {
        // Dentro de LD-BYTES / SAVE-BYTES ROM → captura a mitad de carga.
        if (pc is >= LdBytesLo and <= LdBytesHi)
            return true;
        if (pc is >= SaveBytesLo and <= SaveBytesHi)
            return true;
        // ROM de carga con IFF off y PC bajo: típico mid-load sin cinta.
        if (!iff1 && pc < 0x4000 && pc is >= 0x04C2 and <= 0x0800)
            return true;
        return false;
    }

    public static bool TryPeekZ80(byte[] data, out ushort pc, out bool iff1)
    {
        pc = 0;
        iff1 = false;
        if (data.Length < 30)
            return false;

        pc = (ushort)(data[6] | (data[7] << 8));
        iff1 = data[27] != 0;
        if (pc == 0 && data.Length >= 34)
        {
            // v2/v3
            pc = (ushort)(data[32] | (data[33] << 8));
            iff1 = data[27] != 0;
        }

        return true;
    }

    public static bool TryPeekSna(byte[] data, out ushort pc, out bool iff1)
    {
        pc = 0;
        iff1 = false;
        if (data.Length < 27)
            return false;

        iff1 = (data[19] & 0x04) != 0;
        if (data.Length >= 49181)
        {
            // 128K SNA: PC explícito
            pc = (ushort)(data[49179] | (data[49180] << 8));
            return true;
        }

        // 48K: PC en stack (no leemos RAM aquí; aproximamos SP+0 desconocido).
        // Sin dump completo no podemos RETN; marcar desconocido.
        return false;
    }

    private static bool BasicRequiresInterpreter(TapeImage tape)
    {
        for (int i = 0; i + 1 < tape.Blocks.Count; i++)
        {
            var h = tape.Blocks[i];
            var d = tape.Blocks[i + 1];
            if (h.Flag != 0x00 || h.Data.Length < 19 || h.Data[1] != 0)
                continue;
            if (d.Flag != 0xFF || d.Data.Length < 2)
                continue;
            if (TapeLoader.BasicRequiresInterpreterBeforeUsr(d.Data))
                return true;
        }
        return false;
    }

    /// <summary>True si algún Program ROM-trap contiene token USR (FlashLoad→USR).</summary>
    private static bool BasicHasUsr(TapeImage tape)
    {
        for (int i = 0; i + 1 < tape.Blocks.Count; i++)
        {
            var h = tape.Blocks[i];
            var d = tape.Blocks[i + 1];
            if (!h.IsRomTrapCandidate || !d.IsRomTrapCandidate)
                continue;
            if (h.Flag != 0x00 || h.Data.Length < 19 || h.Data[1] != 0)
                continue;
            if (d.Flag != 0xFF || d.Data.Length < 2)
                continue;
            if (TapeLoader.BasicContainsUsr(d.Data))
                return true;
        }
        return false;
    }
}
