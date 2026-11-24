namespace MultiEmulator.Spectrum.Services;

public static class AppPaths
{
    private static string? _rootOverride;
    private static string? _romsOverride;
    private static EmulatorPlatform _platform = EmulatorPlatform.ZxSpectrum;
    private static bool _legacyRomsMigrated;

    /// <summary>
    /// Plataforma activa para rutas LocalAppData y ROMs de sistema.
    /// ZX usa la carpeta histórica <c>Spectrum</c> en LocalAppData; las ROMs
    /// de sistema viven en <c>roms/{platform-id}/</c> del proyecto/publish.
    /// </summary>
    public static EmulatorPlatform Platform
    {
        get => _platform;
        set => _platform = value;
    }

    /// <summary>
    /// Raíz LocalAppData de la plataforma (caché, estados, descargas, índices).
    /// </summary>
    public static string Root =>
        _rootOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MULTIEMULADOR", EmulatorPlatformInfo.LocalAppFolder(_platform));

    /// <summary>
    /// ROMs de sistema: <c>roms/{platform-id}/</c> junto al binario o en el árbol del proyecto.
    /// En tests con <see cref="SetRootForTests"/> sigue siendo <c>{Root}/roms</c>.
    /// </summary>
    public static string Roms => ResolveRomsDirectory();

    public static string Downloads => Path.Combine(Root, "downloads");
    public static string States => Path.Combine(Root, "states");
    public static string Media => Path.Combine(Root, "media");

    /// <summary>Solo para tests: apunta Root a un directorio temporal. Pasar null para restaurar.</summary>
    public static void SetRootForTests(string? root)
    {
        _rootOverride = root;
        _legacyRomsMigrated = false;
    }

    /// <summary>Solo para tests: fuerza el directorio de ROMs de sistema.</summary>
    public static void SetRomsForTests(string? romsDir)
    {
        _romsOverride = romsDir;
        _legacyRomsMigrated = false;
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(States);
        Directory.CreateDirectory(Media);

        string roms = ResolveRomsDirectory();
        Directory.CreateDirectory(roms);
        TryMigrateLegacyLocalRoms(roms);
    }

    /// <summary>
    /// Borra la caché local efímera de la plataforma activa (descargas y media).
    /// Conserva <c>states/</c>, <c>catalog-cache.json</c>,
    /// índices <c>archiveorg-*-index.json</c> y ajustes de usuario
    /// (favoritos, fuente ROM, mapeo de mando, <c>recent-items.json</c>).
    /// No toca ROMs de sistema del proyecto (<c>roms/{platform-id}/</c>) ni metadatos durables
    /// (<c>assets/catalog/{platform}/games-metadata.json</c>).
    /// </summary>
    public static void ClearLocalCache()
    {
        ClearDirectoryContents(Downloads);
        ClearDirectoryContents(Media);

        // Recrear carpetas vacías de caché; states/, índices LocalAppData,
        // roms del proyecto y metadatos del proyecto se conservan.
        EnsureCreated();
    }

    /// <summary>
    /// Carpeta <c>JUEGOS</c> del proyecto o junto al exe publicado.
    /// Busca en BaseDirectory / cwd y sube el árbol hasta encontrar <c>JUEGOS/</c>
    /// (o la raíz del repo con esa carpeta).
    /// </summary>
    public static string? TryFindJuegosDirectory()
    {
        foreach (string start in EnumerateSearchRoots())
        {
            string beside = Path.Combine(start, "JUEGOS");
            if (Directory.Exists(beside))
                return Path.GetFullPath(beside);

            string? dir = start;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "JUEGOS");
                if (Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);

                // Raíz de repo típica aunque JUEGOS aún no exista: no inventar path vacío.
                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Busca una ROM de sistema por nombre (proyecto/publish, luego legado LocalAppData).
    /// </summary>
    public static string? TryFindSystemRom(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string primary = Path.Combine(Roms, fileName);
        if (File.Exists(primary))
            return primary;

        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MULTIEMULADOR",
            EmulatorPlatformInfo.LocalAppFolder(_platform),
            "roms",
            fileName);
        if (File.Exists(legacy))
            return legacy;

        // Legado publish/App plano: …/roms/48.rom
        string flat = Path.Combine(AppContext.BaseDirectory, "roms", fileName);
        if (File.Exists(flat))
            return flat;

        return null;
    }

