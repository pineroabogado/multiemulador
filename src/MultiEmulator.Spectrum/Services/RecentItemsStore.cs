using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiEmulator.Spectrum.Services;

/// <summary>Origen de una entrada reciente.</summary>
public enum RecentKind
{
    Local = 0,
    Catalog = 1,
}

/// <summary>
/// Elemento de Archivo → Recientes. Persistido en
/// <c>%LocalAppData%\MULTIEMULADOR\{platform}\recent-items.json</c>
/// (sobrevive a <see cref="AppPaths.ClearLocalCache"/>).
/// </summary>
public sealed class RecentItem
{
    [JsonPropertyName("kind")]
    public RecentKind Kind { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>Ruta local absoluta (solo <see cref="RecentKind.Local"/>).</summary>
    [JsonPropertyName("localPath")]
    public string? LocalPath { get; set; }

    [JsonPropertyName("entryId")]
    public string? EntryId { get; set; }

    [JsonPropertyName("scId")]
    public int? ScId { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("romDownloadSource")]
    public RomDownloadSource? RomDownloadSource { get; set; }

    /// <summary>URL de descarga que funcionó (catálogo).</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("scFile")]
    public string? ScFile { get; set; }

    [JsonPropertyName("scSlug")]
    public string? ScSlug { get; set; }

    [JsonPropertyName("entryUrl")]
    public string? EntryUrl { get; set; }

    [JsonPropertyName("openedAt")]
    public DateTimeOffset OpenedAt { get; set; }

    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title.Trim();
            if (Kind == RecentKind.Local && !string.IsNullOrWhiteSpace(LocalPath))
                return Path.GetFileName(LocalPath);
            return "(sin título)";
        }
    }

    public string DedupKey()
    {
        if (Kind == RecentKind.Local)
        {
            string path = (LocalPath ?? "").Trim();
            try { path = Path.GetFullPath(path); }
            catch { /* leave as-is */ }
            return "local:" + path.ToLowerInvariant();
        }

        string platform = string.IsNullOrWhiteSpace(Platform)
            ? EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum)
            : Platform.Trim();
        if (ScId is int sc and > 0)
            return "catalog:" + platform + ":sc:" + sc.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(EntryId))
            return "catalog:" + platform + ":id:" + EntryId.Trim();
        return "catalog:" + platform + ":title:" + (Title ?? "").Trim().ToLowerInvariant();
    }

    public CatalogEntry ToCatalogEntry()
    {
        return new CatalogEntry
        {
            Id = EntryId?.Trim() ?? "",
            Title = string.IsNullOrWhiteSpace(Title) ? "(sin título)" : Title.Trim(),
            Platform = string.IsNullOrWhiteSpace(Platform)
                ? EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum)
                : Platform.Trim(),
            Url = Url?.Trim() ?? "",
            Format = string.IsNullOrWhiteSpace(Format) ? ".tap" : Format.Trim(),
            ScId = ScId,
            ScFile = ScFile,
            ScSlug = ScSlug,
            EntryUrl = EntryUrl,
        };
    }

    public static RecentItem FromLocalPath(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch { full = path; }

        return new RecentItem
        {
            Kind = RecentKind.Local,
            Title = Path.GetFileName(full),
            LocalPath = full,
            Platform = EmulatorPlatformInfo.Id(AppPaths.Platform),
            OpenedAt = DateTimeOffset.UtcNow,
        };
    }

    public static RecentItem FromCatalog(CatalogEntry entry, RomDownloadSource source)
    {
        return new RecentItem
        {
            Kind = RecentKind.Catalog,
            Title = entry.Title?.Trim() ?? "",
            EntryId = string.IsNullOrWhiteSpace(entry.Id) ? null : entry.Id.Trim(),
            ScId = entry.ScId,
            Platform = string.IsNullOrWhiteSpace(entry.Platform)
                ? EmulatorPlatformInfo.Id(AppPaths.Platform)
                : entry.Platform.Trim(),
            RomDownloadSource = source,
            Url = string.IsNullOrWhiteSpace(entry.Url) ? null : entry.Url.Trim(),
            Format = string.IsNullOrWhiteSpace(entry.Format) ? null : entry.Format.Trim(),
            ScFile = entry.ScFile,
            ScSlug = entry.ScSlug,
            EntryUrl = entry.EntryUrl,
            OpenedAt = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>
/// Lista de recientes (máx. <see cref="MaxItems"/>, más reciente primero).
/// No se borra con <see cref="AppPaths.ClearLocalCache"/>.
/// </summary>
public sealed class RecentItemsStore
{
    public const int MaxItems = 12;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _gate = new();
    private List<RecentItem> _items = new();

    public string FilePath => Path.Combine(AppPaths.Root, "recent-items.json");

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public IReadOnlyList<RecentItem> Snapshot()
    {
        lock (_gate)
            return _items.Select(Clone).ToList();
    }

    public void Load()
    {
        lock (_gate)
        {
            _items = new List<RecentItem>();
            try
            {
                AppPaths.EnsureCreated();
                if (!File.Exists(FilePath))
                    return;

                string json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<RecentItem>>(json, JsonOpts);
                if (list == null) return;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in list)
                {
                    if (item == null) continue;
                    if (item.Kind == RecentKind.Local && string.IsNullOrWhiteSpace(item.LocalPath))
                        continue;
                    if (item.Kind == RecentKind.Catalog
                        && string.IsNullOrWhiteSpace(item.EntryId)
                        && item.ScId is not > 0
                        && string.IsNullOrWhiteSpace(item.Title))
                        continue;

                    string key = item.DedupKey();
                    if (!seen.Add(key))
                        continue;
                    _items.Add(Clone(item));
                    if (_items.Count >= MaxItems)
                        break;
                }
            }
            catch
            {
                _items = new List<RecentItem>();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            AppPaths.EnsureCreated();
            string json = JsonSerializer.Serialize(_items, JsonOpts);
            File.WriteAllText(FilePath, json);
        }
    }

    public void AddLocal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        Add(RecentItem.FromLocalPath(path));
    }

    public void AddCatalog(CatalogEntry entry, RomDownloadSource source)
    {
        if (entry == null) return;
        Add(RecentItem.FromCatalog(entry, source));
    }

    public void Clear()
    {
        lock (_gate)
            _items.Clear();
        Save();
    }

    private void Add(RecentItem item)
    {
        lock (_gate)
        {
            string key = item.DedupKey();
            _items.RemoveAll(x => string.Equals(x.DedupKey(), key, StringComparison.OrdinalIgnoreCase));
            _items.Insert(0, Clone(item));
            if (_items.Count > MaxItems)
                _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }
        Save();
    }

    private static RecentItem Clone(RecentItem src) => new()
    {
        Kind = src.Kind,
        Title = src.Title,
        LocalPath = src.LocalPath,
        EntryId = src.EntryId,
        ScId = src.ScId,
        Platform = src.Platform,
        RomDownloadSource = src.RomDownloadSource,
        Url = src.Url,
        Format = src.Format,
        ScFile = src.ScFile,
        ScSlug = src.ScSlug,
        EntryUrl = src.EntryUrl,
        OpenedAt = src.OpenedAt,
    };
}
