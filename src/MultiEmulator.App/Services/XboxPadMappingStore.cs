using System.Text.Json;
using MultiEmulator.App.Input;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.Services;

public sealed class XboxPadMappingSettings
{
    public bool MapPadToKeyboard { get; set; } = true;
    public Dictionary<string, string[]> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class XboxPadMappingStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string FilePath => Path.Combine(AppPaths.Root, "xbox-pad-mapping.json");

    public static XboxPadMappingSettings Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(FilePath))
                return CreateDefaultSettings();
            string json = File.ReadAllText(FilePath);
            var s = JsonSerializer.Deserialize<XboxPadMappingSettings>(json, JsonOpts);
            return s ?? CreateDefaultSettings();
        }
        catch
        {
            return CreateDefaultSettings();
        }
    }

    public static void Save(XboxPadMapping mapping, bool mapPadToKeyboard)
    {
        AppPaths.EnsureCreated();
        var settings = new XboxPadMappingSettings
        {
            MapPadToKeyboard = mapPadToKeyboard,
            Bindings = mapping.SnapshotForSave()
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    private static XboxPadMappingSettings CreateDefaultSettings() => new()
    {
        MapPadToKeyboard = true,
        Bindings = XboxPadMapping.CreateDefaultBindings()
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value, StringComparer.OrdinalIgnoreCase)
    };
}
