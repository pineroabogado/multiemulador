using System.Net.Http.Headers;
using System.Text.Json;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Resolución exacta de descargas Spectrum Computing / ZXDB.
/// Intenta la API documentada en SC (<c>/api/v1/entries</c>) y, si no está
/// disponible, ZXInfo v3 (<c>api.zxinfo.dk/v3</c>), que expone los mismos paths pub.
/// </summary>
public sealed class SpectrumComputingApi
{
    public const string ScApiBase = SpectrumComputingUrls.Site + "/api/v1";
    public const string ZxInfoApiBase = "https://api.zxinfo.dk/v3";

    private readonly HttpClient _http;

    public SpectrumComputingApi(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultHttp();
    }

    private static HttpClient CreateDefaultHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "MULTIEMULADOR/1.0 (ZX Spectrum; +https://spectrumcomputing.co.uk)");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return http;
    }

    /// <summary>
    /// URLs de descarga jugables para la entrada (filtradas/ordenadas por formato).
    /// Vacío si la API no responde o no hay ficheros.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveDownloadUrlsAsync(
        CatalogEntry entry,
        RomMediaFormat format = RomMediaFormat.Auto,
        CancellationToken ct = default)
    {
        int? scId = entry.ScId is int id and > 0 ? id : null;
        if (scId is null)
            scId = await TrySearchEntryIdAsync(entry.Title, ct).ConfigureAwait(false);

        if (scId is not int sid || sid <= 0)
            return Array.Empty<string>();

        entry.ScId ??= sid;

        var paths = await GetEntryDownloadPathsAsync(sid, ct).ConfigureAwait(false);
        if (paths.Count == 0)
            return Array.Empty<string>();

        var urls = new List<string>(paths.Count);
        foreach (string path in paths)
        {
            string abs = ToAbsoluteMediaUrl(path);
            if (string.IsNullOrWhiteSpace(abs)) continue;
            if (!IsPlayableMediaUrl(abs)) continue;
            if (format != RomMediaFormat.Auto && !RomMediaFormatInfo.MatchesFormat(format, abs))
            {
                // En formato forzado aún dejamos fallbacks de cinta si no hay match exacto:
                // se filtra después; aquí solo acumulamos jugables.
            }
            if (!urls.Contains(abs, StringComparer.OrdinalIgnoreCase))
                urls.Add(abs);
        }

        if (format != RomMediaFormat.Auto)
        {
            var exact = urls.Where(u => RomMediaFormatInfo.MatchesFormat(format, u)).ToList();
            if (exact.Count > 0)
                urls = exact;
            // Si no hay exactos, conservar todos (cascade a cinta) y SortUrls priorizará.
        }

        RomMediaFormatInfo.SortUrls(urls, format);
        return urls;
    }

    /// <summary>Busca el id ZXDB/SC por título (SC API → ZXInfo).</summary>
    public async Task<int?> TrySearchEntryIdAsync(string title, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        int? sc = await TryParseSearchIdAsync(
            $"{ScApiBase}/entries/search?title={Uri.EscapeDataString(title.Trim())}", ct)
            .ConfigureAwait(false);
        if (sc is > 0) return sc;

        return await TryParseZxInfoSearchIdAsync(title.Trim(), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetEntryDownloadPathsAsync(int scId, CancellationToken ct)
    {
        // 1) API SC documentada por el usuario (puede 404 hoy).
        var fromSc = await TryParseEntryDownloadsAsync(
            $"{ScApiBase}/entries/{scId}", ct).ConfigureAwait(false);
        if (fromSc.Count > 0)
            return fromSc;

        // 2) ZXInfo v3 (misma base ZXDB; paths /pub/sinclair/…).
        return await TryParseZxInfoGameDownloadsAsync(scId, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> TryParseZxInfoGameDownloadsAsync(int scId, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync($"{ZxInfoApiBase}/games/{scId}", ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<string>();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("_source", out var source))
                root = source;

            var list = new List<string>();
            CollectReleaseFilePaths(root, list);
            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task<int?> TryParseZxInfoSearchIdAsync(string title, CancellationToken ct)
    {
        try
        {
            string url =
                $"{ZxInfoApiBase}/search?query={Uri.EscapeDataString(title)}" +
                "&titlesonly=true&machinetype=ZXSPECTRUM&size=5&mode=compact";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("hits", out var hitsWrap))
                return null;
            if (!hitsWrap.TryGetProperty("hits", out var hits) ||
                hits.ValueKind != JsonValueKind.Array)
                return null;

            string want = NormalizeTitle(title);
            int? bestId = null;
            double bestScore = double.MinValue;

            foreach (var hit in hits.EnumerateArray())
            {
                string? idStr = hit.TryGetProperty("_id", out var idEl) ? idEl.GetString() : null;
                if (!TryParseScId(idStr, out int id))
                    continue;

                string hitTitle = "";
                if (hit.TryGetProperty("_source", out var src) &&
                    src.TryGetProperty("title", out var tEl))
                    hitTitle = tEl.GetString() ?? "";

                double score = hit.TryGetProperty("_score", out var sEl) &&
                               sEl.TryGetDouble(out double sd)
                    ? sd
                    : 0;
                if (string.Equals(NormalizeTitle(hitTitle), want, StringComparison.Ordinal))
                    score += 1000;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = id;
                }
            }

            return bestId;
        }
        catch
        {
            return null;
        }
    }

    private async Task<int?> TryParseSearchIdAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            string media = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!media.Contains("json", StringComparison.OrdinalIgnoreCase))
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            // Formas flexibles: [{id}], {entries:[{id}]}, {hits:{hits:[{_id}]}}
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (TryReadId(el, out int id))
                        return id;
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (string key in new[] { "entries", "results", "data", "items" })
                {
                    if (!doc.RootElement.TryGetProperty(key, out var arr) ||
                        arr.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (TryReadId(el, out int id))
                            return id;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> TryParseEntryDownloadsAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<string>();
            string media = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!media.Contains("json", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<string>();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            var list = new List<string>();
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("downloads", out var downloads))
                CollectDownloadArray(downloads, list);
            else if (root.TryGetProperty("files", out var files))
                CollectDownloadArray(files, list);
            else
                CollectReleaseFilePaths(root, list);

            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void CollectReleaseFilePaths(JsonElement root, List<string> list)
    {
        if (root.TryGetProperty("releases", out var releases) &&
            releases.ValueKind == JsonValueKind.Array)
        {
            foreach (var rel in releases.EnumerateArray())
            {
                if (!rel.TryGetProperty("files", out var files) ||
                    files.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var f in files.EnumerateArray())
                {
                    if (f.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } path)
                        list.Add(path);
                    else if (f.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } url)
                        list.Add(url);
                }
            }
        }

        if (root.TryGetProperty("downloads", out var downloads))
            CollectDownloadArray(downloads, list);
    }

    private static void CollectDownloadArray(JsonElement arr, List<string> list)
    {
        if (arr.ValueKind != JsonValueKind.Array) return;
        foreach (var f in arr.EnumerateArray())
        {
            if (f.ValueKind == JsonValueKind.String && f.GetString() is { Length: > 0 } s)
            {
                list.Add(s);
                continue;
            }
            if (f.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } path)
                list.Add(path);
            else if (f.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } url)
                list.Add(url);
            else if (f.TryGetProperty("href", out var h) && h.GetString() is { Length: > 0 } href)
                list.Add(href);
        }
    }

    private static bool TryReadId(JsonElement el, out int id)
    {
        id = 0;
        if (el.TryGetProperty("id", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out id))
                return id > 0;
            if (idEl.ValueKind == JsonValueKind.String)
                return TryParseScId(idEl.GetString(), out id);
        }
        if (el.TryGetProperty("_id", out var undId))
            return TryParseScId(undId.GetString(), out id);
        if (el.TryGetProperty("entry_id", out var eid))
        {
            if (eid.ValueKind == JsonValueKind.Number && eid.TryGetInt32(out id))
                return id > 0;
            return TryParseScId(eid.GetString(), out id);
        }
        return false;
    }

    private static bool TryParseScId(string? raw, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        if (int.TryParse(raw, out id) && id > 0) return true;
        // "0001813"
        if (int.TryParse(raw.TrimStart('0') is { Length: > 0 } t ? t : "0", out id) && id > 0)
            return true;
        return false;
    }

    public static string ToAbsoluteMediaUrl(string pathOrUrl)
    {
        string p = pathOrUrl.Trim();
        if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return p;
        if (p.StartsWith("//"))
            return "https:" + p;
        // /denied/… no es descargable públicamente
        if (p.StartsWith("/denied/", StringComparison.OrdinalIgnoreCase))
            return "";
        if (!p.StartsWith('/'))
            p = "/" + p;
        return SpectrumComputingUrls.Site + p;
    }

    public static bool IsPlayableMediaUrl(string url)
    {
        string u = url;
        int q = u.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0) u = u[..q];
        return u.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".z80.zip", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".sna.zip", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".dsk.zip", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".tap", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".z80", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".sna", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".dsk", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".trd.zip", StringComparison.OrdinalIgnoreCase)
               || u.EndsWith(".trd", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitle(string title) =>
        string.Join(' ', title.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
}
