using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.ViewModels;

/// <summary>Ítem del ComboBox «Formato».</summary>
public sealed class RomMediaFormatOption
{
    public RomMediaFormatOption(RomMediaFormat format)
    {
        Format = format;
        DisplayName = RomMediaFormatInfo.DisplayName(format);
    }

    public RomMediaFormat Format { get; }
    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
