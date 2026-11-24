using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiEmulator.Spectrum.Services;

/// <summary>URLs de media conocidas en la ficha (scrape) al momento de jugar.</summary>
public sealed class CatalogGameMediaUrls
{
    public string? LoadScreenUrl { get; set; }
    public string? InGameUrl { get; set; }
    public string? CoverUrl { get; set; }
    public List<string>? AdditionalMediaUrls { get; set; }
}

/// <summary>Metadatos durables de un juego del catálogo (URLs que funcionaron, media SC, ids IA).</summary>
public sealed class CatalogGameMetadata
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("scId")] public int? ScId { get; set; }
    [JsonPropertyName("scSlug")] public string? ScSlug { get; set; }
    [JsonPropertyName("scFile")] public string? ScFile { get; set; }
    [JsonPropertyName("entryUrl")] public string? EntryUrl { get; set; }
    [JsonPropertyName("workingDownloadUrl")] public string? WorkingDownloadUrl { get; set; }
    [JsonPropertyName("workingDownloadUrls")] public List<string> WorkingDownloadUrls { get; set; } = new();
    [JsonPropertyName("format")] public string? Format { get; set; }
    /// <summary>spectrum-computing | software-library | tosec | other</summary>
    [JsonPropertyName("downloadSource")] public string? DownloadSource { get; set; }
    [JsonPropertyName("softLibraryIdentifier")] public string? SoftLibraryIdentifier { get; set; }
    [JsonPropertyName("tosecFileName")] public string? TosecFileName { get; set; }
    /// <summary>Estrategia de carga que pasó el health-check (p.ej. preferir TZX).</summary>
    [JsonPropertyName("successfulLoadStrategy")] public string? SuccessfulLoadStrategy { get; set; }
    [JsonPropertyName("loadScreenUrl")] public string? LoadScreenUrl { get; set; }
    [JsonPropertyName("inGameUrl")] public string? InGameUrl { get; set; }
    [JsonPropertyName("coverUrl")] public string? CoverUrl { get; set; }
    [JsonPropertyName("additionalMediaUrls")] public List<string> AdditionalMediaUrls { get; set; } = new();
    [JsonPropertyName("lastPlayedUtc")] public DateTimeOffset? LastPlayedUtc { get; set; }
    [JsonPropertyName("updatedUtc")] public DateTimeOffset? UpdatedUtc { get; set; }
}