    private static string ResolveRomsDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_romsOverride))
            return Path.GetFullPath(_romsOverride);

        // Tests: aislar bajo Root temporal.
        if (!string.IsNullOrWhiteSpace(_rootOverride))
            return Path.Combine(_rootOverride, "roms");

        string platformFolder = EmulatorPlatformInfo.Id(_platform);

        foreach (string start in EnumerateSearchRoots())
        {
            // Binario publicado / output: roms/{platform-id}
            string besideApp = Path.Combine(start, "roms", platformFolder);
            if (Directory.Exists(besideApp) || HasAnyRom(besideApp))
                return Path.GetFullPath(besideApp);

            // Legado plano junto al binario: roms/*.rom
            string flatBeside = Path.Combine(start, "roms");
            if (HasAnyRom(flatBeside) && !Directory.Exists(Path.Combine(flatBeside, platformFolder)))
            {
                // Preferir subcarpeta de plataforma si podemos crearla junto al flat;
                // si solo hay flat con ROMs, usarlo hasta migrar/publicar de nuevo.
                string platformUnderFlat = Path.Combine(flatBeside, platformFolder);
                if (Directory.Exists(platformUnderFlat))
                    return Path.GetFullPath(platformUnderFlat);
                return Path.GetFullPath(flatBeside);
            }

            // Árbol del repo: …/roms/{platform-id}
            string? projectRoot = WalkForProjectRoot(start);
            if (projectRoot != null)
            {
                string projectRoms = Path.Combine(projectRoot, "roms", platformFolder);
                return Path.GetFullPath(projectRoms);
            }
        }

        // Fallback: junto al binario.
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "roms", platformFolder));
    }

    private static void TryMigrateLegacyLocalRoms(string destination)
    {
        if (_legacyRomsMigrated)
            return;
        _legacyRomsMigrated = true;

        try
        {
            Directory.CreateDirectory(destination);

            // Si ya hay ROMs en destino, no sobrescribir; solo rellenar huecos.
            string localLegacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MULTIEMULADOR",
                EmulatorPlatformInfo.LocalAppFolder(_platform),
                "roms");

            CopyMissingRoms(localLegacy, destination);

            // Legado plano junto al binario → subcarpeta de plataforma.
            string flatBeside = Path.Combine(AppContext.BaseDirectory, "roms");
            if (!string.Equals(
                    Path.GetFullPath(flatBeside),
                    Path.GetFullPath(destination),
                    StringComparison.OrdinalIgnoreCase)
                && HasAnyRom(flatBeside)
                && !string.Equals(
                    Path.GetFileName(Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    Path.GetFileName(flatBeside.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    StringComparison.OrdinalIgnoreCase))
            {
                CopyMissingRoms(flatBeside, destination);
            }
        }
        catch
        {
            // Migración best-effort; el bootstrap puede descargar o usar bundled.
        }
    }

    private static void CopyMissingRoms(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
            return;

        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name))
                continue;

            // Solo imágenes ROM y aviso legal; no copiar basura.
            string ext = Path.GetExtension(name);
            if (!ext.Equals(".rom", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("AMSTRAD-ROM-NOTICE.txt", StringComparison.OrdinalIgnoreCase))
                continue;

            string dest = Path.Combine(destDir, name);
            if (File.Exists(dest))
                continue;

            try
            {
                File.Copy(file, dest, overwrite: false);
            }
            catch
            {
                // Ignorar ficheros bloqueados.
            }
        }
    }

    private static bool HasAnyRom(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                   && Directory.EnumerateFiles(dir, "*.rom").Any();
        }
        catch
        {
            return false;
        }
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

    private static string? WalkForProjectRoot(string start)
    {
        string? dir = start;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            bool hasRoms = Directory.Exists(Path.Combine(dir, "roms", EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum)))
                           || Directory.Exists(Path.Combine(dir, "roms"));
            bool hasSrcApp = Directory.Exists(Path.Combine(dir, "src", "MultiEmulator.App"));
            bool hasAssets = Directory.Exists(Path.Combine(dir, "assets"));
            if (hasSrcApp || (hasRoms && hasAssets))
                return Path.GetFullPath(dir);

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static void ClearDirectoryContents(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (string file in Directory.EnumerateFiles(dir))
            TryDeleteFile(file);

        foreach (string sub in Directory.EnumerateDirectories(dir))
        {
            try
            {
                Directory.Delete(sub, recursive: true);
            }
            catch
            {
                // Ignorar ficheros bloqueados; el resto se limpia igual.
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignorar bloqueos puntuales.
        }
    }
}
