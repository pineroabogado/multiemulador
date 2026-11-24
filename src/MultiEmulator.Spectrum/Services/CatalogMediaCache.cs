using System.Net.Http.Headers;
using MultiEmulator.Spectrum.Media;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Descarga media SC con cascada de URLs y caché en disco bajo
/// <c>%LocalAppData%\MULTIEMULADOR\{platform}\media\{platformId}\{id7|stem}\</c>.
/// </summary>
public sealed class CatalogMediaCache
{
    private readonly HttpClient _http;

    public CatalogMediaCache(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultHttp();
    }

    private static HttpClient CreateDefaultHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (ZX Spectrum; +https://spectrumcomputing.co.uk)");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return http;
    }

    public string GetEntryCacheDir(CatalogEntry entry)
    {
        AppPaths.EnsureCreated();
        string platformId = string.IsNullOrWhiteSpace(entry.Platform)
            ? EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum)
            : entry.Platform.Trim();
        string dir = Path.Combine(AppPaths.Media, platformId, SpectrumComputingMedia.CacheFolderKey(entry));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Descarga una URL concreta (enlace real de la ficha) a caché local.
    /// </summary>
    public async Task<string?> TryDownloadUrlAsync(
        CatalogEntry entry,
        string url,
        string cacheBaseName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        string dir = GetEntryCacheDir(entry);
        string safe = SanitizeBaseName(cacheBaseName);
        string? existing = FindCachedFile(dir, safe);
        if (existing != null)
            return existing;

        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length < 32)
                return null;

            string ext = GuessExtension(url, response.Content.Headers.ContentType?.MediaType, bytes);
            if (ScrDecoder.LooksLikeScr(bytes) ||
                url.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
                ext = ".scr";
            if (url.Contains("SCRtoImage.php", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("SCRtoFace.php", StringComparison.OrdinalIgnoreCase))
            {
                if (!ScrDecoder.LooksLikeScr(bytes))
                    ext = GuessExtension(url, response.Content.Headers.ContentType?.MediaType, bytes);
            }

            string dest = Path.Combine(dir, safe + ext);
            await File.WriteAllBytesAsync(dest, bytes, ct);
            return dest;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Devuelve ruta local si hay caché o si alguna URL de la cascada responde 200.
    /// Null si todas fallan (404 u otro error).
    /// </summary>
    public async Task<string?> TryGetPathAsync(
        CatalogEntry entry,
        CatalogMediaKind kind,
        CancellationToken ct = default,
        IReadOnlyList<string>? preferredUrls = null)
    {
        string dir = GetEntryCacheDir(entry);
        string baseName = SpectrumComputingMedia.CacheFileBaseName(kind);

        string? existing = FindCachedFile(dir, baseName);
        if (existing != null)
            return existing;

        string missMarker = Path.Combine(dir, baseName + ".missing");
        if (File.Exists(missMarker))
            return null;

        var urls = new List<string>();
        if (preferredUrls != null)
        {
            foreach (string u in preferredUrls)
            {
                if (!string.IsNullOrWhiteSpace(u) &&
                    !urls.Contains(u, StringComparer.OrdinalIgnoreCase))
                    urls.Add(u);
            }
        }
        foreach (string u in SpectrumComputingMedia.EnumerateUrls(entry, kind))
        {
            if (!urls.Contains(u, StringComparer.OrdinalIgnoreCase))
                urls.Add(u);
        }

        bool sawNotFound = false;
        bool sawOtherFailure = false;

        foreach (string url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    sawNotFound = true;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    sawOtherFailure = true;
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 32)
                {
                    sawOtherFailure = true;
                    continue;
                }

                string ext = GuessExtension(url, response.Content.Headers.ContentType?.MediaType, bytes);
                if (ScrDecoder.LooksLikeScr(bytes) ||
                    url.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
                    ext = ".scr";

                string dest = Path.Combine(dir, baseName + ext);
                await File.WriteAllBytesAsync(dest, bytes, ct);
                return dest;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                sawOtherFailure = true;
            }
        }

        if (sawNotFound && !sawOtherFailure)
        {
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dir, baseName + ".missing"),
                    DateTime.UtcNow.ToString("O"),
                    ct);
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static string SanitizeBaseName(string name)
    {
        string s = name.Trim();
        if (string.IsNullOrEmpty(s))
            s = "file";
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        if (s.Length > 80)
            s = s[..80];
        return s;
    }

    private static string? FindCachedFile(string dir, string baseName)
    {
        if (!Directory.Exists(dir))
            return null;

        foreach (string path in Directory.EnumerateFiles(dir, baseName + ".*"))
        {
            string ext = Path.GetExtension(path);
            if (ext.Equals(".missing", StringComparison.OrdinalIgnoreCase))
                continue;
            if (new FileInfo(path).Length > 0)
                return path;
        }

        return null;
    }

    private static string GuessExtension(string url, string? mediaType, byte[] bytes)
    {
        if (url.Contains("SCRtoFace.php", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("SCRtoImage.php", StringComparison.OrdinalIgnoreCase))
            return ".png";

        string urlPath = url;
        int q = urlPath.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
            urlPath = urlPath[..q];

        string ext = Path.GetExtension(urlPath);
        if (!string.IsNullOrEmpty(ext) && ext.Length <= 5)
            return ext.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            if (mediaType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("jpg", StringComparison.OrdinalIgnoreCase))
                return ".jpg";
            if (mediaType.Contains("png", StringComparison.OrdinalIgnoreCase))
                return ".png";
            if (mediaType.Contains("gif", StringComparison.OrdinalIgnoreCase))
                return ".gif";
            if (mediaType.Contains("webp", StringComparison.OrdinalIgnoreCase))
                return ".webp";
        }

        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return ".gif";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";

        return ".bin";
    }
}
