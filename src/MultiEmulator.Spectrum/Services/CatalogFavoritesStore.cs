using System.Text.Json;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Favoritos del catálogo en
/// <c>%LocalAppData%\MULTIEMULADOR\{platform}\catalog-favorites.json</c>
/// (ZX: carpeta histórica <c>Spectrum</c>).
/// </summary>
public sealed class CatalogFavoritesStore
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private EmulatorPlatform _platform = EmulatorPlatform.ZxSpectrum;

    public EmulatorPlatform Platform
    {
        get => _platform;
        set => _platform = value;
    }

    public string FilePath => Path.Combine(AppPaths.Root, "catalog-favorites.json");

    public int Count
    {
        get { lock (_gate) return _keys.Count; }
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
        return platformId + ":" + local;
    }

    private static string LegacyKeyFor(CatalogEntry entry)
    {
        if (entry.ScId is int id and > 0)
            return "sc:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(entry.Id))
            return "id:" + entry.Id.Trim();
        return "title:" + (entry.Title ?? "").Trim();
    }

    public void Load()
    {
        lock (_gate)
        {
            _keys.Clear();
            try
            {
                AppPaths.EnsureCreated();
                if (!File.Exists(FilePath))
                    return;
                string json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list == null) return;
                foreach (string k in list)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                        _keys.Add(k.Trim());
                }
            }
            catch
            {
                // ignore corrupt file
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            AppPaths.EnsureCreated();
            var list = _keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }

    public bool IsFavorite(CatalogEntry entry)
    {
        lock (_gate)
        {
            if (_keys.Contains(KeyFor(entry)))
                return true;
            // Compat con favoritos guardados antes del prefijo de plataforma.
            return _keys.Contains(LegacyKeyFor(entry));
        }
    }

    public bool IsFavoriteKey(string key)
    {
        lock (_gate)
            return _keys.Contains(key);
    }

    /// <summary>Devuelve el nuevo estado (true = favorito).</summary>
    public bool Toggle(CatalogEntry entry)
    {
        string key = KeyFor(entry);
        string legacy = LegacyKeyFor(entry);
        bool now;
        lock (_gate)
        {
            bool was = _keys.Contains(key) || _keys.Contains(legacy);
            _keys.Remove(legacy);
            if (was)
            {
                _keys.Remove(key);
                now = false;
            }
            else
            {
                _keys.Add(key);
                now = true;
            }
        }
        Save();
        return now;
    }

    public void Set(CatalogEntry entry, bool favorite)
    {
        string key = KeyFor(entry);
        string legacy = LegacyKeyFor(entry);
        lock (_gate)
        {
            _keys.Remove(legacy);
            if (favorite) _keys.Add(key);
            else _keys.Remove(key);
        }
        Save();
    }
}
