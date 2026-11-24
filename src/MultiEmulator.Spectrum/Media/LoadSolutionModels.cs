using System.Text.Json.Serialization;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Media;

/// <summary>Etiquetas de estrategia técnica persistidas en el registro de soluciones.</summary>
public static class LoadSolutionTags
{
    public const string InstantFlashIffOn = "InstantFlashIffOn";
    /// <summary>Alias histórico de <see cref="InstantFlashIffOn"/>.</summary>
    public const string FlashLoadIff = "FlashLoadIff";
    public const string FastLoadWarp = "FastLoadWarp";
    public const string FloatingBus = "FloatingBus";
    /// <summary>Arkanoid: un solo flanco paper/frame en floating bus (evita ~2–3× sync).</summary>
    public const string FloatingBusVsyncLatch = "FloatingBusVsyncLatch";
    public const string BioToxinHle = "BioToxinHle";
    public const string DeferMidLoadZ80 = "DeferMidLoadZ80";
    /// <summary>Alias histórico de <see cref="DeferMidLoadZ80"/>.</summary>
    public const string DeferSoftLibZ80 = "DeferSoftLibZ80";
    public const string PreferTapWhenTzxNA = "PreferTapWhenTzxNA";
    public const string PreferTzx = "PreferTzx";
    public const string ForceTap = "ForceTap";
    public const string ForceTzx = "ForceTzx";
    public const string TurboIrqPolicy = "TurboIrqPolicy";
    public const string HealthRetrySibling = "HealthRetrySibling";
    public const string ForceMachine48 = "ForceMachine48";
    public const string ForceMachine128 = "ForceMachine128";
    public const string UlaIrqContentionGeneral = "UlaIrqContentionGeneral";
    public const string MediaOnly = "MediaOnly";

    public static bool IsInstantFlashIff(string? tag) =>
        EqualsTag(tag, InstantFlashIffOn) || EqualsTag(tag, FlashLoadIff);

    public static bool IsDeferMidLoadZ80(string? tag) =>
        EqualsTag(tag, DeferMidLoadZ80) || EqualsTag(tag, DeferSoftLibZ80);

    public static LoadStrategyKind? ToLoadStrategyKind(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim();
        if (EqualsTag(t, PreferTzx)) return LoadStrategyKind.PreferTzx;
        if (EqualsTag(t, ForceTap) || EqualsTag(t, PreferTapWhenTzxNA))
            return LoadStrategyKind.ForceTap;
        if (EqualsTag(t, ForceTzx)) return LoadStrategyKind.ForceTzx;
        if (IsDeferMidLoadZ80(t)) return LoadStrategyKind.SkipSnapshot;
        return null;
    }

    public static string FromLoadStrategyKind(LoadStrategyKind kind) => kind switch
    {
        LoadStrategyKind.PreferTzx => PreferTzx,
        LoadStrategyKind.ForceTap => ForceTap,
        LoadStrategyKind.ForceTzx => ForceTzx,
        LoadStrategyKind.SkipSnapshot => DeferMidLoadZ80,
        _ => kind.ToString()
    };

    public static RomMediaFormat ParsePreferredFormat(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred))
            return RomMediaFormat.Auto;
        return preferred.Trim().ToLowerInvariant() switch
        {
            "tap" => RomMediaFormat.Tap,
            "tzx" => RomMediaFormat.Tzx,
            "z80" => RomMediaFormat.Z80,
            "sna" => RomMediaFormat.Sna,
            _ => RomMediaFormat.Auto
        };
    }

    private static bool EqualsTag(string? a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Regla por patrón de medio (turbo TZX, TAP flashable, Z80 mid-load…).</summary>
public sealed class LoadSolutionPattern
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("matchKind")] public string? MatchKind { get; set; }
    [JsonPropertyName("matchHasTurboBlocks")] public bool? MatchHasTurboBlocks { get; set; }
    [JsonPropertyName("matchCanFlashToUsr")] public bool? MatchCanFlashToUsr { get; set; }
    [JsonPropertyName("matchLikelyMidLoad")] public bool? MatchLikelyMidLoad { get; set; }
    [JsonPropertyName("strategies")] public List<string> Strategies { get; set; } = new();
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

/// <summary>Solución técnica conocida para un juego (formato, fuente, estrategias).</summary>
public sealed class LoadSolution
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("scId")] public int? ScId { get; set; }
    [JsonPropertyName("titleStem")] public string? TitleStem { get; set; }
    /// <summary>tap | tzx | z80 | sna | auto</summary>
    [JsonPropertyName("preferredFormat")] public string? PreferredFormat { get; set; }
    /// <summary>tosec | spectrum-computing | software-library | juegos | other</summary>
    [JsonPropertyName("preferredSource")] public string? PreferredSource { get; set; }
    [JsonPropertyName("localFileHint")] public string? LocalFileHint { get; set; }
    [JsonPropertyName("strategies")] public List<string> Strategies { get; set; } = new();
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("lastSuccessUtc")] public DateTimeOffset? LastSuccessUtc { get; set; }
    [JsonPropertyName("successCount")] public int SuccessCount { get; set; }
}

public sealed class LoadSolutionsIndex
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("patterns")] public List<LoadSolutionPattern> Patterns { get; set; } = new();
    [JsonPropertyName("solutions")] public Dictionary<string, LoadSolution> Solutions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Pista resuelta para OpenFile / OpenCatalog (juego + patrones).</summary>
public sealed class LoadSolutionHint
{
    public LoadSolution? Solution { get; init; }
    public string? ResolvedKey { get; init; }
    public IReadOnlyList<string> PatternStrategies { get; init; } = Array.Empty<string>();

    public IEnumerable<string> AllStrategies()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Solution?.Strategies != null)
        {
            foreach (string s in Solution.Strategies)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (seen.Add(s.Trim()))
                    yield return s.Trim();
            }
        }

        foreach (string s in PatternStrategies)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (seen.Add(s.Trim()))
                yield return s.Trim();
        }
    }

    public bool HasTag(string tag) =>
        AllStrategies().Any(s => string.Equals(s, tag, StringComparison.OrdinalIgnoreCase));

    public bool HasInstantFlashIff =>
        AllStrategies().Any(LoadSolutionTags.IsInstantFlashIff);

    public bool HasDeferMidLoadZ80 =>
        AllStrategies().Any(LoadSolutionTags.IsDeferMidLoadZ80);
}
