using System.Text;
using System.Text.RegularExpressions;
using MultiEmulator.Spectrum.Media;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Empareja una ficha de catálogo con un fichero ya presente en la carpeta <c>JUEGOS</c>
/// (proyecto o junto al exe publicado), sin red.
/// </summary>
public static class LocalJuegosMatcher
{
    /// <summary>
    /// Extensiones de medio conocidas (compuestas primero). Orden no implica preferencia.
    /// </summary>
    public static readonly string[] KnownExtensions =
    [
        ".tap.zip", ".tzx.zip", ".z80.zip", ".sna.zip", ".dsk.zip", ".trd.zip",
        ".tap", ".tzx", ".z80", ".sna", ".dsk", ".trd", ".zip"
    ];

    /// <summary>
    /// Busca en <paramref name="juegosDir"/> el mejor fichero para <paramref name="entry"/>
    /// según stems/título y preferencia de formato. Null si no hay coincidencia.
    /// </summary>
    public static string? TryFindLocalPath(
        CatalogEntry entry,
        string? juegosDir = null,
        RomMediaFormat format = RomMediaFormat.Auto)
    {
        var ranked = FindLocalCandidates(entry, juegosDir, format);
        return ranked.Count == 0 ? null : ranked[0];
    }

    /// <summary>
    /// Todos los matches locales ordenados (mejor primero) para reintento de carga.
    /// Incorpora bonus de salud estática (TZX limpio &gt; TAP &gt; Z80 mid-load).
    /// </summary>
    public static IReadOnlyList<string> FindLocalCandidates(
        CatalogEntry entry,
        string? juegosDir = null,
        RomMediaFormat format = RomMediaFormat.Auto,
        string? localFileHint = null)
    {
        string? dir = juegosDir ?? AppPaths.TryFindJuegosDirectory();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return Array.Empty<string>();

        string[] files;
        try
        {
            files = Directory.GetFiles(dir);
        }
        catch
        {
            return Array.Empty<string>();
        }

        if (files.Length == 0)
            return Array.Empty<string>();

        var stems = EnumerateMatchStems(entry);
        if (stems.Count == 0)
            return Array.Empty<string>();

        var ranked = new List<(string path, int score)>();

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
                continue;

            int nameScore = ScoreFileNameAgainstStems(name, stems);
            if (nameScore <= 0)
                continue;

            int formatScore = RomMediaFormatInfo.UrlScore(format, name);
            // Nombre exacto/fuerte manda; formato decide empates.
            // Spectaculator FlashLoad: TAP con USR flashable gana al TZX turbo hermano
            // (p.ej. Arkanoid.tap.zip vs Arkanoid.tzx.zip).
            int healthBonus = 0;
            try
            {
                string kind = RomMediaFormatInfo.Classify(name);
                if (kind is "tap" or "tzx")
                {
                    var sig = LoadMediaInspect.InspectPath(path);
                    healthBonus = Math.Clamp(sig.HealthScore, -40, 160);
                    if (sig.CanFlashToUsr)
                        healthBonus += 400; // supera UrlScore TZX (×10) en empate de nombre
                    if (kind == "tzx" && sig.RequiresInterpreter && sig.HasTurboBlocks)
                        healthBonus -= 200;
                }
                else if (kind is "z80" or "sna")
                    healthBonus = -6;
            }
            catch { /* ignore */ }

            // Registro: fichero local que ya funcionó manda sobre el ranking genérico.
            int hintBonus = 0;
            if (!string.IsNullOrWhiteSpace(localFileHint)
                && string.Equals(name, localFileHint, StringComparison.OrdinalIgnoreCase))
                hintBonus = 5000;

            int total = nameScore * 1000 + formatScore * 10 + healthBonus + hintBonus;
            ranked.Add((Path.GetFullPath(path), total));
        }

