using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Fallback Archive.org: set TOSEC ZX Spectrum LadyEklipse 2017-11-01.
/// Plantilla: <c>Games/%5B{TAP|TZX|Z80|SNA}%5D/&lt;nombre TOSEC&gt;.zip</c>.
/// Índice maestro: <c>{ItemId}_files.xml</c> (preferido sobre listing HTML).
/// Preferencia de descarga: TAP &gt; TZX &gt; Z80 &gt; SNA.
/// </summary>
public static partial class ArchiveOrgTosecTap
{
    public const string ItemId = "ZXSpectrumTOSECSetV20171101LadyEklipse";
    public const string TapFolderEncoded = "Games/%5BTAP%5D";
    public const string TapFolderDecoded = "Games/[TAP]";

    /// <summary>Formatos indexados bajo Games/[FMT] (orden = preferencia de descarga).</summary>
    public static readonly string[] IndexedFormats = ["TAP", "TZX", "Z80", "SNA"];

    /// <summary>Base de descarga (carpeta TAP, con barra final).</summary>
    public static string TapBaseUrl =>
        $"https://archive.org/download/{ItemId}/{TapFolderEncoded}/";

    public static string IndexListingUrl => TapBaseUrl;

    public static string FilesXmlUrl =>
        $"https://archive.org/download/{ItemId}/{ItemId}_files.xml";

    public static string ItemBaseUrl =>
        $"https://archive.org/download/{ItemId}/";

    private static readonly string[] DirtyFlags =
    [
        "[h ", "[h]", "[cr ", "[cr]", "[t]", "[t ", "[tr ", "[tr]", "[b]", "[b ",
        "[o]", "[f]", "[p]", "[m]", "[bios]", "[req ", "[aka ",
    ];

    private static readonly object IndexLock = new();
    private static IReadOnlyList<string>? _memoryIndex;
    private static DateTimeOffset _memoryIndexLoadedAt;

    /// <summary>Índice multi-formato (rutas relativas <c>Games/[FMT]/file.zip</c>).</summary>
    public static string IndexCachePath =>
        Path.Combine(AppPaths.Root, "archiveorg-tosec-games-index.json");

    /// <summary>Caché legada solo-TAP (se migra al leer).</summary>
    public static string LegacyTapIndexCachePath =>
        Path.Combine(AppPaths.Root, "archiveorg-tosec-tap-index.json");

    public static string FormatFolderDecoded(string format) =>
        "Games/[" + format.Trim().ToUpperInvariant() + "]";

    public static string FormatFolderEncoded(string format) =>
        "Games/%5B" + format.Trim().ToUpperInvariant() + "%5D";

    /// <summary>
    /// Construye URL absoluta a un zip del set.
    /// Acepta nombre suelto (asume TAP), ruta relativa <c>Games/[TAP]/…zip</c>
    /// o nombre + <paramref name="format"/>.
    /// </summary>
    public static string BuildFileUrl(string fileName, string? format = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Nombre vacío.", nameof(fileName));

        var (fmt, name) = SplitRelativeOrFileName(fileName, format);
        // EscapeDataString: espacios → %20, [ ] → %5B %5D, ( ) → %28 %29.
        return ItemBaseUrl + FormatFolderEncoded(fmt) + "/" + Uri.EscapeDataString(name);
    }

