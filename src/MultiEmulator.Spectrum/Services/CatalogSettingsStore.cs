using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Ajustes de catálogo en
/// <c>%LocalAppData%\MULTIEMULADOR\{platform}\catalog-settings.json</c>
/// (ZX: carpeta histórica <c>Spectrum</c>).
/// </summary>
public sealed class CatalogSettings
{
    [JsonPropertyName("romDownloadSource")]
    public RomDownloadSource RomDownloadSource { get; set; } = RomDownloadSource.SpectrumComputing;

    /// <summary>Preferencia de formato (Auto / TAP / TZX / Z80·SNA).</summary>
    [JsonPropertyName("romMediaFormat")]
    public RomMediaFormat RomMediaFormat { get; set; } = RomMediaFormat.Auto;
}

public static class CatalogSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string FilePath => Path.Combine(AppPaths.Root, "catalog-settings.json");

    public static CatalogSettings Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(FilePath))
                return new CatalogSettings();

            string json = File.ReadAllText(FilePath);
            var s = JsonSerializer.Deserialize<CatalogSettings>(json, JsonOpts);
            return s ?? new CatalogSettings();
        }
        catch
        {
            return new CatalogSettings();
        }
    }

    public static void Save(CatalogSettings settings)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    public static void SaveRomDownloadSource(RomDownloadSource source)
    {
        var s = Load();
        s.RomDownloadSource = source;
        Save(s);
    }

    public static void SaveRomMediaFormat(RomMediaFormat format)
    {
        var s = Load();
        s.RomMediaFormat = format;
        Save(s);
    }
}