        return ranked
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.path)
            .ToList();
    }

    /// <summary>
    /// Stems normalizados con los que se compara el nombre de fichero (sin extensión).
    /// </summary>
    public static IReadOnlyList<string> EnumerateMatchStems(CatalogEntry entry)
    {
        var list = new List<string>();
        void AddRaw(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            foreach (string variant in ExpandStemVariants(s.Trim()))
            {
                string key = NormalizeKey(variant);
                if (key.Length == 0) continue;
                if (!list.Contains(key, StringComparer.OrdinalIgnoreCase))
                    list.Add(key);
            }
        }

        AddRaw(entry.ScFile);
        AddRaw(entry.ScSlug);
        if (!string.IsNullOrWhiteSpace(entry.Title))
        {
            AddRaw(entry.Title);
            try { AddRaw(SpectrumComputingUrls.ToFileStem(entry.Title)); }
            catch { /* título vacío/raro */ }
        }

        if (list.Count == 0 && !string.IsNullOrWhiteSpace(entry.Id))
        {
            AddRaw(entry.Id);
            try { AddRaw(SpectrumComputingUrls.ToFileStem(entry.Id)); }
            catch { /* id vacío/raro */ }
        }

        return list;
    }

    /// <summary>
    /// Quita extensiones conocidas (p.ej. <c>FlyingShark.tzx.zip</c> → <c>FlyingShark</c>).
    /// </summary>
    public static string StripKnownExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "";

        string name = Path.GetFileName(fileName.Trim());
        foreach (string ext in KnownExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        }

        return Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>
    /// Clave alfanumérica minúscula para comparar stems («Flying Shark» ↔ «FlyingShark»).
    /// </summary>
    public static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Puntuación de coincidencia nombre↔stems: 100 exacto, 80 prefijo fuerte, 60 contiene, 0 no.
    /// </summary>
    public static int ScoreFileNameAgainstStems(string fileName, IReadOnlyList<string> normalizedStems)
    {
        if (normalizedStems.Count == 0 || string.IsNullOrWhiteSpace(fileName))
            return 0;

        string fileKey = NormalizeKey(StripKnownExtension(fileName));
        if (fileKey.Length == 0)
            return 0;

        // Soft Library / Archive: zx_Title_Year_Publisher.zip → título embebido.
        string archiveKey = ExtractArchiveOrgSoftLibKey(fileName) ?? "";

        int best = 0;
        foreach (string stem in normalizedStems)
        {
            if (stem.Length == 0) continue;

            if (string.Equals(fileKey, stem, StringComparison.OrdinalIgnoreCase))
                best = Math.Max(best, 100);
            else if (fileKey.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                     && (fileKey.Length == stem.Length
                         || !char.IsAsciiLetterOrDigit(fileKey[stem.Length])))
                best = Math.Max(best, 85);
            else if (stem.Length >= 3
                     && fileKey.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                best = Math.Max(best, 70);
            else if (stem.Length >= 4
                     && fileKey.Contains(stem, StringComparison.OrdinalIgnoreCase))
                best = Math.Max(best, 55);
            else if (archiveKey.Length > 0
                     && string.Equals(archiveKey, stem, StringComparison.OrdinalIgnoreCase))
                best = Math.Max(best, 95);
            else if (archiveKey.Length > 0
                     && stem.Length >= 4
                     && archiveKey.Contains(stem, StringComparison.OrdinalIgnoreCase))
                best = Math.Max(best, 60);
        }

        return best;
    }

    private static IEnumerable<string> ExpandStemVariants(string raw)
    {
        yield return raw;

        string noSep = raw.Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal);
        if (!string.Equals(noSep, raw, StringComparison.OrdinalIgnoreCase))
            yield return noSep;

        string spaces = raw.Replace('_', ' ').Replace('-', ' ');
        if (!string.Equals(spaces, raw, StringComparison.OrdinalIgnoreCase))
            yield return spaces;

        // CamelCase → «FlyingShark» ya es stem; también «JetSetWilly» desde slug.
        if (raw.IndexOfAny([' ', '_', '-']) < 0 && HasInternalUpper(raw))
            yield return SplitCamel(raw);
    }

    private static bool HasInternalUpper(string s)
    {
        for (int i = 1; i < s.Length; i++)
        {
            if (char.IsAsciiLetterUpper(s[i]))
                return true;
        }

        return false;
    }

    private static string SplitCamel(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsAsciiLetterUpper(c) && char.IsAsciiLetterLower(s[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>zx_Double_Dragon_III_The_Rosetta_Stone_1991_Storm_Software_128K.zip</c>
    /// → clave alfanumérica del tramo de título (sin prefijo zx / año / publisher).
    /// </summary>
    private static string? ExtractArchiveOrgSoftLibKey(string fileName)
    {
        string stem = StripKnownExtension(fileName);
        if (!stem.StartsWith("zx_", StringComparison.OrdinalIgnoreCase))
            return null;

        string body = stem[3..];
        // Cortar en el primer año de 4 dígitos rodeado de _.
        var year = Regex.Match(body, @"_\d{4}(_|$)");
        if (year.Success)
            body = body[..year.Index];

        return NormalizeKey(body.Replace('_', ' '));
    }
}
