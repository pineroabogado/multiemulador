using CommunityToolkit.Mvvm.ComponentModel;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.ViewModels;

public partial class CatalogPlatformItemViewModel : ViewModelBase
{
    public EmulatorPlatform Platform { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public bool IsCatalogActive { get; }
    public string ButtonLabel { get; }

    [ObservableProperty] private bool _isSelected;

    /// <summary>Opacidad visual: activos más nítidos; seleccionada al 100%.</summary>
    public double ButtonOpacity =>
        !IsCatalogActive ? 0.55 : (IsSelected ? 1.0 : 0.8);

    public CatalogPlatformItemViewModel(EmulatorPlatform platform)
    {
        Platform = platform;
        Id = EmulatorPlatformInfo.Id(platform);
        DisplayName = EmulatorPlatformInfo.DisplayName(platform);
        IsCatalogActive = EmulatorPlatformInfo.IsCatalogActive(platform);
        ButtonLabel = IsCatalogActive ? DisplayName : $"{DisplayName} · Próximamente";
    }

    partial void OnIsSelectedChanged(bool value) =>
        OnPropertyChanged(nameof(ButtonOpacity));
}
