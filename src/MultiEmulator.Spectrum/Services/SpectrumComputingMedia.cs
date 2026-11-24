namespace MultiEmulator.Spectrum.Services;

/// <summary>Tipos de imagen de ficha Spectrum Computing.</summary>
public enum CatalogMediaKind
{
    LoadScreen,
    InGame,
    Cover,
    CoverBack,
    SideA,
    SideB
}

/// <summary>
/// Cascadas de URL para pantallas de carga, in-game, carátula y extras ZXDB.
/// </summary>
public static class SpectrumComputingMedia
{
    public static string ResolveStem(CatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ScFile))
            return entry.ScFile.Trim();
        if (!string.IsNullOrWhiteSpace(entry.Title))
            return SpectrumComputingUrls.ToFileStem(entry.Title);
        if (!string.IsNullOrWhiteSpace(entry.Id))
            return SpectrumComputingUrls.ToFileStem(entry.Id);
        throw new ArgumentException("Entrada sin stem resoluble.", nameof(entry));
    }

    /// <summary>Carpeta de caché: id7 (7 dígitos) o stem.</summary>
    public static string CacheFolderKey(CatalogEntry entry)
    {
        if (entry.ScId is int id and > 0)
            return SpectrumComputingUrls.FormatEntryId7(id);

        string stem = ResolveStem(entry);
        foreach (char c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }

    public static IReadOnlyList<string> EnumerateUrls(CatalogEntry entry, CatalogMediaKind kind)
    {
        string stem = ResolveStem(entry);
        return kind switch
        {
            CatalogMediaKind.LoadScreen => SpectrumComputingUrls.EnumerateLoadScreenUrls(stem),
            CatalogMediaKind.InGame => SpectrumComputingUrls.EnumerateInGameScreenUrls(stem),
            CatalogMediaKind.Cover => SpectrumComputingUrls.EnumerateCoverUrls(stem, entry.ScId),
            CatalogMediaKind.CoverBack => SpectrumComputingUrls.EnumerateCoverExtraUrls(stem, entry.ScId, "_Back"),
            CatalogMediaKind.SideA => SpectrumComputingUrls.EnumerateCoverExtraUrls(stem, entry.ScId, "_SideA"),
            CatalogMediaKind.SideB => SpectrumComputingUrls.EnumerateCoverExtraUrls(stem, entry.ScId, "_SideB"),
            _ => Array.Empty<string>()
        };
    }

    public static string CacheFileBaseName(CatalogMediaKind kind) => kind switch
    {
        CatalogMediaKind.LoadScreen => "load",
        CatalogMediaKind.InGame => "ingame",
        CatalogMediaKind.Cover => "cover",
        CatalogMediaKind.CoverBack => "cover_back",
        CatalogMediaKind.SideA => "side_a",
        CatalogMediaKind.SideB => "side_b",
        _ => "media"
    };

    public static string DisplayLabel(CatalogMediaKind kind) => kind switch
    {
        CatalogMediaKind.LoadScreen => "Carga",
        CatalogMediaKind.InGame => "In-game",
        CatalogMediaKind.Cover => "Carátula",
        CatalogMediaKind.CoverBack => "Trasera",
        CatalogMediaKind.SideA => "Lado A",
        CatalogMediaKind.SideB => "Lado B",
        _ => "Imagen"
    };

    /// <summary>Badge de disponibilidad a partir de License/Notes.</summary>
    public static (string Text, string Kind) ResolveAvailabilityBadge(CatalogEntry entry)
    {
        string lic = (entry.License ?? "").Trim();
        string notes = (entry.Notes ?? "").Trim();
        string blob = (lic + " " + notes).ToLowerInvariant();

        if (blob.Contains("denied", StringComparison.Ordinal) ||
            blob.Contains("denegad", StringComparison.Ordinal) ||
            blob.Contains("no redistribution", StringComparison.Ordinal) ||
            blob.Contains("not available", StringComparison.Ordinal))
            return ("Distribución denegada", "denied");

        if (string.IsNullOrWhiteSpace(lic) ||
            lic.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
            blob.Contains("unknown", StringComparison.Ordinal))
            return ("Licencia desconocida", "unknown");

        if (lic.Contains("spectrum-computing", StringComparison.OrdinalIgnoreCase) ||
            lic.Contains("ok", StringComparison.OrdinalIgnoreCase) ||
            lic.Contains("free", StringComparison.OrdinalIgnoreCase) ||
            lic.Contains("abandoned", StringComparison.OrdinalIgnoreCase))
            return ("Disponible (SC)", "ok");

        return (lic, "info");
    }
}
