using System.Text.Json;
using System.Text.Json.Serialization;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Media;

/// <summary>
/// Registro durable de soluciones técnicas de carga en
/// <c>{repo}/assets/catalog/{platform}/load-solutions.json</c>.
/// Sobrevive a <see cref="AppPaths.ClearLocalCache"/>. Se aplica antes del ranking Auto.
/// </summary>
public static class LoadSolutionsStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static LoadSolutionsIndex _index = new();
    private static bool _loaded;
    private static EmulatorPlatform _platform = EmulatorPlatform.ZxSpectrum;

    public static EmulatorPlatform Platform
    {
        get { lock (Gate) return _platform; }
    }

    public static void EnsureLoaded(EmulatorPlatform? platform = null)
    {
        lock (Gate)
        {
            if (platform is EmulatorPlatform p && p != _platform)
            {
                _platform = p;
                _loaded = false;
            }

            if (!_loaded)
                LoadUnlocked();
        }
    }

    public static void Reload(EmulatorPlatform? platform = null)
    {
        lock (Gate)
        {
            if (platform is EmulatorPlatform p)
                _platform = p;
            LoadUnlocked();
        }
    }

    public static string KeyFor(CatalogEntry entry)
    {
        string platformId = string.IsNullOrWhiteSpace(entry.Platform)
            ? EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum)
            : entry.Platform.Trim();
        string local;
        if (entry.ScId is int id and > 0)
            local = "sc:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        else if (!string.IsNullOrWhiteSpace(entry.Id))
            local = "id:" + entry.Id.Trim();
        else
            local = "title:" + LocalJuegosMatcher.NormalizeKey(entry.Title ?? "");

        return platformId + ":" + local;
    }

    public static string KeyForTitleStem(string titleOrStem, EmulatorPlatform? platform = null)
    {
        string platformId = EmulatorPlatformInfo.Id(platform ?? _platform);
        string stem = LocalJuegosMatcher.NormalizeKey(titleOrStem);
        return platformId + ":title:" + stem;
    }

    /// <summary>
    /// Resuelve solución por ficha de catálogo y/o ruta local (stem),
    /// más estrategias de patrón aplicables al medio primario.
    /// </summary>
    public static LoadSolutionHint Resolve(
        CatalogEntry? entry = null,
        string? path = null,
        EmulatorPlatform? platform = null)
    {
        lock (Gate)
        {
            if (platform is EmulatorPlatform p && p != _platform)
            {
                _platform = p;
                _loaded = false;
            }

            if (!_loaded)
                LoadUnlocked();

            LoadSolution? solution = null;
            string? key = null;

            if (entry != null)
            {
                key = KeyFor(entry);
                if (_index.Solutions.TryGetValue(key, out var byEntry))
                    solution = byEntry;
                else
                {
                    string legacy = LegacyKeyFor(entry);
                    if (!string.IsNullOrEmpty(legacy) && _index.Solutions.TryGetValue(legacy, out byEntry))
                    {
                        solution = byEntry;
                        key = legacy;
                    }
                }

                if (solution == null)
                {
                    foreach (string stem in LocalJuegosMatcher.EnumerateMatchStems(entry))
                    {
                        string titleKey = KeyForTitleStem(stem);
                        if (_index.Solutions.TryGetValue(titleKey, out byEntry))
                        {
                            solution = byEntry;
                            key = titleKey;
                            break;
                        }

                        var byStem = FindByTitleStemUnlocked(stem);
                        if (byStem != null)
                        {
                            solution = byStem.Value.solution;
                            key = byStem.Value.key;
                            break;
                        }
                    }
                }
            }

            if (solution == null && !string.IsNullOrWhiteSpace(path))
            {
                string fileStem = LocalJuegosMatcher.StripKnownExtension(Path.GetFileName(path));
                string norm = LocalJuegosMatcher.NormalizeKey(fileStem);
                if (!string.IsNullOrEmpty(norm))
                {
                    var hit = FindByTitleStemUnlocked(norm)
                              ?? FindByLocalFileHintUnlocked(Path.GetFileName(path));
                    if (hit != null)
                    {
                        solution = hit.Value.solution;
                        key = hit.Value.key;
                    }
                }
            }

            var patternTags = MatchPatternStrategiesUnlocked(path);
            return new LoadSolutionHint
            {
                Solution = solution,
                ResolvedKey = key,
                PatternStrategies = patternTags
            };
        }
    }

    public static LoadSolution? TryGet(CatalogEntry entry)
    {
        var hint = Resolve(entry);
        return hint.Solution;
    }

    /// <summary>
    /// Si hay solución con formato preferido y el usuario pidió Auto, usa el del registro.
    /// </summary>
    public static RomMediaFormat ApplyPreferredFormat(RomMediaFormat requested, LoadSolutionHint hint)
    {
        if (requested != RomMediaFormat.Auto)
            return requested;
        if (hint.Solution?.PreferredFormat == null)
            return requested;
        return LoadSolutionTags.ParsePreferredFormat(hint.Solution.PreferredFormat);
    }

    /// <summary>
    /// Tras carga Healthy (o StillLoading aceptado): upsert de la solución que funcionó.
    /// </summary>
    public static void RecordSuccessfulPlay(
        CatalogEntry? entry,
        string path,
        LoadStrategyKind strategy,
        LoadSolutionHint? priorHint = null,
        string? preferredSource = null)
    {
        lock (Gate)
        {
            if (!_loaded)
                LoadUnlocked();

            string key;
            LoadSolution meta;

            if (entry != null)
            {
                key = KeyFor(entry);
                meta = GetOrCreateUnlocked(key, entry.Title, entry.ScId, entry);
            }
            else if (priorHint?.ResolvedKey is { } rk && _index.Solutions.TryGetValue(rk, out var existing))
            {
                key = rk;
                meta = existing;
            }
            else
            {
                string stem = LocalJuegosMatcher.StripKnownExtension(Path.GetFileName(path));
                key = KeyForTitleStem(stem);
                meta = GetOrCreateUnlocked(key, stem, null, null);
                meta.TitleStem ??= LocalJuegosMatcher.NormalizeKey(stem);
            }

            if (entry != null)
            {
                if (!string.IsNullOrWhiteSpace(entry.Title))
                    meta.Title = entry.Title;
                if (entry.ScId is int sid and > 0)
                    meta.ScId = sid;
                if (string.IsNullOrWhiteSpace(meta.TitleStem))
                {
                    string? sc = entry.ScFile;
                    meta.TitleStem = LocalJuegosMatcher.NormalizeKey(
                        !string.IsNullOrWhiteSpace(sc) ? sc! : entry.Title);
                }
            }

            string kind = RomMediaFormatInfo.Classify(path);
            if (kind is "tap" or "tzx" or "z80" or "sna")
                meta.PreferredFormat = kind;

            meta.LocalFileHint = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(preferredSource))
                meta.PreferredSource = preferredSource.Trim();

            MergeStrategyTags(meta, strategy, priorHint);

            meta.LastSuccessUtc = DateTimeOffset.UtcNow;
            meta.SuccessCount = Math.Max(0, meta.SuccessCount) + 1;
            _index.Solutions[key] = meta;
            SaveUnlocked();
        }
    }

    private static void MergeStrategyTags(
        LoadSolution meta,
        LoadStrategyKind strategy,
        LoadSolutionHint? priorHint)
    {
        var tags = new List<string>();
        void Add(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return;
            t = t.Trim();
            if (!tags.Contains(t, StringComparer.OrdinalIgnoreCase))
                tags.Add(t);
        }

        foreach (string existing in meta.Strategies)
            Add(existing);

        if (priorHint != null)
        {
            foreach (string t in priorHint.AllStrategies())
                Add(t);
        }

        Add(LoadSolutionTags.FromLoadStrategyKind(strategy));
        Add(LoadSolutionTags.HealthRetrySibling);

        meta.Strategies = tags;
    }

    private static IReadOnlyList<string> MatchPatternStrategiesUnlocked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || _index.Patterns.Count == 0)
            return Array.Empty<string>();

        LoadMediaSignals? signals;
        try { signals = LoadMediaInspect.InspectPath(path); }
        catch { return Array.Empty<string>(); }

        var tags = new List<string>();
        foreach (var pattern in _index.Patterns)
        {
            if (!PatternMatches(pattern, signals))
                continue;
            foreach (string s in pattern.Strategies)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (!tags.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase))
                    tags.Add(s.Trim());
            }
        }

        return tags;
    }

    private static bool PatternMatches(LoadSolutionPattern pattern, LoadMediaSignals signals)
    {
        if (!string.IsNullOrWhiteSpace(pattern.MatchKind)
            && !string.Equals(pattern.MatchKind, signals.Kind, StringComparison.OrdinalIgnoreCase))
            return false;
        if (pattern.MatchHasTurboBlocks is bool turbo && signals.HasTurboBlocks != turbo)
            return false;
        if (pattern.MatchCanFlashToUsr is bool flash && signals.CanFlashToUsr != flash)
            return false;
        if (pattern.MatchLikelyMidLoad is bool mid && signals.LikelyMidLoadSnapshot != mid)
            return false;
        return true;
    }

    private static (string key, LoadSolution solution)? FindByTitleStemUnlocked(string normalizedStem)
    {
        if (string.IsNullOrEmpty(normalizedStem))
            return null;

        foreach (var kv in _index.Solutions)
        {
            string stem = LocalJuegosMatcher.NormalizeKey(kv.Value.TitleStem ?? kv.Value.Title ?? "");
            if (string.Equals(stem, normalizedStem, StringComparison.OrdinalIgnoreCase))
                return (kv.Key, kv.Value);
        }

        return null;
    }

    private static (string key, LoadSolution solution)? FindByLocalFileHintUnlocked(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        foreach (var kv in _index.Solutions)
        {
            if (string.IsNullOrWhiteSpace(kv.Value.LocalFileHint))
                continue;
            if (string.Equals(kv.Value.LocalFileHint, fileName, StringComparison.OrdinalIgnoreCase))
                return (kv.Key, kv.Value);
        }

        return null;
    }

    private static string LegacyKeyFor(CatalogEntry entry)
    {
        if (entry.ScId is int id and > 0)
            return "sc:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(entry.Id))
            return "id:" + entry.Id.Trim();
        return "title:" + LocalJuegosMatcher.NormalizeKey(entry.Title ?? "");
    }

    private static LoadSolution GetOrCreateUnlocked(
        string key,
        string? title,
        int? scId,
        CatalogEntry? entry)
    {
        if (_index.Solutions.TryGetValue(key, out var meta) && meta != null)
            return meta;

        if (entry != null)
        {
            string legacy = LegacyKeyFor(entry);
            if (_index.Solutions.TryGetValue(legacy, out meta) && meta != null)
            {
                _index.Solutions.Remove(legacy);
                return meta;
            }
        }

        return new LoadSolution
        {
            Title = title ?? "",
            ScId = scId is > 0 ? scId : null
        };
    }

    private static void LoadUnlocked()
    {
        _index = new LoadSolutionsIndex();
        _loaded = true;

        string? path = ResolvePath(createDir: false);
        if (path == null || !File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<LoadSolutionsIndex>(json, JsonOpts);
            if (parsed == null)
                return;

            _index.Version = parsed.Version;
            _index.Patterns = parsed.Patterns ?? new List<LoadSolutionPattern>();
            var rebuilt = new Dictionary<string, LoadSolution>(StringComparer.OrdinalIgnoreCase);
            if (parsed.Solutions != null)
            {
                foreach (var kv in parsed.Solutions)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                        rebuilt[kv.Key.Trim()] = kv.Value;
                }
            }

            _index.Solutions = rebuilt;
        }
        catch
        {
            _index = new LoadSolutionsIndex();
        }
    }

    private static void SaveUnlocked()
    {
        string? path = ResolvePath(createDir: true);
        if (path == null)
            return;

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_index, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Disco lleno / sin permiso: no tumbar el play.
        }
    }

    private static string? ResolvePath(bool createDir)
    {
        string? dir = createDir
            ? ProjectCatalogPaths.TryEnsureCatalogDir(_platform)
            : ProjectCatalogPaths.TryGetCatalogDir(_platform);

        if (dir != null)
            return Path.Combine(dir, ProjectCatalogPaths.LoadSolutionsFileName);

        AppPaths.EnsureCreated();
        return Path.Combine(AppPaths.Root, ProjectCatalogPaths.LoadSolutionsFileName);
    }
}
