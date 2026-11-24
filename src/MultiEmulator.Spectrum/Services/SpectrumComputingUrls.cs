using System.Globalization;
using System.Text;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Plantillas Spectrum Computing para cintas y fichas.
/// Descarga: probar candidatas en orden hasta HTTP 200.
/// </summary>
public static class SpectrumComputingUrls
{
    public const string Site = "https://spectrumcomputing.co.uk";
    public const string PubBase = Site + "/pub/sinclair";
    public const string GamesBase = PubBase + "/games";
    public const string SnapsGamesBase = PubBase + "/snaps/games";
    public const string GamesDiskBase = PubBase + "/games-disk";
    public const string CompilationsBase = PubBase + "/compilations";
    public const string ZxdbEntriesBase = Site + "/zxdb/sinclair/entries";
    public const string ScreensBase = Site + "/pub/sinclair/screens";
    public const string AdvertsBase = Site + "/pub/sinclair/games-adverts";

    /// <summary>Carpeta numérica histórica en pub (sigue viva: <c>games/123/…</c>).</summary>
    public const string NumericFolder = "123";

    /// <summary>Carpeta numérica documentada por SC (<c>0-9</c>); se prueba además de <see cref="NumericFolder"/>.</summary>
    public const string NumericFolderAlt = "0-9";

    /// <summary>
    /// Extensiones de cinta empaquetada (Auto): TZX primero; TAP como alternativa.
    /// Usar <see cref="GameZipExtensionsFor"/> si hay preferencia de formato.
    /// </summary>
    public static readonly string[] GameZipExtensions = [".tzx.zip", ".tap.zip"];

    public static string[] GameZipExtensionsFor(RomMediaFormat format) =>
        RomMediaFormatInfo.SpectrumComputingZipExtensions(format);

    public static string BuildEntryUrl(int scId, string slug) =>
        $"{Site}/entry/{scId}/ZX-Spectrum/{slug.Trim().TrimStart('/')}";

    public static string BuildSearchUrl(string query) =>
        $"{Site}/index.php?a=search&s={Uri.EscapeDataString(query.Trim())}";

