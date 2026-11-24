using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Fuente alternativa Archive.org — colección oficial Software Library: ZX Spectrum.
/// <para>
/// Colecciones: <c>zx_spectrum_library_games</c>, <c>softwarelibrary_zx_spectrum</c>,
/// <c>softwarelibrary</c>, <c>emulationstation</c> (subidas JSMESS/MAME, ~2014).
/// </para>
/// <para>
/// Plantilla de descarga (TOSEC → IA):
/// <c>https://archive.org/download/zx_&lt;Titulo&gt;_&lt;Año&gt;_&lt;Editorial&gt;/&lt;Titulo&gt;_&lt;Año&gt;_&lt;Editorial&gt;.&lt;ext&gt;</c>
/// Ficha: sustituir <c>/download/</c> por <c>/details/</c> y quitar el fichero.
/// Extensiones habituales: <c>.z80</c>, <c>.tzx</c>, <c>.tap</c>.
/// </para>
/// </summary>
public static partial class ArchiveOrgSoftwareLibrary
{
    public const string CollectionId = "zx_spectrum_library_games";
    public const string ParentCollectionId = "softwarelibrary_zx_spectrum";
    public const string DetailsUrl = "https://archive.org/details/" + CollectionId;
    public const string SearchApiUrl = "https://archive.org/advancedsearch.php";

    private static readonly string[] DirtyTitleMarkers =
    [
        "[h]", "[h ", "[t]", "[t ", "[tr ", "[tr]", "[cr", "[b]", "[o]", "[f]", "[p]", "[m]",
    ];

    private static readonly string[] DirtyIdSuffixes =
    [
        "_h", "_t", "_tr_", "_cr", "_b", "_o", "_f", "_p", "_m",
    ];

    /// <summary>Orden de prueba en descarga convencional (Software Library ≈ snapshots).</summary>
    private static readonly string[] ConventionalExtensions = [".z80", ".tzx", ".tap", ".sna"];

    private static IReadOnlyList<string>? _testOverrideUrls;

    /// <summary>Inyecta URLs fijas (tests). <c>null</c> restaura; lista vacía = sin resultados.</summary>
    public static void SetCandidateUrlsForTests(IReadOnlyList<string>? urls) =>
        _testOverrideUrls = urls;

    public static string BuildDetailsUrl(string identifier) =>
        $"https://archive.org/details/{Uri.EscapeDataString(identifier.Trim())}";

    public static string BuildDownloadUrl(string identifier, string fileName) =>
        $"https://archive.org/download/{Uri.EscapeDataString(identifier.Trim())}/{Uri.EscapeDataString(fileName.Trim())}";

    /// <summary>
    /// Stem del fichero a partir del identifier IA: quita el prefijo <c>zx_</c>.
    /// <c>zx_Street_Hawk_1986_Ocean</c> → <c>Street_Hawk_1986_Ocean</c>.
    /// </summary>
    public static string FileStemFromIdentifier(string identifier)
    {
        string id = identifier.Trim();
        if (id.StartsWith("zx_", StringComparison.OrdinalIgnoreCase))
            return id[3..];
        return id;
    }

    /// <summary>
    /// URLs convencionales <c>/download/{id}/{stem}.z80|.tzx|.tap</c> sin consultar metadata.
    /// </summary>
    public static IReadOnlyList<string> BuildConventionalDownloadUrls(string identifier)
    {
        string id = identifier.Trim();
        if (id.Length == 0) return Array.Empty<string>();

        string stem = FileStemFromIdentifier(id);
        var urls = new List<string>(ConventionalExtensions.Length);
        foreach (string ext in ConventionalExtensions)
            urls.Add(BuildDownloadUrl(id, stem + ext));
        return urls;
    }

