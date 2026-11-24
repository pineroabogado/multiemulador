using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiEmulator.Spectrum.Media;

namespace MultiEmulator.Spectrum.Services;

public sealed class CatalogEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    /// <summary>Id de plataforma (<c>zx-spectrum</c>, <c>amstrad-cpc</c>, <c>msx</c>). Por defecto ZX.</summary>
    [JsonPropertyName("platform")] public string Platform { get; set; } = EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum);
    [JsonPropertyName("machine")] public string Machine { get; set; } = "48k";
    /// <summary>Extensión del artefacto remoto (p.ej. .tap.zip).</summary>
    [JsonPropertyName("format")] public string Format { get; set; } = ".tap";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    /// <summary>Nombre de fichero en Spectrum Computing sin extensión (p.ej. GreenBeret).</summary>
    [JsonPropertyName("scFile")] public string? ScFile { get; set; }
    /// <summary>Id numérico ZXDB / Spectrum Computing (p.ej. 255 = Arkanoid).</summary>
    [JsonPropertyName("scId")] public int? ScId { get; set; }
    /// <summary>Slug de la ficha SC (p.ej. Ballblazer, La_Abadia_del_Crimen).</summary>
    [JsonPropertyName("scSlug")] public string? ScSlug { get; set; }
    /// <summary>URL de la ficha en Spectrum Computing.</summary>
    [JsonPropertyName("entryUrl")] public string? EntryUrl { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("license")] public string License { get; set; } = "unknown";
    [JsonPropertyName("notes")] public string? Notes { get; set; }

    [JsonIgnore]
    public EmulatorPlatform EmulatorPlatform => EmulatorPlatformInfo.ParseId(Platform);

    /// <summary>Enlace a la pantalla del juego en Spectrum Computing, si se puede resolver.</summary>
    public string? ResolveSpectrumComputingPageUrl()
    {
        if (!string.IsNullOrWhiteSpace(EntryUrl))
            return EntryUrl.Trim();

        if (ScId is int id and > 0)
        {
            string slug = !string.IsNullOrWhiteSpace(ScSlug)
                ? ScSlug!
                : (!string.IsNullOrWhiteSpace(ScFile) ? ScFile! : SpectrumComputingUrls.ToFileStem(Title));
            return SpectrumComputingUrls.BuildEntryUrl(id, slug);
        }

        // Entradas sintéticas / sin id: búsqueda en SC
        if (!string.IsNullOrWhiteSpace(Title))
            return SpectrumComputingUrls.BuildSearchUrl(Title);

        return null;
    }
}

public sealed class CatalogIndex
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("platform")] public string Platform { get; set; } = EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum);
    [JsonPropertyName("entries")] public List<CatalogEntry> Entries { get; set; } = new();
}

public sealed class CatalogService
{
    private readonly HttpClient _http;
    private readonly SpectrumComputingApi _scApi;
    private CatalogIndex _index = new();
    private EmulatorPlatform _platform = EmulatorPlatform.ZxSpectrum;

    public CatalogService(HttpClient? http = null, EmulatorPlatform platform = EmulatorPlatform.ZxSpectrum)
    {
        _http = http ?? CreateDefaultHttp();
        _scApi = new SpectrumComputingApi(_http);
        _platform = platform;
        _index.Platform = EmulatorPlatformInfo.Id(platform);
    }

    public EmulatorPlatform Platform
    {
        get => _platform;
        set
        {
            _platform = value;
            _index.Platform = EmulatorPlatformInfo.Id(value);
        }
    }

    private static HttpClient CreateDefaultHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (ZX Spectrum; +https://spectrumcomputing.co.uk)");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return http;
    }

    public IReadOnlyList<CatalogEntry> Entries => _index.Entries;

    public string CachePath => Path.Combine(AppPaths.Root, "catalog-cache.json");