    public static string FormatEntryId7(int scId) =>
        scId.ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>Convierte «Green Beret» / «1 Line 3D Maze» → «GreenBeret» / «1Line3DMaze».</summary>
    public static string ToFileStem(string titleOrId)
    {
        if (string.IsNullOrWhiteSpace(titleOrId))
            throw new ArgumentException("Título vacío.", nameof(titleOrId));

        string raw = titleOrId.Trim();
        var parts = raw.Split(new[] { ' ', '-', '_', '.', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Título vacío.", nameof(titleOrId));

        var sb = new StringBuilder();
        foreach (string part in parts)
        {
            string p = part.Trim();
            if (p.Length == 0) continue;
            sb.Append(char.ToUpper(p[0], CultureInfo.InvariantCulture));
            if (p.Length > 1)
                sb.Append(p.AsSpan(1));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Carpeta primaria /pub/sinclair/games: dígito o símbolo → <c>123</c>; si no, 1ª letra A–Z.
    /// </summary>
    public static string FolderPrefix(string fileStem)
    {
        string stem = fileStem.Trim();
        if (stem.Length == 0)
            throw new ArgumentException("Stem vacío.", nameof(fileStem));

        foreach (char c in stem)
        {
            if (char.IsAsciiDigit(c))
                return NumericFolder;
            if (char.IsAsciiLetter(c))
                return char.ToLowerInvariant(c).ToString();
        }

        // Solo símbolos / vacío de letras-dígitos → carpeta numérica.
        return NumericFolder;
    }

    /// <summary>
    /// Carpetas a probar para un stem: letra, o <c>123</c> + <c>0-9</c> si es numérico/símbolo.
    /// </summary>
    public static IReadOnlyList<string> EnumerateFolderPrefixes(string fileStem)
    {
        string primary = FolderPrefix(fileStem);
        if (primary is NumericFolder or NumericFolderAlt)
            return [NumericFolder, NumericFolderAlt];
        return [primary];
    }

    public static string BuildPubGameZipUrl(string fileStem, string extension, string? folder = null)
    {
        string stem = fileStem.Trim();
        if (stem.Length == 0)
            throw new ArgumentException("Stem vacío.", nameof(fileStem));

        string ext = NormalizeExt(extension);
        string L = string.IsNullOrWhiteSpace(folder) ? FolderPrefix(stem) : folder.Trim();
        return $"{GamesBase}/{L}/{stem}{ext}";
    }

    /// <summary><c>…/snaps/games/&lt;inicial&gt;/&lt;Stem&gt;.z80.zip</c> (o .sna.zip).</summary>
    public static string BuildPubSnapZipUrl(string fileStem, string extension, string? folder = null)
    {
        string stem = fileStem.Trim();
        if (stem.Length == 0)
            throw new ArgumentException("Stem vacío.", nameof(fileStem));
        string ext = NormalizeExt(extension);
        if (ext is not (".z80.zip" or ".sna.zip"))
        {
            if (ext is ".z80") ext = ".z80.zip";
            else if (ext is ".sna") ext = ".sna.zip";
        }
        string L = string.IsNullOrWhiteSpace(folder) ? FolderPrefix(stem) : folder.Trim();
        return $"{SnapsGamesBase}/{L}/{stem}{ext}";
    }

    /// <summary><c>…/games-disk/&lt;inicial&gt;/&lt;Stem&gt;.dsk.zip</c>.</summary>
    public static string BuildPubDiskZipUrl(string fileStem, string? folder = null)
    {
        string stem = fileStem.Trim();
        if (stem.Length == 0)
            throw new ArgumentException("Stem vacío.", nameof(fileStem));
        string L = string.IsNullOrWhiteSpace(folder) ? FolderPrefix(stem) : folder.Trim();
        return $"{GamesDiskBase}/{L}/{stem}.dsk.zip";
    }

    /// <summary><c>…/compilations/&lt;inicial&gt;/&lt;Stem&gt;.tzx.zip</c> (etc.).</summary>
    public static string BuildPubCompilationZipUrl(string fileStem, string extension, string? folder = null)
    {
        string stem = fileStem.Trim();
        if (stem.Length == 0)
            throw new ArgumentException("Stem vacío.", nameof(fileStem));
        string ext = NormalizeExt(extension);
        string L = string.IsNullOrWhiteSpace(folder) ? FolderPrefix(stem) : folder.Trim();
        return $"{CompilationsBase}/{L}/{stem}{ext}";
    }

    /// <summary>
    /// <c>.../zxdb/sinclair/entries/0029742/1Line3DMaze.tzx.zip</c>
    /// </summary>
    public static string BuildZxdbGameZipUrl(int scId, string fileStem, string extension)
    {
        string stem = fileStem.Trim();
        if (stem.Length == 0)
            throw new ArgumentException("Stem vacío.", nameof(fileStem));
        if (scId <= 0)
            throw new ArgumentOutOfRangeException(nameof(scId));

        return $"{ZxdbEntriesBase}/{FormatEntryId7(scId)}/{stem}{NormalizeExt(extension)}";
    }

    /// <summary>Compat: primer candidato pub .tap.zip.</summary>
    public static string BuildTapZipUrl(string fileStem) =>
        BuildPubGameZipUrl(fileStem, ".tap.zip");

    public static string BuildTapZipUrlFromTitle(string titleOrId) =>
        BuildTapZipUrl(ToFileStem(titleOrId));

    public static string BuildGameZipUrl(string fileStem, string extension) =>
        BuildPubGameZipUrl(fileStem, extension);

    /// <summary>
    /// Todas las URLs razonables a probar para un stem (+ scId opcional).
    /// Orden según <paramref name="format"/>: API paths no van aquí (ver
    /// <see cref="SpectrumComputingApi"/>); esto es la cascada de plantillas.
    /// Incluye <c>games/123</c> y <c>games/0-9</c>, snaps y games-disk según formato.
    /// </summary>
    public static IReadOnlyList<string> EnumerateGameZipUrls(
        string fileStem,
        int? scId = null,
        RomMediaFormat format = RomMediaFormat.Auto)
    {
        string stem = fileStem.Trim();
        var list = new List<string>(16);
        string[] exts = GameZipExtensionsFor(format);
        var folders = EnumerateFolderPrefixes(stem);

        void Add(string url)
        {
            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                list.Add(url);
        }

        void AddTapeExts()
        {
            foreach (string folder in folders)
            {
                foreach (string ext in exts)
                {
                    if (ext is ".z80.zip" or ".sna.zip" or ".dsk.zip")
                        continue;
                    Add(BuildPubGameZipUrl(stem, ext, folder));
                }
            }
        }

        void AddSnapExts()
        {
            foreach (string folder in folders)
            {
                if (format is RomMediaFormat.Sna or RomMediaFormat.Auto)
                    Add(BuildPubSnapZipUrl(stem, ".sna.zip", folder));
                if (format is RomMediaFormat.Z80 or RomMediaFormat.Auto or RomMediaFormat.Sna)
                    Add(BuildPubSnapZipUrl(stem, ".z80.zip", folder));
                if (format is RomMediaFormat.Z80)
                    Add(BuildPubSnapZipUrl(stem, ".sna.zip", folder));
            }
        }

        void AddDiskExts()
        {
            foreach (string folder in folders)
                Add(BuildPubDiskZipUrl(stem, folder));
        }

        switch (format)
        {
            case RomMediaFormat.Z80:
            case RomMediaFormat.Sna:
                AddSnapExts();
                AddTapeExts();
                break;
            case RomMediaFormat.Dsk:
                AddDiskExts();
                AddTapeExts();
                break;
            case RomMediaFormat.Tap:
            case RomMediaFormat.Tzx:
                AddTapeExts();
                break;
            default:
                AddTapeExts();
                AddSnapExts();
                break;
        }

        // Por si una caché antigua usó la 1ª letra ignorando dígitos (…/l/1Line…).
        if (FolderPrefix(stem) == NumericFolder)
        {
            char? letter = null;
            foreach (char c in stem)
            {
                if (char.IsAsciiLetter(c))
                {
                    letter = char.ToLowerInvariant(c);
                    break;
                }
            }
            if (letter is char L)
            {
                foreach (string ext in exts)
                {
                    if (ext is ".z80.zip" or ".sna.zip" or ".dsk.zip")
                        Add(ext == ".dsk.zip"
                            ? BuildPubDiskZipUrl(stem, L.ToString())
                            : BuildPubSnapZipUrl(stem, ext, L.ToString()));
                    else
                        Add($"{GamesBase}/{L}/{stem}{ext}");
                }
            }
        }

        if (scId is int id and > 0)
        {
            foreach (string ext in exts)
            {
                if (ext is ".dsk.zip")
                    continue; // zxdb entries raramente aloja dsk con esta plantilla
                Add(BuildZxdbGameZipUrl(id, stem, ext));
            }
        }

        return list;
    }

    /// <summary>Si <paramref name="url"/> es .tzx.zip, hermana .tap.zip; si no, null.</summary>
    public static string? TryTzxZipToTapZipUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string u = url.Trim();
        int q = u.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0) u = u[..q];
        if (!u.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase))
            return null;
        return u[..^".tzx.zip".Length] + ".tap.zip";
    }

    /// <summary>
    /// Si <paramref name="url"/> es .tap.zip, devuelve la hermana .tzx.zip; si no, null.
    /// </summary>
    public static string? TryTapZipToTzxZipUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string u = url.Trim();
        int q = u.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0) u = u[..q];
        if (!u.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase))
            return null;
        return u[..^".tap.zip".Length] + ".tzx.zip";
    }


    /// <summary>
    /// Carpetas de pantallas: letra (y, si el stem es numérico, también <c>123</c>).
    /// </summary>
    public static IReadOnlyList<string> EnumerateScreenFolders(string fileStem)
    {
        string stem = fileStem.Trim();
        var list = new List<string>(2);
        void Add(string folder)
        {
            if (!list.Contains(folder, StringComparer.OrdinalIgnoreCase))
                list.Add(folder);
        }

        Add(FolderPrefix(stem));

        char? letter = null;
        foreach (char c in stem)
        {
            if (char.IsAsciiLetter(c))
            {
                letter = char.ToLowerInvariant(c);
                break;
            }
        }
        if (letter is char L)
            Add(L.ToString());

        return list;
    }

    /// <summary>
    /// Carga: SCR nativo → GIF → SCRtoFace (fallback remoto).
    /// </summary>
    public static IReadOnlyList<string> EnumerateLoadScreenUrls(string fileStem)
    {
        string stem = fileStem.Trim();
        var list = new List<string>(6);
        void Add(string url)
        {
            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                list.Add(url);
        }

        foreach (string L in EnumerateScreenFolders(stem))
            Add($"{ScreensBase}/load/{L}/scr/{stem}.scr");
        foreach (string L in EnumerateScreenFolders(stem))
            Add($"{ScreensBase}/load/{L}/gif/{stem}.gif");
        foreach (string L in EnumerateScreenFolders(stem))
            Add($"{Site}/SCRtoFace.php?SCR=pub/sinclair/screens/load/{L}/scr/{stem}.scr");

        return list;
    }

    /// <summary>Extras ZXDB: <c>{Stem}_Back.jpg</c>, <c>_SideA</c>, <c>_SideB</c>.</summary>
    public static IReadOnlyList<string> EnumerateCoverExtraUrls(string fileStem, int? scId, string suffix)
    {
        string stem = fileStem.Trim();
        string suf = suffix.StartsWith('_') ? suffix : "_" + suffix;
        var list = new List<string>(2);
        if (scId is int id and > 0)
            list.Add($"{ZxdbEntriesBase}/{FormatEntryId7(id)}/{stem}{suf}.jpg");
        return list;
    }

    /// <summary>In-game GIF bajo <c>/pub/sinclair/screens/in-game/{L}/{Stem}.gif</c>.</summary>
    public static IReadOnlyList<string> EnumerateInGameScreenUrls(string fileStem)
    {
        string stem = fileStem.Trim();
        var list = new List<string>(2);
        foreach (string L in EnumerateScreenFolders(stem))
        {
            string url = $"{ScreensBase}/in-game/{L}/{stem}.gif";
            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                list.Add(url);
        }

        return list;
    }

    /// <summary>
    /// Carátula: ZXDB jpg (si hay scId) → anuncio pub → inlay genérico (si existe).
    /// </summary>
    public static IReadOnlyList<string> EnumerateCoverUrls(string fileStem, int? scId = null)
    {
        string stem = fileStem.Trim();
        var list = new List<string>(6);
        void Add(string url)
        {
            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                list.Add(url);
        }

        if (scId is int id and > 0)
            Add($"{ZxdbEntriesBase}/{FormatEntryId7(id)}/{stem}.jpg");

        foreach (string L in EnumerateScreenFolders(stem))
        {
            Add($"{AdvertsBase}/{L}/{stem}.jpg");
            // Inlays sin editorial (a menudo 404; se prueba por si acaso)
            Add($"{Site}/pub/sinclair/games-inlays/{L}/{stem}.jpg");
        }

        return list;
    }

    public static CatalogEntry CreateEntry(string title, string? id = null, string? scFile = null, string machine = "48k", int? scId = null, string? scSlug = null)
    {
        string stem = !string.IsNullOrWhiteSpace(scFile) ? scFile.Trim() : ToFileStem(title);
        string entryId = string.IsNullOrWhiteSpace(id)
            ? stem.ToLowerInvariant()
            : id.Trim();

        var urls = EnumerateGameZipUrls(stem, scId);
        string first = urls[0];
        string format = first.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)
            ? ".tzx.zip"
            : ".tap.zip";
        var entry = new CatalogEntry
        {
            Id = entryId,
            Title = title.Trim(),
            Machine = machine,
            Format = format,
            Url = first,
            ScFile = stem,
            ScId = scId,
            ScSlug = scSlug,
            License = "spectrum-computing",
            Notes = "Descarga .tzx.zip/.tap.zip (pub + zxdb) desde Spectrum Computing"
        };
        if (scId is int sid and > 0)
        {
            string slug = !string.IsNullOrWhiteSpace(scSlug) ? scSlug! : stem;
            entry.ScSlug = slug;
            entry.EntryUrl = BuildEntryUrl(sid, slug);
        }
        return entry;
    }

    private static string NormalizeExt(string extension)
    {
        string ext = extension.Trim();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return ext;
    }
}
