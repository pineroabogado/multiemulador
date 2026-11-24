using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Extrae el listado de juegos ZX Spectrum desde
/// https://spectrumcomputing.co.uk/list?mode=1&amp;crap=0&amp;genre=Game:&amp;prefix=X
/// </summary>
public static partial class SpectrumComputingListScraper
{
    public const string ListUrlTemplate =
        "https://spectrumcomputing.co.uk/list?mode=1&crap=0&genre=Game:&prefix={0}";

    /// <summary>Prefijos A–Z y 0–9 (como el índice de Spectrum Computing).</summary>
    public static IReadOnlyList<string> DefaultPrefixes { get; } =
        "abcdefghijklmnopqrstuvwxyz0123456789".Select(c => c.ToString()).ToArray();

    [GeneratedRegex("""href="/entry/(\d+)/([^/]+)/([^"]+)"[^>]*>([^<]+)</a>""", RegexOptions.CultureInvariant)]
    private static partial Regex EntryLinkRegex();

    public readonly record struct ListedGame(int Id, string Platform, string Slug, string Title);

    public static IEnumerable<ListedGame> ParseListHtml(string html)
    {
        var seen = new HashSet<int>();
        foreach (Match m in EntryLinkRegex().Matches(html))
        {
            if (!m.Groups[2].Value.Equals("ZX-Spectrum", StringComparison.OrdinalIgnoreCase))
                continue;

            int id = int.Parse(m.Groups[1].Value);
            if (!seen.Add(id))
                continue;

            string slug = m.Groups[3].Value;
            string title = WebUtility.HtmlDecode(m.Groups[4].Value.Trim());
            yield return new ListedGame(id, "ZX-Spectrum", slug, title);
        }
    }

    public static async Task<CatalogIndex> BuildIndexAsync(
        HttpClient http,
        IEnumerable<string>? prefixes = null,
        IProgress<(string Prefix, int PrefixCount, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var byId = new Dictionary<int, CatalogEntry>();
        foreach (string prefix in prefixes ?? DefaultPrefixes)
        {
            ct.ThrowIfCancellationRequested();
            string url = string.Format(ListUrlTemplate, Uri.EscapeDataString(prefix));
            string html = await http.GetStringAsync(url, ct);
            int added = 0;
            foreach (var g in ParseListHtml(html))
            {
                if (byId.ContainsKey(g.Id))
                    continue;

                string stem = StemFromSlugOrTitle(g.Slug, g.Title);
                var urls = SpectrumComputingUrls.EnumerateGameZipUrls(stem, g.Id);
                byId[g.Id] = new CatalogEntry
                {
                    Id = $"sc-{g.Id}",
                    Title = g.Title,
                    Machine = "48k",
                    Format = ".tap.zip",
                    ScFile = stem,
                    ScId = g.Id,
                    ScSlug = g.Slug,
                    EntryUrl = SpectrumComputingUrls.BuildEntryUrl(g.Id, g.Slug),
                    Url = urls[0],
                    License = "spectrum-computing",
                    Notes = $"SC#{g.Id} {g.Slug}"
                };
                added++;
            }

            progress?.Report((prefix, added, byId.Count));
            await Task.Delay(80, ct);
        }

        return new CatalogIndex
        {
            Version = 3,
            Entries = byId.Values
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    /// <summary>
    /// El .tap.zip en /pub suele usar CamelCase sin espacios (Arkanoid, GreenBeret, Aaargh).
    /// El slug SC usa guiones bajos (Atic_Atac) → se quitan.
    /// </summary>
    public static string StemFromSlugOrTitle(string slug, string title)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            string s = slug.Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Trim();
            // Quitar sufijos de plataforma raros; mantener letras/dígitos
            if (s.Length > 0)
                return s;
        }

        string cleaned = title.Trim();
        // Quitar etiquetas tipo [MOD] / [ZXZVM] del principio
        while (cleaned.StartsWith('[') && cleaned.Contains(']'))
        {
            int close = cleaned.IndexOf(']');
            cleaned = cleaned[(close + 1)..].TrimStart();
        }

        return SpectrumComputingUrls.ToFileStem(cleaned);
    }

    /// <summary>
    /// Carga shards offline <c>sc-games-prefix-*.json</c> (exportes del listado SC)
    /// y los convierte a <see cref="CatalogIndex"/>.
    /// </summary>
    public static CatalogIndex? TryLoadPrefixShards(string catalogDir)
    {
        if (string.IsNullOrWhiteSpace(catalogDir) || !Directory.Exists(catalogDir))
            return null;

        string[] files = Directory.GetFiles(catalogDir, "sc-games-prefix-*.json");
        if (files.Length == 0)
            return null;

        var byId = new Dictionary<int, CatalogEntry>();
        foreach (string path in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string json = File.ReadAllText(path);
                var shard = JsonSerializer.Deserialize<PrefixShardFile>(json);
                if (shard?.Games == null)
                    continue;

                foreach (var g in shard.Games)
                {
                    if (g.Id <= 0 || byId.ContainsKey(g.Id))
                        continue;

                    string slug = g.Slug ?? "";
                    string title = g.Title ?? $"SC#{g.Id}";
                    string stem = StemFromSlugOrTitle(slug, title);
                    var urls = SpectrumComputingUrls.EnumerateGameZipUrls(stem, g.Id);
                    byId[g.Id] = new CatalogEntry
                    {
                        Id = $"sc-{g.Id}",
                        Title = title,
                        Machine = "48k",
                        Format = ".tap.zip",
                        ScFile = stem,
                        ScId = g.Id,
                        ScSlug = slug,
                        EntryUrl = !string.IsNullOrWhiteSpace(g.EntryUrl)
                            ? g.EntryUrl
                            : SpectrumComputingUrls.BuildEntryUrl(g.Id, slug),
                        Url = urls[0],
                        License = "spectrum-computing",
                        Notes = $"SC#{g.Id} {slug}"
                    };
                }
            }
            catch
            {
                // Shard ilegible: seguir con el resto.
            }
        }

        if (byId.Count == 0)
            return null;

        return new CatalogIndex
        {
            Version = 3,
            Platform = EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum),
            Entries = byId.Values
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private sealed class PrefixShardFile
    {
        [JsonPropertyName("games")]
        public List<PrefixShardGame>? Games { get; set; }
    }

    private sealed class PrefixShardGame
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("entryUrl")]
        public string? EntryUrl { get; set; }
    }
}