    /// <summary>Vacía el índice en memoria (p.ej. plataformas «Próximamente»).</summary>
    public void ClearEntries()
    {
        _index = new CatalogIndex
        {
            Platform = EmulatorPlatformInfo.Id(_platform),
            Entries = new List<CatalogEntry>()
        };
    }

    public async Task LoadAsync(string? remoteUrl, string localFallbackPath, CancellationToken ct = default)
    {
        try
        {
            if (!EmulatorPlatformInfo.IsCatalogActive(_platform))
            {
                ClearEntries();
                return;
            }

            AppPaths.EnsureCreated();
            try { CatalogGameMetadataStore.EnsureLoaded(_platform); }
            catch
            {
                // games-metadata.json corrupto/enorme: catálogo sigue con índice vacío/parcial.
            }

            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                try
                {
                    var json = await _http.GetStringAsync(remoteUrl, ct);
                    _index = JsonSerializer.Deserialize<CatalogIndex>(json) ?? new CatalogIndex();
                    NormalizeEntries(_index, _platform);
                    await WriteCatalogCacheAsync(json, ct);
                    return;
                }
                catch
                {
                    // fall through
                }
            }

            // Preferir listado durable del proyecto, luego LocalAppData, luego índice embebido.
            foreach (string? candidate in new[]
                     {
                         ProjectCatalogPaths.ProjectCatalogCachePath(_platform),
                         CachePath
                     })
            {
                if (candidate == null || !File.Exists(candidate))
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(candidate, ct);
                    _index = JsonSerializer.Deserialize<CatalogIndex>(json) ?? new CatalogIndex();
                    NormalizeEntries(_index, _platform);
                    return;
                }
                catch
                {
                    // Caché ilegible: probar siguiente candidato.
                }
            }

