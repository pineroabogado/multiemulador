using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Media;

/// <summary>Candidato de carga con ranking de salud (preflight).</summary>
public sealed class LoadCandidate
{
    public string Path { get; init; } = "";
    public LoadStrategyKind Strategy { get; init; }
    public RomMediaFormat Format { get; init; }
    public LoadMediaSignals? Signals { get; init; }
    public int Score { get; init; }
    public string Label
    {
        get
        {
            // Path es propiedad: no usar System.IO.Path aquí (shadowing).
            string name = FileNameOf(Path);
            return $"{LoadPolicies.StrategyDisplayName(Strategy)} · {name}";
        }
    }

    private static string FileNameOf(string filePath) => System.IO.Path.GetFileName(filePath);
}

/// <summary>
/// Ranking de candidatos locales / hermanos: TZX limpio &gt; TAP &gt; Z80 completo;
/// demota Soft Library mid-load y TAP BioToxin-like cuando hay TZX.
/// </summary>
public static class LoadCandidateRanker
{
    /// <summary>
    /// Construye la lista ordenada de intentos a partir de una ruta primaria
    /// y opcionales hermanos en el mismo directorio.
    /// </summary>
    public static IReadOnlyList<LoadCandidate> BuildAttempts(
        string primaryPath,
        RomMediaFormat format = RomMediaFormat.Auto,
        IEnumerable<string>? extraPaths = null,
        bool inspect = true,
        LoadSolutionHint? solutionHint = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<LoadCandidate>();
        var preferredKinds = PreferredStrategyKinds(solutionHint);
        string? localHint = solutionHint?.Solution?.LocalFileHint;

        void Add(string path, LoadStrategyKind strategy, RomMediaFormat fmt, int bonus = 0)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            string full = Path.GetFullPath(path);
            string key = full + "|" + strategy + "|" + fmt;
            if (!seen.Add(key))
                return;

            LoadMediaSignals? signals = null;
            int score = 0;
            if (inspect)
            {
                try
                {
                    signals = LoadMediaInspect.InspectPath(full);
                    score = signals.HealthScore;
                }
                catch
                {
                    signals = null;
                    score = RomMediaFormatInfo.UrlScore(RomMediaFormat.Auto, full);
                }
            }
            else
            {
                score = RomMediaFormatInfo.UrlScore(RomMediaFormat.Auto, full);
            }

            // Nudge por preferencia del usuario (sin aplastar la salud del medio).
            score += RomMediaFormatInfo.UrlScore(format, full) / 2;
            // FlashLoad→USR viable (señal estática): priorizar frente a TZX turbo sin flash.
            if (signals is { CanFlashToUsr: true })
                score += 30;
            if (signals is { CanFlashToUsr: false, Kind: "tzx" })
                score -= 20;

            if (signals is { LikelyMidLoadSnapshot: true } && format is RomMediaFormat.Auto or RomMediaFormat.Tap or RomMediaFormat.Tzx)
                score -= 50;

            // Registro de soluciones: aplicar antes del ranking genérico Auto.
            if (preferredKinds.Contains(strategy))
                score += 80;
            if (!string.IsNullOrWhiteSpace(localHint)
                && string.Equals(Path.GetFileName(full), localHint, StringComparison.OrdinalIgnoreCase))
                score += 120;
            if (solutionHint is { HasDeferMidLoadZ80: true }
                && signals is { LikelyMidLoadSnapshot: true })
                score -= 100;

            list.Add(new LoadCandidate
            {
                Path = full,
                Strategy = strategy,
                Format = fmt,
                Signals = signals,
                Score = score + bonus
            });
        }

        // 1) Primario según preferencia.
        // Bonus moderado: la salud estática (CanFlashToUsr) debe poder ganar al primario.
        if (format == RomMediaFormat.Tap)
            Add(primaryPath, LoadStrategyKind.ForceTap, RomMediaFormat.Tap, bonus: 20);
        else if (format == RomMediaFormat.Tzx)
            Add(primaryPath, LoadStrategyKind.ForceTzx, RomMediaFormat.Tzx, bonus: 20);
        else
            Add(primaryPath, LoadStrategyKind.Default, RomMediaFormat.Auto, bonus: 12);

        // 2) Hermanos TZX/TAP explícitos.
        foreach (string sibling in EnumerateTapeSiblings(primaryPath))
        {
            string kind = RomMediaFormatInfo.Classify(sibling);
            if (kind == "tzx" && format != RomMediaFormat.Tap)
                Add(sibling, LoadStrategyKind.PreferTzx, RomMediaFormat.Auto, bonus: 15);
            else if (kind == "tap" && format != RomMediaFormat.Tzx)
                Add(sibling, LoadStrategyKind.ForceTap, RomMediaFormat.Tap, bonus: 5);
            else
                Add(sibling, LoadStrategyKind.SiblingTape, RomMediaFormat.Auto);
        }

        // 3) Extra paths (p.ej. otros matches de JUEGOS).
        if (extraPaths != null)
        {
            foreach (string p in extraPaths)
            {
                string kind = RomMediaFormatInfo.Classify(p);
                var strategy = kind is "z80" or "sna"
                    ? LoadStrategyKind.CascadeNext
                    : LoadStrategyKind.SiblingTape;
                // En Auto, snapshots mid-load van al final (SkipSnapshot semántica).
                if (kind is "z80" or "sna" && format is RomMediaFormat.Auto or RomMediaFormat.Tap or RomMediaFormat.Tzx)
                    Add(p, LoadStrategyKind.SkipSnapshot, format, bonus: -10);
                else
                    Add(p, strategy, format);
            }
        }

        // 4) Si el primario es snapshot, forzar intento de cinta hermana primero ya está;
        // añadir reintento ForceTap del primario si era TZX preferido vía MediaLoader.
        string primaryKind = RomMediaFormatInfo.Classify(primaryPath);
        if (format == RomMediaFormat.Auto && primaryKind == "tap")
            Add(primaryPath, LoadStrategyKind.ForceTap, RomMediaFormat.Tap, bonus: -5);
        if (format == RomMediaFormat.Auto && primaryKind is "tzx" or "tap")
        {
            // Ya cubierto; si falló PreferTzx, ForceTap del .tap hermano.
        }

        return list
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Strategy)
            .ToList();
    }

    private static HashSet<LoadStrategyKind> PreferredStrategyKinds(LoadSolutionHint? hint)
    {
        var set = new HashSet<LoadStrategyKind>();
        if (hint == null)
            return set;

        foreach (string tag in hint.AllStrategies())
        {
            var kind = LoadSolutionTags.ToLoadStrategyKind(tag);
            if (kind is LoadStrategyKind k)
                set.Add(k);
        }

        return set;
    }

    /// <summary>Hermanos .tzx/.tap (y .zip) en el mismo directorio.</summary>
    public static IEnumerable<string> EnumerateTapeSiblings(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            yield break;

        string stem = LocalJuegosMatcher.StripKnownExtension(Path.GetFileName(path));
        if (string.IsNullOrEmpty(stem))
            yield break;

        string[] patterns =
        [
            stem + ".tzx.zip", stem + ".tzx",
            stem + ".tap.zip", stem + ".tap",
            stem + ".z80.zip", stem + ".z80",
            stem + ".sna.zip", stem + ".sna"
        ];

        string fullPrimary = Path.GetFullPath(path);
        foreach (string name in patterns)
        {
            string candidate = Path.Combine(dir, name);
            if (!File.Exists(candidate))
                continue;
            if (string.Equals(Path.GetFullPath(candidate), fullPrimary, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return candidate;
        }
    }
}
