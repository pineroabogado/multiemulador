namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Resuelve el directorio durable del proyecto <c>assets/catalog/{platform}/</c>
/// (metadatos de juegos, espejo de catalog-cache). No vive en LocalAppData.
/// ZX admite legado plano <c>assets/catalog/</c> si aún no se migró a subcarpeta.
/// </summary>
public static class ProjectCatalogPaths
{
    private static string? _catalogDirOverride;
    private static EmulatorPlatform _defaultPlatform = EmulatorPlatform.ZxSpectrum;

    /// <summary>Solo tests: fuerza el directorio de catálogo del proyecto (absoluto, sin subcarpeta de plataforma).</summary>
    public static void SetCatalogDirForTests(string? dir) => _catalogDirOverride = dir;

    /// <summary>Plataforma por defecto cuando no se pasa argumento (hoy ZX).</summary>
    public static EmulatorPlatform DefaultPlatform
    {
        get => _defaultPlatform;
        set => _defaultPlatform = value;
    }

    public static string GamesMetadataFileName => "games-metadata.json";
    public static string LoadSolutionsFileName => "load-solutions.json";
    public static string CatalogCacheFileName => "catalog-cache.json";
    public static string IndexFileName => "index.json";

    /// <summary>Raíz <c>assets/catalog</c> del repo (padre de las carpetas por plataforma).</summary>
    public static string? TryGetCatalogRoot()
    {
        if (!string.IsNullOrWhiteSpace(_catalogDirOverride))
        {
            // Override de test: si apunta a …/assets/catalog/{id}, el root es el padre;
            // si es un dir plano de test, devolvemos el propio override.
            string full = Path.GetFullPath(_catalogDirOverride);
            string name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (EmulatorPlatformInfo.TryParseId(name, out _))
                return Directory.GetParent(full)?.FullName;
            return full;
        }

        foreach (string start in EnumerateSearchRoots())
        {
            string? found = WalkForAssetsCatalogRoot(start);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Directorio <c>assets/catalog/{platform-id}</c> (o override de test / legado ZX plano).
    /// </summary>
    public static string? TryGetCatalogDir(EmulatorPlatform? platform = null)
    {
        if (!string.IsNullOrWhiteSpace(_catalogDirOverride))
            return _catalogDirOverride;

        EmulatorPlatform p = platform ?? _defaultPlatform;
        string? root = TryGetCatalogRoot();
        if (root == null)
            return null;

        string platformDir = Path.Combine(root, EmulatorPlatformInfo.Id(p));
        if (Directory.Exists(platformDir) || File.Exists(Path.Combine(platformDir, IndexFileName)) ||
            File.Exists(Path.Combine(platformDir, GamesMetadataFileName)))
            return Path.GetFullPath(platformDir);

        // Legado ZX: ficheros en assets/catalog/ sin subcarpeta.
        if (p == EmulatorPlatform.ZxSpectrum && HasLegacyFlatCatalog(root))
            return Path.GetFullPath(root);

        // Plataforma conocida pero aún sin ficheros: devolver ruta canónica.
        return Path.GetFullPath(platformDir);
    }

    /// <summary>
    /// Asegura el directorio de catálogo de la plataforma (crea carpetas placeholder si hace falta).
    /// </summary>
    public static string? TryEnsureCatalogDir(EmulatorPlatform? platform = null)
    {
        if (!string.IsNullOrWhiteSpace(_catalogDirOverride))
        {
            Directory.CreateDirectory(_catalogDirOverride);
            return _catalogDirOverride;
        }

        EmulatorPlatform p = platform ?? _defaultPlatform;
        string? dir = TryGetCatalogDir(p);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        foreach (string start in EnumerateSearchRoots())
        {
            string? projectRoot = WalkForProjectRoot(start);
            if (projectRoot == null)
                continue;

            string root = Path.Combine(projectRoot, "assets", "catalog");
            Directory.CreateDirectory(root);
            dir = Path.Combine(root, EmulatorPlatformInfo.Id(p));
            Directory.CreateDirectory(dir);
            return dir;
        }

        return null;
    }

    public static string? GamesMetadataPath(EmulatorPlatform? platform = null) =>
        TryGetCatalogDir(platform) is string d ? Path.Combine(d, GamesMetadataFileName) : null;

    public static string? LoadSolutionsPath(EmulatorPlatform? platform = null) =>
        TryGetCatalogDir(platform) is string d ? Path.Combine(d, LoadSolutionsFileName) : null;

    public static string? ProjectCatalogCachePath(EmulatorPlatform? platform = null) =>
        TryGetCatalogDir(platform) is string d ? Path.Combine(d, CatalogCacheFileName) : null;

    public static string? IndexPath(EmulatorPlatform? platform = null) =>
        TryGetCatalogDir(platform) is string d ? Path.Combine(d, IndexFileName) : null;

    /// <summary>Prefijo estable para claves de caché/favoritos cuando se mezclan plataformas.</summary>
    public static string CacheKeyPrefix(EmulatorPlatform platform) =>
        EmulatorPlatformInfo.Id(platform) + ":";

    private static bool HasLegacyFlatCatalog(string root)
    {
        return File.Exists(Path.Combine(root, IndexFileName))
               || File.Exists(Path.Combine(root, GamesMetadataFileName))
               || File.Exists(Path.Combine(root, CatalogCacheFileName));
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string? candidate in new[]
                 {
                     AppContext.BaseDirectory,
                     Environment.CurrentDirectory,
                     Directory.GetCurrentDirectory()
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string full;
            try { full = Path.GetFullPath(candidate); }
            catch { continue; }

            if (seen.Add(full))
                yield return full;
        }
    }

    private static string? WalkForAssetsCatalogRoot(string start)
    {
        string? dir = start;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            string catalog = Path.Combine(dir, "assets", "catalog");
            if (Directory.Exists(catalog)
                || File.Exists(Path.Combine(catalog, IndexFileName))
                || Directory.Exists(Path.Combine(catalog, EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum))))
                return Path.GetFullPath(catalog);

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static string? WalkForProjectRoot(string start)
    {
        string? dir = start;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            bool hasAssets = Directory.Exists(Path.Combine(dir, "assets"));
            bool hasSrcApp = Directory.Exists(Path.Combine(dir, "src", "MultiEmulator.App"));
            bool hasPublish = Directory.Exists(Path.Combine(dir, "publish"));
            string catalogRoot = Path.Combine(dir, "assets", "catalog");
            bool hasCatalog = Directory.Exists(catalogRoot)
                              || File.Exists(Path.Combine(catalogRoot, IndexFileName))
                              || Directory.Exists(Path.Combine(catalogRoot, "zx-spectrum"));
            if (hasSrcApp || (hasAssets && hasPublish) || (hasAssets && hasCatalog))
                return Path.GetFullPath(dir);

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
}
