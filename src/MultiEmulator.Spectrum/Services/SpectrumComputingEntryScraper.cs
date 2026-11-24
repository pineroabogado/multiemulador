using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace MultiEmulator.Spectrum.Services;

/// <summary>Ítem de «Additional File Downloads» en la ficha Spectrum Computing.</summary>
public sealed class SpectrumComputingFileItem
{
    public required string Type { get; init; }
    public string Description { get; init; } = "";
    public string FileName { get; init; } = "";
    public required string Href { get; init; }
    /// <summary>URL absoluta del archivo (pub/zxdb/…).</summary>
    public required string AbsoluteUrl { get; init; }
    /// <summary>URL de previsualización (p.ej. SCRtoImage / misma imagen).</summary>
    public string? PreviewUrl { get; init; }

    public bool IsImageLike
    {
        get
        {
            string n = (FileName + " " + AbsoluteUrl).ToLowerInvariant();
            return n.Contains(".gif") || n.Contains(".jpg") || n.Contains(".jpeg") ||
                   n.Contains(".png") || n.Contains(".webp") || n.Contains(".scr") ||
                   AbsoluteUrl.Contains("SCRtoImage", StringComparison.OrdinalIgnoreCase) ||
                   AbsoluteUrl.Contains("SCRtoFace", StringComparison.OrdinalIgnoreCase);
        }
    }

    public CatalogMediaKind? MapToMainSlot()
    {
        string t = Type.Trim().ToLowerInvariant();
        if (t.Contains("loading") || t.Contains("carga") || t.Contains("load screen"))
            return CatalogMediaKind.LoadScreen;
        if (t.Contains("running") || t.Contains("ejecuci") || t.Contains("in-game") || t.Contains("in game"))
            return CatalogMediaKind.InGame;
        if ((t.Contains("inlay") && t.Contains("front")) || t.Contains("carátula") || t.Contains("caratula"))
            return CatalogMediaKind.Cover;
        if ((t.Contains("inlay") && t.Contains("back")) || t.Contains("trasera"))
            return CatalogMediaKind.CoverBack;
        if (t.Contains("side a") || t.Contains("lado a"))
            return CatalogMediaKind.SideA;
        if (t.Contains("side b") || t.Contains("lado b"))
            return CatalogMediaKind.SideB;
        return null;
    }
}

/// <summary>
/// Descarga y parsea la ficha SC (<c>/entry/{id}/ZX-Spectrum/{slug}</c>)
/// para obtener enlaces reales de Additional File Downloads.
/// </summary>
public sealed partial class SpectrumComputingEntryScraper
{
    private readonly HttpClient _http;

    public SpectrumComputingEntryScraper(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultHttp();
    }

    private static HttpClient CreateDefaultHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (ZX Spectrum; +https://spectrumcomputing.co.uk)");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return http;
    }

