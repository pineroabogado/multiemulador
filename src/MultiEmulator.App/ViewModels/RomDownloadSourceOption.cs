using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.ViewModels;

/// <summary>Ítem del ComboBox «Fuente ROM» (preferida primero; luego cascada).</summary>
public sealed class RomDownloadSourceOption
{
    public RomDownloadSourceOption(RomDownloadSource source)
    {
        Source = source;
        DisplayName = RomDownloadSourceInfo.DisplayName(source);
    }

    public RomDownloadSource Source { get; }
    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
