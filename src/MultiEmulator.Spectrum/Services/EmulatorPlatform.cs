namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Familia de emulador / sistema del catálogo.
/// Solo ZX Spectrum tiene catálogo activo hoy; el resto son placeholders.
/// </summary>
public enum EmulatorPlatform
{
    ZxSpectrum = 0,
    AmstradCpc = 1,
    Msx = 2,
}

/// <summary>Metadatos UI/rutas de cada <see cref="EmulatorPlatform"/>.</summary>
public static class EmulatorPlatformInfo
{
    public static IReadOnlyList<EmulatorPlatform> All { get; } =
    [
        EmulatorPlatform.ZxSpectrum,
        EmulatorPlatform.AmstradCpc,
        EmulatorPlatform.Msx,
    ];

    /// <summary>Id estable en rutas y JSON (p.ej. <c>zx-spectrum</c>).</summary>
    public static string Id(EmulatorPlatform platform) => platform switch
    {
        EmulatorPlatform.ZxSpectrum => "zx-spectrum",
        EmulatorPlatform.AmstradCpc => "amstrad-cpc",
        EmulatorPlatform.Msx => "msx",
        _ => "unknown",
    };

    public static string DisplayName(EmulatorPlatform platform) => platform switch
    {
        EmulatorPlatform.ZxSpectrum => "ZX Spectrum",
        EmulatorPlatform.AmstradCpc => "Amstrad CPC",
        EmulatorPlatform.Msx => "MSX",
        _ => platform.ToString(),
    };

    /// <summary>
    /// Carpeta bajo <c>%LocalAppData%\MULTIEMULADOR\</c>.
    /// ZX mantiene <c>Spectrum</c> por compatibilidad con datos existentes.
    /// </summary>
    public static string LocalAppFolder(EmulatorPlatform platform) => platform switch
    {
        EmulatorPlatform.ZxSpectrum => "Spectrum",
        EmulatorPlatform.AmstradCpc => "AmstradCpc",
        EmulatorPlatform.Msx => "Msx",
        _ => Id(platform),
    };

    /// <summary>True si el catálogo de esa plataforma está cableado (hoy solo ZX).</summary>
    public static bool IsCatalogActive(EmulatorPlatform platform) =>
        platform == EmulatorPlatform.ZxSpectrum;

    public static EmulatorPlatform ParseId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return EmulatorPlatform.ZxSpectrum;

        return id.Trim().ToLowerInvariant() switch
        {
            "zx-spectrum" or "zxspectrum" or "spectrum" or "zx" => EmulatorPlatform.ZxSpectrum,
            "amstrad-cpc" or "amstradcpc" or "amstrad" or "cpc" => EmulatorPlatform.AmstradCpc,
            "msx" => EmulatorPlatform.Msx,
            _ => EmulatorPlatform.ZxSpectrum,
        };
    }

    public static bool TryParseId(string? id, out EmulatorPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            platform = EmulatorPlatform.ZxSpectrum;
            return false;
        }

        platform = ParseId(id);
        string normalized = id.Trim().ToLowerInvariant();
        return normalized is "zx-spectrum" or "zxspectrum" or "spectrum" or "zx"
            or "amstrad-cpc" or "amstradcpc" or "amstrad" or "cpc"
            or "msx";
    }
}
