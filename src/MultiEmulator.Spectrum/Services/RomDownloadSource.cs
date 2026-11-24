namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Fuente preferida al descargar ROMs del catálogo.
/// La descarga siempre cascada: preferida → resto en orden SC → Library → TOSEC.
/// </summary>
public enum RomDownloadSource
{
    /// <summary>Spectrum Computing (pub + zxdb + API). Por defecto / 1ª en cascada.</summary>
    SpectrumComputing = 0,

    /// <summary>Archive.org Software Library (<c>zx_spectrum_library_games</c>).</summary>
    ArchiveOrgSoftwareLibrary = 1,

    /// <summary>Archive.org TOSEC LadyEklipse (<c>Games/[TAP|TZX|Z80|…]</c>).</summary>
    ArchiveOrgTosec = 2,
}

/// <summary>Etiquetas UI y clasificación de URLs por fuente.</summary>
public static class RomDownloadSourceInfo
{
    public static string DisplayName(RomDownloadSource source) => source switch
    {
        RomDownloadSource.SpectrumComputing => "Spectrum Computing",
        RomDownloadSource.ArchiveOrgSoftwareLibrary => "Archive.org Software Library",
        RomDownloadSource.ArchiveOrgTosec => "Archive.org TOSEC",
        _ => source.ToString()
    };

    /// <summary>True si la URL pertenece a la fuente indicada.</summary>
    public static bool UrlMatches(RomDownloadSource source, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        url = url.Trim();

        return source switch
        {
            RomDownloadSource.SpectrumComputing => IsSpectrumComputingUrl(url),
            RomDownloadSource.ArchiveOrgSoftwareLibrary => IsSoftwareLibraryUrl(url),
            RomDownloadSource.ArchiveOrgTosec => ArchiveOrgTosecTap.IsTosecUrl(url),
            _ => false
        };
    }

    public static bool IsSpectrumComputingUrl(string url) =>
        url.Contains("spectrumcomputing.co.uk", StringComparison.OrdinalIgnoreCase)
        || url.Contains("zxdb.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Archive.org que no es el set TOSEC LadyEklipse.</summary>
    public static bool IsSoftwareLibraryUrl(string url) =>
        url.Contains("archive.org", StringComparison.OrdinalIgnoreCase)
        && !ArchiveOrgTosecTap.IsTosecUrl(url);

    public static RomDownloadSource? TryDetectFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (IsSpectrumComputingUrl(url)) return RomDownloadSource.SpectrumComputing;
        if (ArchiveOrgTosecTap.IsTosecUrl(url)) return RomDownloadSource.ArchiveOrgTosec;
        if (IsSoftwareLibraryUrl(url)) return RomDownloadSource.ArchiveOrgSoftwareLibrary;
        return null;
    }

    public static IReadOnlyList<RomDownloadSource> All { get; } =
    [
        RomDownloadSource.SpectrumComputing,
        RomDownloadSource.ArchiveOrgSoftwareLibrary,
        RomDownloadSource.ArchiveOrgTosec,
    ];

    /// <summary>
    /// Orden de cascada: <paramref name="preferred"/> primero, luego SC → Library → TOSEC
    /// sin duplicar.
    /// </summary>
    public static IReadOnlyList<RomDownloadSource> CascadeOrder(
        RomDownloadSource preferred = RomDownloadSource.SpectrumComputing)
    {
        var list = new List<RomDownloadSource>(3) { preferred };
        foreach (var s in All)
        {
            if (!list.Contains(s))
                list.Add(s);
        }
        return list;
    }
}