public sealed class CatalogGameMetadataIndex
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("games")] public Dictionary<string, CatalogGameMetadata> Games { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Persistencia de metadatos por juego en
/// <c>{repo}/assets/catalog/{platform}/games-metadata.json</c>.
/// Sobrevive a <see cref="AppPaths.ClearLocalCache"/>.
/// </summary>
public static class CatalogGameMetadataStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static CatalogGameMetadataIndex _index = new();
    private static bool _loaded;
    private static EmulatorPlatform _platform = EmulatorPlatform.ZxSpectrum;

    public static EmulatorPlatform Platform
    {
        get { lock (Gate) return _platform; }
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
            local = "title:" + (entry.Title ?? "").Trim();

        // Prefijo de plataforma evita colisiones entre sistemas futuros.
        // Claves antiguas sin prefijo siguen resolviéndose en TryGet.
        return platformId + ":" + local;
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

    public static CatalogGameMetadata? TryGet(CatalogEntry entry)
    {
        lock (Gate)
        {
            if (!_loaded)
                LoadUnlocked();
            string key = KeyFor(entry);
            if (_index.Games.TryGetValue(key, out var m))
                return m;

            // Compat: claves pre-plataforma (solo ZX).
            string legacy = LegacyKeyFor(entry);
            if (!string.IsNullOrEmpty(legacy) && _index.Games.TryGetValue(legacy, out m))
                return m;
            return null;
        }
    }

    private static string LegacyKeyFor(CatalogEntry entry)
    {
        if (entry.ScId is int id and > 0)
            return "sc:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(entry.Id))
            return "id:" + entry.Id.Trim();
        return "title:" + (entry.Title ?? "").Trim();
    }

    public static CatalogGameMetadata? TryGetByKey(string key)
    {
        lock (Gate)
        {
            if (!_loaded)
                LoadUnlocked();
            return _index.Games.TryGetValue(key, out var m) ? m : null;
        }
    }

    /// <summary>
    /// Aplica URL de descarga conocida y campos SC al <see cref="CatalogEntry"/>
    /// antes de la cascada de descarga.
    /// </summary>
    public static void ApplyToEntry(CatalogEntry entry)
    {
        var meta = TryGet(entry);
        if (meta == null) return;

        if (string.IsNullOrWhiteSpace(entry.Title) && !string.IsNullOrWhiteSpace(meta.Title))
            entry.Title = meta.Title;
        if (entry.ScId is null or <= 0 && meta.ScId is int sid and > 0)
            entry.ScId = sid;
        if (string.IsNullOrWhiteSpace(entry.ScSlug) && !string.IsNullOrWhiteSpace(meta.ScSlug))
            entry.ScSlug = meta.ScSlug;
        if (string.IsNullOrWhiteSpace(entry.ScFile) && !string.IsNullOrWhiteSpace(meta.ScFile))
            entry.ScFile = meta.ScFile;
        if (string.IsNullOrWhiteSpace(entry.EntryUrl) && !string.IsNullOrWhiteSpace(meta.EntryUrl))
            entry.EntryUrl = meta.EntryUrl;
        if (!string.IsNullOrWhiteSpace(meta.WorkingDownloadUrl))
            entry.Url = meta.WorkingDownloadUrl!;
        if (!string.IsNullOrWhiteSpace(meta.Format))
            entry.Format = meta.Format!;
    }

    /// <summary>URLs de descarga a probar primero (la que ya funcionó + historial).</summary>
    public static IReadOnlyList<string> GetPreferredDownloadUrls(CatalogEntry entry)
    {
        var meta = TryGet(entry);
        if (meta == null) return Array.Empty<string>();

        var list = new List<string>();
        void Add(string? u)
        {
            if (string.IsNullOrWhiteSpace(u)) return;
            u = u.Trim();
            if (!list.Contains(u, StringComparer.OrdinalIgnoreCase))
                list.Add(u);
        }

        Add(meta.WorkingDownloadUrl);
        foreach (string u in meta.WorkingDownloadUrls)
            Add(u);
        return list;
    }

    public static IReadOnlyList<string> GetPreferredMediaUrls(CatalogEntry entry, CatalogMediaKind kind)
    {
        var meta = TryGet(entry);
        if (meta == null) return Array.Empty<string>();

        return kind switch
        {
            CatalogMediaKind.LoadScreen => NonEmpty(meta.LoadScreenUrl),
            CatalogMediaKind.InGame => NonEmpty(meta.InGameUrl),
            CatalogMediaKind.Cover => NonEmpty(meta.CoverUrl),
            _ => Array.Empty<string>()
        };
    }

    /// <summary>Tras carga exitosa: guarda metadatos de entrada + URL que funcionó (+ media opcional).</summary>
    public static void RecordSuccessfulPlay(CatalogEntry entry, CatalogGameMediaUrls? media = null) =>
        SaveFromSuccessfulPlay(entry, media);

    /// <summary>Tras OpenPath exitoso: persiste URL/format/ficha SC y media conocida.</summary>
    public static void SaveFromSuccessfulPlay(
        CatalogEntry entry,
        CatalogGameMediaUrls? media = null,
        string? successfulLoadStrategy = null)
    {
        if (string.IsNullOrWhiteSpace(entry.Url) && media == null && string.IsNullOrWhiteSpace(successfulLoadStrategy))
            return;

        lock (Gate)
        {
            EnsureLoadedUnlocked();
            string key = KeyFor(entry);
            var meta = GetOrCreateMetaUnlocked(entry, key);

            MergeEntryFields(meta, entry);

            string? url = entry.Url?.Trim();
            if (!string.IsNullOrWhiteSpace(url))
            {
                meta.WorkingDownloadUrl = url;
                if (!meta.WorkingDownloadUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
                    meta.WorkingDownloadUrls.Insert(0, url);
                while (meta.WorkingDownloadUrls.Count > 8)
                    meta.WorkingDownloadUrls.RemoveAt(meta.WorkingDownloadUrls.Count - 1);

                InferSourceFields(meta, url);
            }

            if (!string.IsNullOrWhiteSpace(entry.Format))
                meta.Format = entry.Format;

            if (!string.IsNullOrWhiteSpace(successfulLoadStrategy))
                meta.SuccessfulLoadStrategy = successfulLoadStrategy.Trim();

            if (media != null)
                MergeKnownMedia(meta, media);

            meta.LastPlayedUtc = DateTimeOffset.UtcNow;
            meta.UpdatedUtc = meta.LastPlayedUtc;
            _index.Games[key] = meta;
            SaveUnlocked();
        }
    }

    private static void MergeKnownMedia(CatalogGameMetadata meta, CatalogGameMediaUrls media)
    {
        if (!string.IsNullOrWhiteSpace(media.LoadScreenUrl))
            meta.LoadScreenUrl = media.LoadScreenUrl.Trim();
        if (!string.IsNullOrWhiteSpace(media.InGameUrl))
            meta.InGameUrl = media.InGameUrl.Trim();
        if (!string.IsNullOrWhiteSpace(media.CoverUrl))
            meta.CoverUrl = media.CoverUrl.Trim();

        if (media.AdditionalMediaUrls == null)
            return;

        foreach (string u in media.AdditionalMediaUrls)
        {
            if (string.IsNullOrWhiteSpace(u)) continue;
            string t = u.Trim();
            if (!meta.AdditionalMediaUrls.Contains(t, StringComparer.OrdinalIgnoreCase))
                meta.AdditionalMediaUrls.Add(t);
        }
    }

    /// <summary>Fusiona URLs de media descubiertas en el scrape de la ficha SC.</summary>
    public static void MergeScrapedMedia(CatalogEntry entry, IReadOnlyList<SpectrumComputingFileItem> scraped)
    {
        if (scraped.Count == 0) return;

        lock (Gate)
        {
            EnsureLoadedUnlocked();
            string key = KeyFor(entry);
            var meta = GetOrCreateMetaUnlocked(entry, key);

            MergeEntryFields(meta, entry);

            foreach (var item in scraped)
            {
                var slot = item.MapToMainSlot();
                string url = item.AbsoluteUrl;
                if (string.IsNullOrWhiteSpace(url)) continue;

                switch (slot)
                {
                    case CatalogMediaKind.LoadScreen:
                        meta.LoadScreenUrl ??= PreferScr(url, item.PreviewUrl);
                        break;
                    case CatalogMediaKind.InGame:
                        meta.InGameUrl ??= url;
                        break;
                    case CatalogMediaKind.Cover:
                        meta.CoverUrl ??= url;
                        break;
                }

                if (item.IsImageLike &&
                    !meta.AdditionalMediaUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
                    meta.AdditionalMediaUrls.Add(url);

                if (!string.IsNullOrWhiteSpace(item.PreviewUrl) &&
                    item.IsImageLike &&
                    !meta.AdditionalMediaUrls.Contains(item.PreviewUrl!, StringComparer.OrdinalIgnoreCase))
                    meta.AdditionalMediaUrls.Add(item.PreviewUrl!);
            }

            meta.UpdatedUtc = DateTimeOffset.UtcNow;
            _index.Games[key] = meta;
            SaveUnlocked();
        }
    }

    private static string PreferScr(string url, string? preview)
    {
        if (url.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
            return url;
        if (!string.IsNullOrWhiteSpace(preview) &&
            preview.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
            return preview!;
        return url;
    }

    private static void MergeEntryFields(CatalogGameMetadata meta, CatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Id))
            meta.Id = entry.Id;
        if (!string.IsNullOrWhiteSpace(entry.Title))
            meta.Title = entry.Title;
        if (entry.ScId is int sid and > 0)
            meta.ScId = sid;
        if (!string.IsNullOrWhiteSpace(entry.ScSlug))
            meta.ScSlug = entry.ScSlug;
        if (!string.IsNullOrWhiteSpace(entry.ScFile))
            meta.ScFile = entry.ScFile;
        if (!string.IsNullOrWhiteSpace(entry.EntryUrl))
            meta.EntryUrl = entry.EntryUrl;
        else if (string.IsNullOrWhiteSpace(meta.EntryUrl))
            meta.EntryUrl = entry.ResolveSpectrumComputingPageUrl();
    }

    private static void InferSourceFields(CatalogGameMetadata meta, string url)
    {
        if (url.Contains("spectrumcomputing.co.uk", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("zxdb.", StringComparison.OrdinalIgnoreCase))
        {
            meta.DownloadSource = "spectrum-computing";
            return;
        }

        if (url.Contains("archive.org", StringComparison.OrdinalIgnoreCase))
        {
            if (ArchiveOrgTosecTap.IsTosecUrl(url))
            {
                meta.DownloadSource = "tosec";
                meta.TosecFileName ??= ArchiveOrgTosecTap.TryRelativePathFromUrl(url)
                    ?? TryFileNameFromUrl(url);
                return;
            }

            meta.DownloadSource = "software-library";
            meta.SoftLibraryIdentifier ??= TryArchiveIdentifierFromUrl(url);
            return;
        }

        meta.DownloadSource ??= "other";
    }

    private static string? TryArchiveIdentifierFromUrl(string url)
    {
        // https://archive.org/download/{id}/file
        const string marker = "/download/";
        int i = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        string rest = url[(i + marker.Length)..];
        int slash = rest.IndexOf('/');
        string id = slash >= 0 ? rest[..slash] : rest;
        id = Uri.UnescapeDataString(id);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static string? TryFileNameFromUrl(string url)
    {
        try
        {
            string path = url;
            int q = path.IndexOf('?', StringComparison.Ordinal);
            if (q >= 0) path = path[..q];
            string name = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? null : Uri.UnescapeDataString(name);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> NonEmpty(string? url) =>
        string.IsNullOrWhiteSpace(url) ? Array.Empty<string>() : new[] { url.Trim() };

    private static void EnsureLoadedUnlocked()
    {
        if (!_loaded)
            LoadUnlocked();
    }

    private static void LoadUnlocked()
    {
        _index = new CatalogGameMetadataIndex();
        _loaded = true;

        string? path = ResolveMetadataPath(createDir: false);
        if (path == null || !File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<CatalogGameMetadataIndex>(json, JsonOpts);
            if (parsed?.Games != null)
            {
                _index = parsed;
                // Dictionary comparer may be lost on deserialize
                var rebuilt = new Dictionary<string, CatalogGameMetadata>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in parsed.Games)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                        rebuilt[kv.Key.Trim()] = kv.Value;
                }
                _index.Games = rebuilt;
            }
        }
        catch
        {
            _index = new CatalogGameMetadataIndex();
        }
    }

    private static void SaveUnlocked()
    {
        string? path = ResolveMetadataPath(createDir: true);
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

    private static CatalogGameMetadata GetOrCreateMetaUnlocked(CatalogEntry entry, string key)
    {
        if (_index.Games.TryGetValue(key, out var meta) && meta != null)
            return meta;

        string legacy = LegacyKeyFor(entry);
        if (_index.Games.TryGetValue(legacy, out meta) && meta != null)
        {
            _index.Games.Remove(legacy);
            return meta;
        }

        return new CatalogGameMetadata();
    }

    private static string? ResolveMetadataPath(bool createDir)
    {
        string? dir = createDir
            ? ProjectCatalogPaths.TryEnsureCatalogDir(_platform)
            : ProjectCatalogPaths.TryGetCatalogDir(_platform);

        if (dir != null)
            return Path.Combine(dir, ProjectCatalogPaths.GamesMetadataFileName);

        // Fallback: LocalAppData (si el binario se ejecuta fuera del árbol del repo).
        AppPaths.EnsureCreated();
        return Path.Combine(AppPaths.Root, ProjectCatalogPaths.GamesMetadataFileName);
    }
}