    /// <summary>
    /// Variantes de título para emparejar con TOSEC (artículos al final, stem SC → palabras, etc.).
    /// </summary>
    public static IReadOnlyList<string> EnumerateTitleQueries(CatalogEntry entry)
    {
        var list = new List<string>();
        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            s = NormalizeSpaces(s);
            if (s.Length == 0) return;
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase))
                list.Add(s);
        }

        Add(entry.Title);
        if (!string.IsNullOrWhiteSpace(entry.ScSlug))
            Add(entry.ScSlug!.Replace('_', ' ').Replace('-', ' '));
        if (!string.IsNullOrWhiteSpace(entry.ScFile))
            Add(SplitCamelOrStem(entry.ScFile!));

        // TOSEC: «La Abadia del Crimen» → «Abadia del Crimen, La»
        foreach (string q in list.ToArray())
        {
            string inverted = InvertLeadingArticle(q);
            if (!string.Equals(inverted, q, StringComparison.OrdinalIgnoreCase))
                Add(inverted);
        }

        return list;
    }

    /// <summary>
    /// Candidatos generados sin índice (solo patrón TOSEC mínimo).
    /// Útil como último recurso; el índice files.xml es preferible.
    /// </summary>
    public static IReadOnlyList<string> BuildPlausibleFileNames(string title)
    {
        string t = NormalizeSpaces(title);
        if (t.Length == 0) return Array.Empty<string>();

        var names = new List<string>();
        void Add(string n)
        {
            if (!names.Contains(n, StringComparer.OrdinalIgnoreCase))
                names.Add(n);
        }

        Add(t + ".zip");
        string inv = InvertLeadingArticle(t);
        if (!string.Equals(inv, t, StringComparison.OrdinalIgnoreCase))
            Add(inv + ".zip");

        return names;
    }

    /// <summary>
    /// Ordena y filtra entradas TOSEC que coinciden con el título.
    /// Prefiere dumps limpios y formato TAP &gt; TZX &gt; Z80 &gt; SNA.
    /// Acepta nombres sueltos o rutas <c>Games/[FMT]/…</c>.
    /// </summary>
    public static IReadOnlyList<string> RankMatches(IEnumerable<string> fileNames, IEnumerable<string> titleQueries)
    {
        var queries = titleQueries
            .Select(NormalizeSpaces)
            .Where(q => q.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (queries.Count == 0) return Array.Empty<string>();

        var scored = new List<(string Key, string Display, int Score, int Len)>();
        foreach (string raw in fileNames)
        {
            string entry = raw.Trim().Replace('\\', '/');
            if (entry.Length < 5) continue;

            var (fmt, name) = SplitRelativeOrFileName(entry, format: null);
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            string baseName = name[..^4]; // sin .zip
            int best = int.MinValue;
            foreach (string q in queries)
            {
                int s = ScoreMatch(baseName, q);
                if (s > best) best = s;
            }

            if (best <= 0)
                continue;

            best += FormatPreferenceBonus(fmt);
            string key = FormatFolderDecoded(fmt) + "/" + name;
            scored.Add((key, key, best, name.Length));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Len)
            .ThenBy(x => x.Display, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Display)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    public static IReadOnlyList<string> MatchFileNames(IEnumerable<string> index, CatalogEntry entry) =>
        RankMatches(index, EnumerateTitleQueries(entry));

    public static IReadOnlyList<string> BuildCandidateUrls(IEnumerable<string> fileNames)
    {
        var urls = new List<string>();
        foreach (string name in fileNames)
        {
            string url = BuildFileUrl(name);
            if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                urls.Add(url);
        }
        return urls;
    }

    /// <summary>
    /// Resuelve URLs Archive.org para una entrada: índice files.xml (caché) → match por título.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResolveCandidateUrlsAsync(
        HttpClient http,
        CatalogEntry entry,
        CancellationToken ct = default)
    {
        var index = await GetOrLoadIndexAsync(http, ct).ConfigureAwait(false);
        if (index.Count == 0)
            return Array.Empty<string>();

        var matches = MatchFileNames(index, entry);
        return BuildCandidateUrls(matches);
    }

    public static async Task<IReadOnlyList<string>> GetOrLoadIndexAsync(
        HttpClient http,
        CancellationToken ct = default)
    {
        lock (IndexLock)
        {
            if (_memoryIndex is { Count: > 0 } &&
                DateTimeOffset.UtcNow - _memoryIndexLoadedAt < TimeSpan.FromHours(12))
                return _memoryIndex;
        }

        AppPaths.EnsureCreated();
        if (File.Exists(IndexCachePath))
        {
            try
            {
                var cached = await LoadIndexCacheAsync(IndexCachePath, ct).ConfigureAwait(false);
                if (cached.Count > 1000)
                {
                    SetMemoryIndex(cached);
                    return cached;
                }
            }
            catch
            {
                // regenerar
            }
        }

        // Migrar caché legada solo-TAP si existe.
        if (File.Exists(LegacyTapIndexCachePath))
        {
            try
            {
                var legacy = await LoadIndexCacheAsync(LegacyTapIndexCachePath, ct).ConfigureAwait(false);
                if (legacy.Count > 1000)
                {
                    var normalized = NormalizeIndexEntries(legacy);
                    await SaveIndexCacheAsync(normalized, ct).ConfigureAwait(false);
                    SetMemoryIndex(normalized);
                    return normalized;
                }
            }
            catch
            {
                // regenerar
            }
        }

        // Primario: files.xml multi-formato.
        var fromXml = await TryLoadIndexFromFilesXmlAsync(http, ct).ConfigureAwait(false);
        if (fromXml.Count > 1000)
        {
            await SaveIndexCacheAsync(fromXml, ct).ConfigureAwait(false);
            SetMemoryIndex(fromXml);
            return fromXml;
        }

        // Fallback: listing HTML de Games/[TAP].
        var fromHtml = await TryLoadIndexFromHtmlAsync(http, ct).ConfigureAwait(false);
        if (fromHtml.Count > 0)
        {
            var normalized = NormalizeIndexEntries(fromHtml);
            if (normalized.Count > 1000)
                await SaveIndexCacheAsync(normalized, ct).ConfigureAwait(false);
            SetMemoryIndex(normalized);
            return normalized;
        }

        if (fromXml.Count > 0)
        {
            SetMemoryIndex(fromXml);
            return fromXml;
        }

        return Array.Empty<string>();
    }

    /// <summary>Parsea el HTML del directorio Archive.org (hrefs .zip relativos).</summary>
    public static IReadOnlyList<string> ParseDirectoryListingHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return Array.Empty<string>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in HrefZipRegex().Matches(html))
        {
            string href = m.Groups[1].Value;
            if (href.Contains('/') || href.StartsWith('#')) continue;
            string name = Uri.UnescapeDataString(href.Replace("&amp;", "&"));
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                set.Add(name);
        }

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Extrae rutas relativas <c>Games/[FMT]/file.zip</c> del files.xml
    /// para TAP, TZX, Z80 y SNA.
    /// </summary>
    public static IReadOnlyList<string> ParseFilesXml(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return Array.Empty<string>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in FileNameAttrRegex().Matches(xml))
        {
            string path = m.Groups[1].Value.Replace('\\', '/');
            if (!TryParseGamesFormatPath(path, out string format, out string fileName))
                continue;
            if (!IsIndexedFormat(format))
                continue;
            set.Add(FormatFolderDecoded(format) + "/" + fileName);
        }

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True si la URL pertenece al set LadyEklipse TOSEC (cualquier formato indexado).</summary>
    public static bool IsTosecUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.Contains(ItemId, StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("archive.org", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (string fmt in IndexedFormats)
        {
            string enc = FormatFolderEncoded(fmt);
            string dec = FormatFolderDecoded(fmt);
            if (url.Contains(enc, StringComparison.OrdinalIgnoreCase) ||
                url.Contains(dec, StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/Games/%5B" + fmt + "%5D/", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/Games/[" + fmt + "]/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ruta relativa TOSEC a persistir (<c>Games/[TAP]/file.zip</c>) desde URL o nombre.
    /// </summary>
    public static string? TryRelativePathFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            string path = url;
            int q = path.IndexOf('?', StringComparison.Ordinal);
            if (q >= 0) path = path[..q];

            const string marker = "/download/";
            int i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                string rest = path[(i + marker.Length)..];
                int slash = rest.IndexOf('/');
                if (slash >= 0)
                    rest = rest[(slash + 1)..];
                // %5BTAP%5D → [TAP], %20 → espacio, %28 → (
                rest = Uri.UnescapeDataString(rest);
                if (TryParseGamesFormatPath(rest, out string format, out string fileName))
                    return FormatFolderDecoded(format) + "/" + fileName;
            }

            string name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = Uri.UnescapeDataString(name);
            return FormatFolderDecoded("TAP") + "/" + name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Expone el índice en memoria (tests / diagnóstico).</summary>
    public static void SetIndexForTests(IReadOnlyList<string>? names)
    {
        lock (IndexLock)
        {
            _memoryIndex = names is null ? null : NormalizeIndexEntries(names);
            _memoryIndexLoadedAt = names is null ? default : DateTimeOffset.UtcNow;
        }
    }

    private static async Task<IReadOnlyList<string>> TryLoadIndexFromHtmlAsync(
        HttpClient http, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, IndexListingUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return Array.Empty<string>();
            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseDirectoryListingHtml(html);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static async Task<IReadOnlyList<string>> TryLoadIndexFromFilesXmlAsync(
        HttpClient http, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(FilesXmlUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return Array.Empty<string>();
            string xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseFilesXml(xml);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static async Task<IReadOnlyList<string>> LoadIndexCacheAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var doc = await JsonSerializer.DeserializeAsync<IndexCacheDto>(fs, cancellationToken: ct)
            .ConfigureAwait(false);
        if (doc?.Files is null || doc.Files.Count == 0)
            return Array.Empty<string>();
        return NormalizeIndexEntries(doc.Files);
    }

    private static async Task SaveIndexCacheAsync(IReadOnlyList<string> files, CancellationToken ct)
    {
        var dto = new IndexCacheDto
        {
            Source = ItemId + "/Games/[TAP|TZX|Z80|SNA]",
            SavedUtc = DateTimeOffset.UtcNow,
            Files = files.ToList()
        };
        await using var fs = File.Create(IndexCachePath);
        await JsonSerializer.SerializeAsync(fs, dto, cancellationToken: ct).ConfigureAwait(false);
    }

    private static void SetMemoryIndex(IReadOnlyList<string> names)
    {
        lock (IndexLock)
        {
            _memoryIndex = names;
            _memoryIndexLoadedAt = DateTimeOffset.UtcNow;
        }
    }

    private static IReadOnlyList<string> NormalizeIndexEntries(IEnumerable<string> entries)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in entries)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var (fmt, name) = SplitRelativeOrFileName(raw, format: null);
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsIndexedFormat(fmt)) continue;
            set.Add(FormatFolderDecoded(fmt) + "/" + name);
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string Format, string FileName) SplitRelativeOrFileName(string input, string? format)
    {
        string name = input.Trim().Replace('\\', '/');
        if (TryParseGamesFormatPath(name, out string parsedFmt, out string fileName))
            return (parsedFmt, fileName);

        int slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        string fmt = string.IsNullOrWhiteSpace(format) ? "TAP" : format.Trim().ToUpperInvariant();
        return (fmt, name);
    }

    private static bool TryParseGamesFormatPath(string path, out string format, out string fileName)
    {
        format = "";
        fileName = "";
        // Games/[TAP]/file.zip  or  Games/%5BTAP%5D/file.zip (decoded path expected)
        var m = GamesFormatPathRegex().Match(path);
        if (!m.Success) return false;
        format = m.Groups[1].Value.ToUpperInvariant();
        fileName = m.Groups[2].Value;
        if (fileName.Contains('/') || fileName.Contains('\\')) return false;
        return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIndexedFormat(string format) =>
        IndexedFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    /// <summary>Bonus de formato: TAP &gt; TZX &gt; Z80 &gt; SNA (cascada TOSEC).</summary>
    private static int FormatPreferenceBonus(string format) =>
        format.ToUpperInvariant() switch
        {
            "TAP" => 50,
            "TZX" => 40,
            "Z80" => 30,
            "SNA" => 20,
            _ => 0
        };

    private static int ScoreMatch(string baseName, string query)
    {
        // Coincidencia exacta de título TOSEC: «Title (Year)…»
        if (baseName.StartsWith(query + " (", StringComparison.OrdinalIgnoreCase))
        {
            int score = 1000;
            if (!IsDirty(baseName)) score += 200;
            // Penalizar secuelas / subtítulos: «Arkanoid - Revenge…»
            if (baseName.Contains(" - ", StringComparison.Ordinal) &&
                !query.Contains(" - ", StringComparison.Ordinal))
                score -= 150;
            // Preferir menos metadatos (más corto)
            score -= Math.Min(80, baseName.Length / 4);
            return score;
        }

        // Título + guion (secuela): solo si la query también lo pide
        if (query.Contains(" - ", StringComparison.Ordinal) &&
            baseName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return IsDirty(baseName) ? 400 : 600;

        return 0;
    }

    private static bool IsDirty(string baseName)
    {
        foreach (string flag in DirtyFlags)
        {
            if (baseName.Contains(flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string InvertLeadingArticle(string title)
    {
        string t = NormalizeSpaces(title);
        string[] articles = ["The ", "La ", "El ", "Los ", "Las ", "Le ", "Les ", "L'", "A ", "An "];
        foreach (string art in articles)
        {
            if (t.StartsWith(art, StringComparison.OrdinalIgnoreCase) && t.Length > art.Length)
            {
                string rest = t[art.Length..].TrimStart();
                string article = art.Trim().TrimEnd('\'');
                if (art.EndsWith('\''))
                    return $"{rest}, {art.TrimEnd()}";
                return $"{rest}, {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(article.ToLowerInvariant())}";
            }
        }
        return t;
    }

    private static string SplitCamelOrStem(string stem)
    {
        string s = stem.Trim();
        if (s.Contains(' ') || s.Contains('_') || s.Contains('-'))
            return NormalizeSpaces(s.Replace('_', ' ').Replace('-', ' '));

        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append(' ');
            else if (i > 0 && char.IsDigit(c) && char.IsLetter(s[i - 1]))
                sb.Append(' ');
            else if (i > 0 && char.IsLetter(c) && char.IsDigit(s[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return NormalizeSpaces(sb.ToString());
    }

    private static string NormalizeSpaces(string s) =>
        string.Join(' ', s.Trim().Split((char[]?)null!, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("""href\s*=\s*["']([^"']+\.zip)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefZipRegex();

    [GeneratedRegex("""
        name\s*=\s*"([^"]+\.zip)"
        """, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNameAttrRegex();

    [GeneratedRegex("""
        ^Games/\[([A-Za-z0-9]+)\]/([^/]+\.zip)$
        """, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GamesFormatPathRegex();

    private sealed class IndexCacheDto
    {
        public string? Source { get; set; }
        public DateTimeOffset SavedUtc { get; set; }
        public List<string> Files { get; set; } = new();
    }
}