    /// <summary>
    /// Normaliza un trozo TOSEC a segmento de identifier IA:
    /// espacios / ( ) ' . → <c>_</c>; colapsa <c>__</c>; conserva guiones tipo Bug-Byte.
    /// </summary>
    public static string NormalizeIdentifierSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '&')
            {
                sb.Append(c);
                continue;
            }

            if (c is ' ' or '(' or ')' or '\'' or '.' or ',' or '/' or ':' or '!' or '?' or '"' or ';')
            {
                if (sb.Length > 0 && sb[^1] != '_')
                    sb.Append('_');
                continue;
            }

            if (c == '_')
            {
                if (sb.Length > 0 && sb[^1] != '_')
                    sb.Append('_');
            }
            // Otros caracteres se omiten.
        }

        while (sb.Length > 0 && sb[^1] == '_')
            sb.Length--;
        return sb.ToString();
    }

    /// <summary>
    /// Construye identifier <c>zx_Title_Year_Publisher</c> desde partes TOSEC.
    /// Aplica inversión de artículo si el título empieza por The/La/El/…
    /// </summary>
    public static string BuildIdentifier(string title, string? year = null, string? publisher = null)
    {
        string t = InvertLeadingArticle(NormalizeSpaces(title));
        string y = string.IsNullOrWhiteSpace(year) ? "" : year.Trim();
        string p = string.IsNullOrWhiteSpace(publisher) ? "" : NormalizeSpaces(publisher);

        var parts = new List<string>();
        string nt = NormalizeIdentifierSegment(t);
        if (nt.Length > 0) parts.Add(nt);
        string ny = NormalizeIdentifierSegment(y);
        if (ny.Length > 0) parts.Add(ny);
        string np = NormalizeIdentifierSegment(p);
        if (np.Length > 0) parts.Add(np);

        return "zx_" + string.Join('_', parts);
    }

    /// <summary>
    /// Parsea nombre TOSEC tipo <c>Street Hawk (1986)(Ocean)</c> o
    /// <c>Abadia del Crimen, La (1988)(Opera Soft)(ES)</c> → identifier.
    /// </summary>
    public static string BuildIdentifierFromTosecName(string tosecName)
    {
        string raw = Path.GetFileNameWithoutExtension(tosecName.Trim());
        // Quitar flags [a], [128K], …
        raw = BracketFlagsRegex().Replace(raw, "").Trim();

        var m = TosecNameRegex().Match(raw);
        if (m.Success)
        {
            string title = m.Groups["title"].Value.Trim();
            string year = m.Groups["year"].Value;
            string publisher = m.Groups["publisher"].Value.Trim();
            // Más grupos (ES)(en)… se ignoran para el identifier base limpio.
            return BuildIdentifier(title, year, publisher);
        }

        return "zx_" + NormalizeIdentifierSegment(raw);
    }

    public static string BuildSearchUrl(string titleQuery, int rows = 25)
    {
        string q =
            $"collection:{CollectionId} AND mediatype:software AND title:(\"{EscapeSolrPhrase(titleQuery)}\")";
        var sb = new StringBuilder(SearchApiUrl);
        sb.Append("?q=").Append(Uri.EscapeDataString(q));
        sb.Append("&fl[]=identifier&fl[]=title&fl[]=year");
        sb.Append("&rows=").Append(rows.ToString(CultureInfo.InvariantCulture));
        sb.Append("&page=1&output=json");
        return sb.ToString();
    }

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

        // Variante con artículo al final (estilo TOSEC / IA).
        foreach (string q in list.ToArray())
        {
            string inverted = InvertLeadingArticle(q);
            if (!string.Equals(inverted, q, StringComparison.OrdinalIgnoreCase))
                Add(inverted);
        }

        return list;
    }

    /// <summary>Ordena hits de búsqueda: título exacto limpio primero; evita secuelas y hacks.</summary>
    public static IReadOnlyList<SoftwareLibraryHit> RankHits(
        IEnumerable<SoftwareLibraryHit> hits,
        IEnumerable<string> titleQueries)
    {
        var queries = titleQueries
            .Select(NormalizeSpaces)
            .Where(q => q.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (queries.Count == 0) return Array.Empty<SoftwareLibraryHit>();

        var scored = new List<(SoftwareLibraryHit Hit, int Score, int Len)>();
        foreach (var hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.Identifier) || string.IsNullOrWhiteSpace(hit.Title))
                continue;

            int best = int.MinValue;
            foreach (string q in queries)
            {
                int s = ScoreHit(hit.Title, hit.Identifier, q);
                if (s > best) best = s;
            }

            if (best > 0)
                scored.Add((hit, best, hit.Title.Length));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Len)
            .ThenBy(x => x.Hit.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Hit)
            .DistinctBy(h => h.Identifier, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    public static IReadOnlyList<SoftwareLibraryHit> ParseSearchJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<SoftwareLibraryHit>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("docs", out var docs) ||
            docs.ValueKind != JsonValueKind.Array)
            return Array.Empty<SoftwareLibraryHit>();

        var list = new List<SoftwareLibraryHit>();
        foreach (var d in docs.EnumerateArray())
        {
            string? id = d.TryGetProperty("identifier", out var idEl) ? idEl.GetString() : null;
            string? title = d.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;
            list.Add(new SoftwareLibraryHit(id!, title!));
        }

        return list;
    }

    /// <summary>Elige el mejor fichero Spectrum de un listado metadata.files.</summary>
    public static string? PickBestMediaFileName(IEnumerable<string> fileNames)
    {
        string? best = null;
        int bestScore = int.MinValue;
        foreach (string raw in fileNames)
        {
            string name = Path.GetFileName(raw.Trim().Replace('\\', '/'));
            if (string.IsNullOrEmpty(name)) continue;
            string ext = Path.GetExtension(name).ToLowerInvariant();
            int score = ext switch
            {
                ".z80" => 100,
                ".sna" => 90,
                ".tap" => 80,
                ".tzx" => 70,
                ".trd" => 65,
                _ => 0
            };
            if (score == 0) continue;
            score -= Math.Min(30, name.Length / 5);
            if (score > bestScore)
            {
                bestScore = score;
                best = name;
            }
        }

        return best;
    }

    public static IReadOnlyList<string> ParseMetadataFileNames(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return Array.Empty<string>();
        using var doc = JsonDocument.Parse(metadataJson);
        if (!doc.RootElement.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var f in files.EnumerateArray())
        {
            if (!f.TryGetProperty("name", out var nameEl)) continue;
            string? name = nameEl.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                list.Add(name!);
        }

        return list;
    }

    /// <summary>
    /// Resuelve URLs: búsqueda en <c>zx_spectrum_library_games</c> → identifiers
    /// → URLs convencionales TOSEC (<c>.z80</c>/<c>.tzx</c>/<c>.tap</c>) y, si hace falta, metadata.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResolveCandidateUrlsAsync(
        HttpClient http,
        CatalogEntry entry,
        CancellationToken ct = default)
    {
        if (_testOverrideUrls is not null)
            return _testOverrideUrls;

        var queries = EnumerateTitleQueries(entry);
        if (queries.Count == 0)
            return Array.Empty<string>();

        var allHits = new List<SoftwareLibraryHit>();
        foreach (string q in queries.Take(3))
        {
            var hits = await SearchAsync(http, q, ct).ConfigureAwait(false);
            allHits.AddRange(hits);
        }

        var ranked = RankHits(allHits, queries);
        if (ranked.Count == 0)
            return Array.Empty<string>();

        var urls = new List<string>();
        void AddUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                urls.Add(url);
        }

        foreach (var hit in ranked)
        {
            ct.ThrowIfCancellationRequested();

            // 1) Plantilla TOSEC documentada (rápida, sin metadata).
            foreach (string u in BuildConventionalDownloadUrls(hit.Identifier))
                AddUrl(u);

            // 2) Metadata por si el fichero real no sigue exactamente el stem.
            string? media = await TryResolveMediaFileAsync(http, hit.Identifier, ct).ConfigureAwait(false);
            if (media != null)
                AddUrl(BuildDownloadUrl(hit.Identifier, media));

            if (urls.Count >= 12)
                break;
        }

        return urls;
    }

    public static async Task<IReadOnlyList<SoftwareLibraryHit>> SearchAsync(
        HttpClient http,
        string titleQuery,
        CancellationToken ct = default)
    {
        string url = BuildSearchUrl(titleQuery);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return Array.Empty<SoftwareLibraryHit>();

        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseSearchJson(json);
    }

    private static async Task<string?> TryResolveMediaFileAsync(
        HttpClient http,
        string identifier,
        CancellationToken ct)
    {
        try
        {
            string metaUrl = $"https://archive.org/metadata/{Uri.EscapeDataString(identifier)}";
            using var resp = await http.GetAsync(metaUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var names = ParseMetadataFileNames(json);
            return PickBestMediaFileName(names);
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreHit(string title, string identifier, string query)
    {
        string t = NormalizeSpaces(title);
        string bare = StripBracketFlags(t);
        string qInv = InvertLeadingArticle(query);

        bool Exact(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        if (Exact(bare, query) || Exact(t, query) || Exact(bare, qInv) || Exact(t, qInv))
        {
            int score = 1000;
            if (IsDirtyTitle(t) || IsDirtyIdentifier(identifier))
                score -= 250;
            if (!Exact(t, query) && !Exact(t, qInv))
                score -= 40;
            score -= Math.Min(50, identifier.Length / 6);
            return score;
        }

        if (t.StartsWith(query + " ", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith(query + "[", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith(qInv + " ", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith(qInv + "[", StringComparison.OrdinalIgnoreCase) ||
            // «Abadia del Crimen, La»
            t.StartsWith(qInv + ",", StringComparison.OrdinalIgnoreCase))
        {
            if (t.Contains(" - ", StringComparison.Ordinal) &&
                !query.Contains(" - ", StringComparison.Ordinal) &&
                !qInv.Contains(" - ", StringComparison.Ordinal))
                return 0;

            int score = 700;
            if (IsDirtyTitle(t) || IsDirtyIdentifier(identifier))
                score -= 250;
            return score;
        }

        return 0;
    }

    private static bool IsDirtyTitle(string title)
    {
        foreach (string m in DirtyTitleMarkers)
        {
            if (title.Contains(m, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsDirtyIdentifier(string id)
    {
        string lower = id.ToLowerInvariant();
        // Sufijos de variante al final: _h, _a2, _128K, _tr_pt…
        // Dirty duro solo hacks/cheats; _a / _128K no.
        foreach (string s in DirtyIdSuffixes)
        {
            if (lower.Contains(s, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string StripBracketFlags(string title)
    {
        string t = BracketFlagsRegex().Replace(title, "").Trim();
        // «Abadia del Crimen, La» → mantener para Exact vs invertido
        return NormalizeSpaces(t.TrimEnd(',', ' '));
    }

    /// <summary>The Hobbit → Hobbit, The · La Abadia… → Abadia…, La</summary>
    public static string InvertLeadingArticle(string title)
    {
        string t = NormalizeSpaces(title);
        string[] articles = ["The ", "La ", "El ", "Los ", "Las ", "Le ", "Les ", "L'", "A ", "An "];
        foreach (string art in articles)
        {
            if (t.StartsWith(art, StringComparison.OrdinalIgnoreCase) && t.Length > art.Length)
            {
                string rest = t[art.Length..].TrimStart();
                if (art.EndsWith('\''))
                    return $"{rest}, {art.TrimEnd()}";
                string article = art.Trim();
                return $"{rest}, {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(article.ToLowerInvariant())}";
            }
        }

        // Ya invertido: «Abadia del Crimen, La» → dejar
        return t;
    }

    private static string EscapeSolrPhrase(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

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

    [GeneratedRegex("""\[[^\]]*\]""", RegexOptions.CultureInvariant)]
    private static partial Regex BracketFlagsRegex();

    /// <summary>Title (Year)(Publisher)…</summary>
    [GeneratedRegex(
        """^(?<title>.+?)\s*\((?<year>\d{4})\)\s*\((?<publisher>[^)]+)\)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex TosecNameRegex();

    public readonly record struct SoftwareLibraryHit(string Identifier, string Title);
}