            if (File.Exists(localFallbackPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(localFallbackPath, ct);
                    _index = JsonSerializer.Deserialize<CatalogIndex>(json) ?? new CatalogIndex();
                    NormalizeEntries(_index, _platform);
                }
                catch
                {
                    ClearEntries();
                }
            }
        }
        catch
        {
            // Arranque resiliente: catálogo vacío mejor que tumbar la app.
            ClearEntries();
        }
    }

    /// <summary>
    /// Vuelve a descargar el listado A–Z / 0–9 desde Spectrum Computing y lo guarda en caché local.
    /// </summary>
    public async Task<int> RebuildFromSpectrumComputingAsync(
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        AppPaths.EnsureCreated();
        status?.Report("Rehaciendo listado Spectrum Computing…");

        var progress = new Progress<(string Prefix, int PrefixCount, int Total)>(p =>
            status?.Report($"Listado «{p.Prefix}»: +{p.PrefixCount} (total {p.Total})…"));

        if (!EmulatorPlatformInfo.IsCatalogActive(_platform))
            throw new InvalidOperationException(
                $"El catálogo de {EmulatorPlatformInfo.DisplayName(_platform)} aún no está disponible.");

        _index = await SpectrumComputingListScraper.BuildIndexAsync(_http, null, progress, ct);
        NormalizeEntries(_index, _platform);

        var opts = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(_index, opts);
        await WriteCatalogCacheAsync(json, ct);

        status?.Report($"Listado listo: {_index.Entries.Count} juegos");
        return _index.Entries.Count;
    }

    private async Task WriteCatalogCacheAsync(string json, CancellationToken ct)
    {
        AppPaths.EnsureCreated();
        await File.WriteAllTextAsync(CachePath, json, ct);

        string? projectDir = ProjectCatalogPaths.TryEnsureCatalogDir(_platform);
        if (projectDir != null)
        {
            string projectPath = Path.Combine(projectDir, ProjectCatalogPaths.CatalogCacheFileName);
            await File.WriteAllTextAsync(projectPath, json, ct);
        }
    }

    /// <summary>
    /// Rellena URL Spectrum Computing si falta, con plantilla
    /// .../games/{letra}/{ScFile|Title}.tap.zip
    /// </summary>
    private static void NormalizeEntries(CatalogIndex index, EmulatorPlatform platform)
    {
        string platformId = EmulatorPlatformInfo.Id(platform);
        index.Platform = platformId;

        foreach (var e in index.Entries)
        {
            if (string.IsNullOrWhiteSpace(e.Platform))
                e.Platform = platformId;

            if (string.IsNullOrWhiteSpace(e.Url))
            {
                string stem = !string.IsNullOrWhiteSpace(e.ScFile)
                    ? e.ScFile!
                    : SpectrumComputingUrls.ToFileStem(string.IsNullOrWhiteSpace(e.Title) ? e.Id : e.Title);
                e.ScFile ??= stem;
                // Preferir TZX (turbo) cuando falte URL; TAP queda como candidato posterior.
                e.Url = SpectrumComputingUrls.BuildPubGameZipUrl(stem, ".tzx.zip");
                if (string.IsNullOrWhiteSpace(e.Format) || e.Format is ".tap" or ".tap.zip")
                    e.Format = ".tzx.zip";
            }

            // Recuperar scId/slug desde id "sc-123" o notes "SC#123 Slug" (cachés antiguas).
            if (e.ScId is null or <= 0 &&
                e.Id.StartsWith("sc-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(e.Id.AsSpan(3), out int fromId))
            {
                e.ScId = fromId;
            }

            if ((e.ScId is null or <= 0 || string.IsNullOrWhiteSpace(e.ScSlug)) &&
                !string.IsNullOrWhiteSpace(e.Notes) &&
                e.Notes.StartsWith("SC#", StringComparison.OrdinalIgnoreCase))
            {
                // "SC#255 Arkanoid"
                string rest = e.Notes[3..].Trim();
                int sp = rest.IndexOf(' ');
                if (sp > 0 && int.TryParse(rest.AsSpan(0, sp), out int nid))
                {
                    e.ScId ??= nid;
                    e.ScSlug ??= rest[(sp + 1)..].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(e.EntryUrl) && e.ScId is int sid and > 0)
            {
                string slug = !string.IsNullOrWhiteSpace(e.ScSlug)
                    ? e.ScSlug!
                    : (!string.IsNullOrWhiteSpace(e.ScFile) ? e.ScFile! : SpectrumComputingUrls.ToFileStem(e.Title));
                e.ScSlug ??= slug;
                e.EntryUrl = SpectrumComputingUrls.BuildEntryUrl(sid, slug);
            }
        }
    }

    public IEnumerable<CatalogEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _index.Entries;

        var hits = _index.Entries.Where(e =>
            e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (e.ScFile?.Contains(query.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        // Como EmulationStation: si no hay hit local, ofrecer descarga por plantilla SC.
        if (hits.Count == 0)
        {
            try
            {
                hits.Add(SpectrumComputingUrls.CreateEntry(query.Trim()));
            }
            catch
            {
                // query inválida
            }
        }

        return hits;
    }

    public string ResolveUrl(CatalogEntry entry) =>
        ResolveDownloadCandidates(entry)[0];

    /// <summary>
    /// URLs Spectrum Computing a probar: URL explícita + pub (letra/123) .tap/.tzx + zxdb/entries/{id7} .tap/.tzx
    /// (+ stems alternativos desde slug/título). Solo candidatos SC (sin Archive.org).
    /// </summary>
    public IReadOnlyList<string> ResolveDownloadCandidates(
        CatalogEntry entry,
        RomMediaFormat format = RomMediaFormat.Auto) =>
        ResolveSpectrumComputingCandidates(entry, format);

    /// <summary>
    /// Candidatos Spectrum Computing (pub + zxdb). Ignora URLs de otras fuentes en
    /// <see cref="CatalogEntry.Url"/> y en metadatos persistidos.
    /// </summary>
    public IReadOnlyList<string> ResolveSpectrumComputingCandidates(
        CatalogEntry entry,
        RomMediaFormat format = RomMediaFormat.Auto)
    {
        var ordered = new List<string>();
        void Add(string? u)
        {
            if (string.IsNullOrWhiteSpace(u)) return;
            u = u.Trim();
            if (!ordered.Contains(u, StringComparer.OrdinalIgnoreCase))
                ordered.Add(u);
        }

        // Solo reutilizar entry.Url si es SC (o no pertenece a otra fuente conocida).
        if (!string.IsNullOrWhiteSpace(entry.Url))
        {
            var detected = RomDownloadSourceInfo.TryDetectFromUrl(entry.Url);
            if (detected is null or RomDownloadSource.SpectrumComputing)
                Add(entry.Url);
        }

        var stems = new List<string>();
        void AddStem(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            s = s.Trim();
            if (!stems.Contains(s, StringComparer.OrdinalIgnoreCase))
                stems.Add(s);
        }

        AddStem(entry.ScFile);
        if (!string.IsNullOrWhiteSpace(entry.ScSlug))
        {
            AddStem(entry.ScSlug);
            AddStem(entry.ScSlug.Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal));
        }
        if (!string.IsNullOrWhiteSpace(entry.Title))
            AddStem(SpectrumComputingUrls.ToFileStem(entry.Title));
        if (stems.Count == 0 && !string.IsNullOrWhiteSpace(entry.Id))
            AddStem(SpectrumComputingUrls.ToFileStem(entry.Id));

        int? scId = entry.ScId is int sid and > 0 ? sid : null;
        foreach (string stem in stems)
        {
            foreach (string u in SpectrumComputingUrls.EnumerateGameZipUrls(stem, scId, format))
                Add(u);
        }

        if (ordered.Count == 0 && stems.Count > 0)
        {
            string fallbackExt = format == RomMediaFormat.Tap ? ".tap.zip" : ".tzx.zip";
            Add(SpectrumComputingUrls.BuildPubGameZipUrl(stems[0], fallbackExt));
        }

        PreferFormatSiblingUrls(ordered, format);
        RomMediaFormatInfo.SortUrls(ordered, format);
        return ordered;
    }

    /// <summary>
    /// Ante cada .tap.zip, antepone la hermana .tzx.zip (misma ruta SC/pub).
    /// Evita que metadatos antiguos (Flying Shark BioToxin TAP) ganen al TZX bueno.
    /// </summary>
    internal static void PreferTzxZipUrls(List<string> urls) =>
        PreferFormatSiblingUrls(urls, RomMediaFormat.Auto);

    /// <summary>
    /// Inserta hermanas TAP↔TZX según preferencia de formato.
    /// </summary>
    internal static void PreferFormatSiblingUrls(List<string> urls, RomMediaFormat format)
    {
        if (urls.Count == 0) return;

        var rebuilt = new List<string>(urls.Count + 4);
        void Add(string? u)
        {
            if (string.IsNullOrWhiteSpace(u)) return;
            if (!rebuilt.Contains(u, StringComparer.OrdinalIgnoreCase))
                rebuilt.Add(u);
        }

        bool preferTzx = RomMediaFormatInfo.PreferTzxSibling(format);
        foreach (string u in urls)
        {
            if (preferTzx)
            {
                string? tzx = SpectrumComputingUrls.TryTapZipToTzxZipUrl(u);
                if (tzx is not null)
                    Add(tzx);
                Add(u);
            }
            else if (format == RomMediaFormat.Tap)
            {
                string? tap = SpectrumComputingUrls.TryTzxZipToTapZipUrl(u);
                if (tap is not null)
                    Add(tap);
                Add(u);
            }
            else
            {
                Add(u);
            }
        }

        urls.Clear();
        urls.AddRange(rebuilt);
    }

    /// <summary>
    /// Descarga en <b>cascada siempre</b>: fuente preferida primero, luego el resto
    /// en orden SC → Archive.org Software Library → Archive.org TOSEC.
    /// <paramref name="preferredSource"/> solo elige quién va primero; no es exclusiva.
    /// <paramref name="format"/> filtra/ordena TAP/TZX/Z80/SNA/DSK en todas las fuentes.
    /// </summary>
    public async Task<string> DownloadAsync(
        CatalogEntry entry,
        RomDownloadSource preferredSource = RomDownloadSource.SpectrumComputing,
        RomMediaFormat format = RomMediaFormat.Auto,
        IProgress<(double? percent, string phase)>? progress = null,
        CancellationToken ct = default)
    {
        AppPaths.EnsureCreated();
        CatalogGameMetadataStore.ApplyToEntry(entry);

        string id = string.IsNullOrWhiteSpace(entry.Id)
            ? SpectrumComputingUrls.ToFileStem(entry.Title).ToLowerInvariant()
            : entry.Id;
        foreach (char c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '-');

        string? cached = FindExistingDownload(id, format);
        if (cached is not null)
        {
            if (string.IsNullOrEmpty(entry.Sha256))
            {
                progress?.Report((100, "Caché lista"));
                return cached;
            }
            var existing = await File.ReadAllBytesAsync(cached, ct);
            if (Convert.ToHexString(SHA256.HashData(existing)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report((100, "Caché lista"));
                return cached;
            }
        }

        Exception? lastError = null;
        var tried = new List<string>();
        string? deferredTapPath = null;
        string? deferredTapUrl = null;
        // Soft Library a menudo solo tiene Z80 de pantalla de carga (1942 Elite).
        // Con Auto/TAP/TZX diferimos el snapshot y seguimos a TOSEC (cinta jugable).
        string? deferredSnapshotPath = null;
        string? deferredSnapshotUrl = null;
        var sourcesTried = new List<string>();
        int totalCandidates = 0;
        RomDownloadSource activeSource = preferredSource;

        async Task<string?> TryCandidatesAsync(IReadOnlyList<string> urls)
        {
            string phaseLabel = DownloadPhaseLabel(activeSource);
            for (int ui = 0; ui < urls.Count; ui++)
            {
                string url = urls[ui];
                if (!tried.Contains(url, StringComparer.OrdinalIgnoreCase))
                    tried.Add(url);

                try
                {
                    progress?.Report((null, phaseLabel));
                    using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = new HttpRequestException(
                            $"HTTP {(int)response.StatusCode} para {url}");
                        continue;
                    }

                    var bytes = await ReadContentWithProgressAsync(response, progress, phaseLabel, ct)
                        .ConfigureAwait(false);
                    if (bytes.Length == 0)
                    {
                        lastError = new InvalidDataException($"Descarga vacía: {url}");
                        continue;
                    }

                    if (!string.IsNullOrEmpty(entry.Sha256))
                    {
                        string hash = Convert.ToHexString(SHA256.HashData(bytes));
                        if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Checksum mismatch for {entry.Title}");
                    }

                    // TAP con POKE/DATA (Flying Shark BioToxin): diferir si quedan TZX
                    // en esta oleada y la preferencia no fuerza TAP.
                    bool isTapZip = url.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)
                                    || url.EndsWith(".tap", StringComparison.OrdinalIgnoreCase);
                    if (isTapZip
                        && RomMediaFormatInfo.DeferHardTapForTzx(format)
                        && MediaLoader.TapArchiveBytesRequireInterpreter(bytes, Path.GetFileName(url)))
                    {
                        bool pendingTzx = false;
                        for (int j = ui + 1; j < urls.Count; j++)
                        {
                            if (urls[j].EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)
                                || urls[j].EndsWith(".tzx", StringComparison.OrdinalIgnoreCase))
                            {
                                pendingTzx = true;
                                break;
                            }
                        }

                        if (pendingTzx)
                        {
                            progress?.Report((100, "Extrayendo…"));
                            string destTap = Path.Combine(AppPaths.Downloads, id + ExtensionForUrl(url));
                            await File.WriteAllBytesAsync(destTap, bytes, ct);
                            deferredTapPath = destTap;
                            deferredTapUrl = url;
                            continue;
                        }
                    }

                    entry.Url = url;
                    entry.Format = GuessFormatFromUrl(url);

                    progress?.Report((100, "Extrayendo…"));
                    string dest = Path.Combine(AppPaths.Downloads, id + ExtensionForUrl(url));
                    await File.WriteAllBytesAsync(dest, bytes, ct);
                    return dest;
                }
                catch (HttpRequestException ex)
                {
                    lastError = ex;
                }
                catch (InvalidDataException ex)
                {
                    lastError = ex;
                }
            }

            return null;
        }

        async Task<IReadOnlyList<string>> BuildSourceCandidatesAsync(RomDownloadSource source)
        {
            var candidates = new List<string>();
            PreferPersistedWorkingUrl(entry, candidates, source, format);

            switch (source)
            {
                case RomDownloadSource.SpectrumComputing:
                {
                    progress?.Report((null, "Resolviendo SC…"));
                    try
                    {
                        var apiUrls = await _scApi.ResolveDownloadUrlsAsync(entry, format, ct)
                            .ConfigureAwait(false);
                        for (int i = apiUrls.Count - 1; i >= 0; i--)
                        {
                            string u = apiUrls[i];
                            candidates.RemoveAll(x =>
                                string.Equals(x, u, StringComparison.OrdinalIgnoreCase));
                            candidates.Insert(0, u);
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }

                    foreach (string u in ResolveSpectrumComputingCandidates(entry, format))
                    {
                        if (!candidates.Contains(u, StringComparer.OrdinalIgnoreCase))
                            candidates.Add(u);
                    }

                    PreferFormatSiblingUrls(candidates, format);
                    RomMediaFormatInfo.SortUrls(candidates, format);
                    break;
                }
                case RomDownloadSource.ArchiveOrgSoftwareLibrary:
                {
                    progress?.Report((null, "Buscando índice Archive.org…"));
                    try
                    {
                        var libraryUrls = await ArchiveOrgSoftwareLibrary
                            .ResolveCandidateUrlsAsync(_http, entry, ct).ConfigureAwait(false);
                        foreach (string u in libraryUrls)
                        {
                            if (!candidates.Contains(u, StringComparer.OrdinalIgnoreCase))
                                candidates.Add(u);
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }

                    RomMediaFormatInfo.SortUrls(candidates, format);
                    break;
                }
                case RomDownloadSource.ArchiveOrgTosec:
                {
                    progress?.Report((null, "Buscando índice Archive.org…"));
                    try
                    {
                        var archiveUrls = await ArchiveOrgTosecTap
                            .ResolveCandidateUrlsAsync(_http, entry, ct).ConfigureAwait(false);
                        foreach (string u in archiveUrls)
                        {
                            if (!candidates.Contains(u, StringComparer.OrdinalIgnoreCase))
                                candidates.Add(u);
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }

                    RomMediaFormatInfo.SortUrls(candidates, format);
                    break;
                }
            }

            return candidates;
        }

        foreach (var source in RomDownloadSourceInfo.CascadeOrder(preferredSource))
        {
            activeSource = source;
            sourcesTried.Add(RomDownloadSourceInfo.DisplayName(source));
            var candidates = await BuildSourceCandidatesAsync(source).ConfigureAwait(false);
            totalCandidates += candidates.Count;
            string? path = await TryCandidatesAsync(candidates).ConfigureAwait(false);
            if (path is null)
                continue;

            // Soft Library snapshot-only: diferir si aún quedan fuentes de cinta (TOSEC).
            // Si el usuario eligió Soft Library como preferida, respetar el Z80/SNA.
            if (source == RomDownloadSource.ArchiveOrgSoftwareLibrary
                && preferredSource != RomDownloadSource.ArchiveOrgSoftwareLibrary
                && ShouldDeferLibrarySnapshotForTape(format, path))
            {
                deferredSnapshotPath = path;
                deferredSnapshotUrl = entry.Url;
                continue;
            }

            return path;
        }

        // TAP diferido solo si ninguna fuente dio TZX/otro mejor.
        if (deferredTapPath is not null && File.Exists(deferredTapPath))
        {
            entry.Url = deferredTapUrl ?? entry.Url;
            entry.Format = GuessFormatFromUrl(entry.Url ?? deferredTapUrl ?? "");
            return deferredTapPath;
        }

        // Snapshot Soft Library como último recurso (si TOSEC tampoco dio cinta).
        if (deferredSnapshotPath is not null && File.Exists(deferredSnapshotPath))
        {
            entry.Url = deferredSnapshotUrl ?? entry.Url;
            entry.Format = GuessFormatFromUrl(entry.Url ?? deferredSnapshotUrl ?? "");
            return deferredSnapshotPath;
        }

        string cascade = string.Join(" → ", sourcesTried);
        string hint = totalCandidates == 0
            ? " (sin candidatos en ninguna fuente)"
            : $" (candidatos:{totalCandidates})";
        throw new HttpRequestException(
            $"No se pudo descargar «{entry.Title}» tras cascada {cascade}{hint}. " +
            "Probado: " +
            string.Join(" | ", tried.Take(24)) +
            (tried.Count > 24 ? $" | …(+{tried.Count - 24})" : ""),
            lastError);
    }

    /// <summary>
    /// Inserta al frente las URLs persistidas que pertenecen a <paramref name="source"/>.
    /// Si la URL guardada es de otra fuente, se ignora.
    /// </summary>
    private static void PreferPersistedWorkingUrl(
        CatalogEntry entry,
        List<string> candidates,
        RomDownloadSource source,
        RomMediaFormat format = RomMediaFormat.Auto)
    {
        try
        {
            CatalogGameMetadataStore.ApplyToEntry(entry);
            var preferred = CatalogGameMetadataStore.GetPreferredDownloadUrls(entry);
            if (preferred.Count == 0)
                return;

            // Insertar en orden inverso para que el primero (de esta fuente) quede al frente.
            for (int i = preferred.Count - 1; i >= 0; i--)
            {
                string working = preferred[i];
                if (!RomDownloadSourceInfo.UrlMatches(source, working))
                    continue;

                candidates.RemoveAll(u => string.Equals(u, working, StringComparison.OrdinalIgnoreCase));
                candidates.Insert(0, working);

                if (RomMediaFormatInfo.PreferTzxSibling(format))
                {
                    string? tzx = SpectrumComputingUrls.TryTapZipToTzxZipUrl(working);
                    if (tzx is not null && RomDownloadSourceInfo.UrlMatches(source, tzx))
                    {
                        candidates.RemoveAll(u => string.Equals(u, tzx, StringComparison.OrdinalIgnoreCase));
                        candidates.Insert(0, tzx);
                    }
                }
                else if (format == RomMediaFormat.Tap)
                {
                    string? tap = SpectrumComputingUrls.TryTzxZipToTapZipUrl(working);
                    if (tap is not null && RomDownloadSourceInfo.UrlMatches(source, tap))
                    {
                        candidates.RemoveAll(u => string.Equals(u, tap, StringComparison.OrdinalIgnoreCase));
                        candidates.Insert(0, tap);
                    }
                }
            }
        }
        catch
        {
            // Metadatos corruptos / sin acceso: seguir con la fuente activa.
        }
    }

    /// <summary>
    /// Soft Library a menudo solo ofrece Z80/SNA capturados en la pantalla de carga
    /// (p.ej. 1942 Elite). Con Auto/TAP/TZX conviene seguir a TOSEC por la cinta.
    /// </summary>
    public static bool ShouldDeferLibrarySnapshotForTape(RomMediaFormat format, string pathOrUrl)
    {
        if (format is RomMediaFormat.Z80 or RomMediaFormat.Sna)
            return false;
        if (format is not (RomMediaFormat.Auto or RomMediaFormat.Tap or RomMediaFormat.Tzx))
            return false;

        string kind = RomMediaFormatInfo.Classify(pathOrUrl);
        return kind is "z80" or "sna";
    }

    private static string? FindExistingDownload(string id, RomMediaFormat format = RomMediaFormat.Auto)
    {
        foreach (string ext in RomMediaFormatInfo.CacheExtensions(format))
        {
            string path = Path.Combine(AppPaths.Downloads, id + ext);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;
        }

        return null;
    }

    private static string ExtensionForUrl(string url)
    {
        string path = url;
        int q = path.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0) path = path[..q];

        if (path.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)) return ".tzx.zip";
        if (path.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)) return ".tap.zip";
        if (path.EndsWith(".z80.zip", StringComparison.OrdinalIgnoreCase)) return ".z80.zip";
        if (path.EndsWith(".sna.zip", StringComparison.OrdinalIgnoreCase)) return ".sna.zip";
        if (path.EndsWith(".dsk.zip", StringComparison.OrdinalIgnoreCase)) return ".dsk.zip";
        if (path.EndsWith(".z80", StringComparison.OrdinalIgnoreCase)) return ".z80";
        if (path.EndsWith(".sna", StringComparison.OrdinalIgnoreCase)) return ".sna";
        if (path.EndsWith(".dsk", StringComparison.OrdinalIgnoreCase)) return ".dsk";
        if (path.EndsWith(".tap", StringComparison.OrdinalIgnoreCase)) return ".tap";
        if (path.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase)) return ".tzx";
        if (path.EndsWith(".trd", StringComparison.OrdinalIgnoreCase)) return ".trd";
        return ".zip";
    }

    private static string GuessFormatFromUrl(string url)
    {
        string ext = ExtensionForUrl(url);
        if (ext is not ".zip")
            return ext;
        if (ArchiveOrgTosecTap.IsTosecUrl(url))
            return ".zip";
        return ext;
    }

    private static string DownloadPhaseLabel(RomDownloadSource source) => source switch
    {
        RomDownloadSource.SpectrumComputing => "Descargando SC…",
        RomDownloadSource.ArchiveOrgSoftwareLibrary => "Descargando Soft Library…",
        RomDownloadSource.ArchiveOrgTosec => "Descargando TOSEC…",
        _ => "Descargando…"
    };

    /// <summary>
    /// Lee el cuerpo HTTP con progreso: % si hay Content-Length; indeterminado si no.
    /// </summary>
    private static async Task<byte[]> ReadContentWithProgressAsync(
        HttpResponseMessage response,
        IProgress<(double? percent, string phase)>? progress,
        string phaseLabel,
        CancellationToken ct)
    {
        long? total = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Buffer inicial: Content-Length si se conoce; si no, 64 KiB creciendo.
        var ms = total is > 0 and <= int.MaxValue
            ? new MemoryStream((int)total.Value)
            : new MemoryStream(64 * 1024);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        long lastReportAt = -1;

        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, n);
            read += n;

            // Limitar spam de Report (cada ~64 KiB o al completar).
            bool due = lastReportAt < 0
                       || read - lastReportAt >= 65536
                       || (total is > 0 && read >= total.Value);
            if (!due || progress is null)
                continue;

            lastReportAt = read;
            if (total is > 0)
            {
                double pct = Math.Clamp(100.0 * read / total.Value, 0, 100);
                string mb = FormatBytes(read);
                string totalMb = FormatBytes(total.Value);
                progress.Report((pct, $"{phaseLabel} {mb}/{totalMb}"));
            }
            else
            {
                progress.Report((null, $"{phaseLabel} {FormatBytes(read)}"));
            }
        }

        if (progress is not null)
        {
            if (total is > 0)
                progress.Report((100, $"{phaseLabel} {FormatBytes(read)}"));
            else
                progress.Report((null, $"{phaseLabel} {FormatBytes(read)}"));
        }

        return ms.ToArray();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }
}