    public static string? ResolveEntryPageUrl(CatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.EntryUrl))
            return entry.EntryUrl.Trim();
        if (entry.ScId is int id and > 0)
        {
            string slug = !string.IsNullOrWhiteSpace(entry.ScSlug)
                ? entry.ScSlug!
                : (!string.IsNullOrWhiteSpace(entry.ScFile) ? entry.ScFile! : SpectrumComputingUrls.ToFileStem(entry.Title));
            return SpectrumComputingUrls.BuildEntryUrl(id, slug);
        }
        return null;
    }

    /// <summary>Obtiene HTML (caché disco) y parsea descargas adicionales.</summary>
    public async Task<IReadOnlyList<SpectrumComputingFileItem>> GetAdditionalFilesAsync(
        CatalogEntry entry,
        CancellationToken ct = default)
    {
        string? pageUrl = ResolveEntryPageUrl(entry);
        if (pageUrl == null)
            return Array.Empty<SpectrumComputingFileItem>();

        string html = await GetEntryHtmlAsync(entry, pageUrl, ct);
        return ParseAdditionalFileDownloads(html);
    }

    public async Task<string> GetEntryHtmlAsync(CatalogEntry entry, string pageUrl, CancellationToken ct)
    {
        AppPaths.EnsureCreated();
        string dir = Path.Combine(AppPaths.Media, SpectrumComputingMedia.CacheFolderKey(entry));
        Directory.CreateDirectory(dir);
        string cachePath = Path.Combine(dir, "entry.html");

        // Caché 24h
        if (File.Exists(cachePath) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromHours(24))
        {
            return await File.ReadAllTextAsync(cachePath, ct);
        }

        using var response = await _http.GetAsync(pageUrl, ct);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(ct);
        try
        {
            await File.WriteAllTextAsync(cachePath, html, ct);
        }
        catch
        {
            // ignore cache write
        }
        return html;
    }

    public static IReadOnlyList<SpectrumComputingFileItem> ParseAdditionalFileDownloads(string html)
    {
        if (string.IsNullOrEmpty(html))
            return Array.Empty<SpectrumComputingFileItem>();

        int start = FindSectionStart(html);
        if (start < 0)
            return Array.Empty<SpectrumComputingFileItem>();

        // Cortar hasta el siguiente panel collapse o fin razonable
        int end = html.IndexOf("collapse_", start + 40, StringComparison.OrdinalIgnoreCase);
        if (end < 0 || end - start > 200_000)
            end = Math.Min(html.Length, start + 120_000);
        // Preferir cierre del panel additional
        int close = html.IndexOf("id=\"collapse_", start + 10, StringComparison.OrdinalIgnoreCase);
        // Ya estamos DENTRO de collapse_additional…; buscar el siguiente panel hermano
        int nextPanel = html.IndexOf("panel-heading", start + 80, StringComparison.OrdinalIgnoreCase);
        if (nextPanel > start)
            end = nextPanel;

        string section = html[start..end];
        var items = new List<SpectrumComputingFileItem>();

        foreach (Match row in RowRegex().Matches(section))
        {
            string block = row.Groups[1].Value;
            var item = ParseRow(block);
            if (item != null)
                items.Add(item);
        }

        return items;
    }

    private static int FindSectionStart(string html)
    {
        string[] markers =
        [
            "Additional File Downloads",
            "Descargas de archivos adicionales",
            "collapse_additional_file_downloads",
            "additional_file_downloads"
        ];
        foreach (string m in markers)
        {
            int i = html.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return i;
        }
        return -1;
    }

    private static SpectrumComputingFileItem? ParseRow(string block)
    {
        var hrefMatch = HrefRegex().Match(block);
        if (!hrefMatch.Success)
            return null;

        string href = WebUtility.HtmlDecode(hrefMatch.Groups[1].Value.Trim());
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
            return null;

        string type = "";
        var small = SmallTypeRegex().Match(block);
        if (small.Success)
            type = WebUtility.HtmlDecode(small.Groups[1].Value.Trim());
        if (string.IsNullOrWhiteSpace(type))
        {
            var title = TitleAttrRegex().Match(block);
            if (title.Success)
                type = WebUtility.HtmlDecode(title.Groups[1].Value.Trim());
        }

        string fileName = "";
        var bold = FileNameRegex().Match(block);
        if (bold.Success)
            fileName = WebUtility.HtmlDecode(bold.Groups[1].Value.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                string pathOnly = href.Split('?', 2)[0];
                fileName = Path.GetFileName(Uri.UnescapeDataString(pathOnly));
            }
            catch
            {
                fileName = href;
            }
        }

        string description = ExtractDescription(block, fileName);

        string absolute = ToAbsoluteUrl(href);
        string? preview = ExtractPreviewUrl(block) ?? GuessPreviewUrl(absolute);

        return new SpectrumComputingFileItem
        {
            Type = string.IsNullOrWhiteSpace(type) ? "Archivo" : type,
            Description = description,
            FileName = fileName,
            Href = href,
            AbsoluteUrl = absolute,
            PreviewUrl = preview
        };
    }

    private static string ExtractDescription(string block, string fileName)
    {
        // col-sm-3 sin <b> suele ser la descripción
        foreach (Match m in ColSm3Regex().Matches(block))
        {
            string inner = m.Groups[1].Value;
            if (inner.Contains("<b>", StringComparison.OrdinalIgnoreCase))
                continue;
            string text = WebUtility.HtmlDecode(StripTags(inner)).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (string.Equals(text, fileName, StringComparison.OrdinalIgnoreCase))
                continue;
            return text;
        }
        return "";
    }

    private static string? ExtractPreviewUrl(string block)
    {
        var m = BackgroundUrlRegex().Match(block);
        if (!m.Success) return null;
        string raw = WebUtility.HtmlDecode(m.Groups[1].Value.Trim().Trim('\'', '"'));
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return ToAbsoluteUrl(raw);
    }

    private static string? GuessPreviewUrl(string absoluteUrl)
    {
        if (absoluteUrl.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
        {
            // Preferir ruta relativa pub/… para SCRtoImage
            string path = absoluteUrl;
            int idx = path.IndexOf("/pub/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rel = path[(idx + 1)..]; // pub/...
                return $"{SpectrumComputingUrls.Site}/SCRtoImage.php?SCR={rel}&border=0";
            }
        }
        if (absoluteUrl.Contains(".gif", StringComparison.OrdinalIgnoreCase) ||
            absoluteUrl.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
            absoluteUrl.Contains(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            absoluteUrl.Contains(".png", StringComparison.OrdinalIgnoreCase))
            return absoluteUrl;
        return null;
    }

    public static string ToAbsoluteUrl(string href)
    {
        string h = href.Trim();
        if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            h.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return h;
        if (h.StartsWith("//"))
            return "https:" + h;
        if (!h.StartsWith('/'))
            h = "/" + h;
        return SpectrumComputingUrls.Site + h;
    }

    private static string StripTags(string html) =>
        TagRegex().Replace(html, "");

    [GeneratedRegex("""(?is)<div\s+class="row\s+zxdb_stripes"[^>]*>(.*?)</a>\s*</div>""", RegexOptions.CultureInvariant)]
    private static partial Regex RowRegex();

    [GeneratedRegex("(?is)href\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex HrefRegex();

    [GeneratedRegex("""(?is)<small>\s*([^<]+?)\s*</small>""", RegexOptions.CultureInvariant)]
    private static partial Regex SmallTypeRegex();

    [GeneratedRegex("(?is)\\btitle\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TitleAttrRegex();

    [GeneratedRegex("""(?is)<b>\s*([^<]+?)\s*</b>""", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();

    [GeneratedRegex("""(?is)col-sm-3[^>]*>(.*?)</div>""", RegexOptions.CultureInvariant)]
    private static partial Regex ColSm3Regex();

    [GeneratedRegex("""(?is)background:url\(([^)]+)\)""", RegexOptions.CultureInvariant)]
    private static partial Regex BackgroundUrlRegex();

    [GeneratedRegex("""<[^>]+>""", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();
}
