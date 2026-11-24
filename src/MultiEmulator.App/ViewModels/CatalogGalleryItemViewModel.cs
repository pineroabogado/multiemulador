using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.ViewModels;

/// <summary>Ítem de la lista Additional File Downloads / galería extra.</summary>
public partial class CatalogGalleryItemViewModel : ViewModelBase
{
    public CatalogMediaKind? Kind { get; }
    public string Label { get; }
    public string Description { get; }
    public string FileName { get; }
    public string AbsoluteUrl { get; }

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _status = "Cargando…";

    /// <summary>Abre el lightbox; lo cablea CatalogDetailViewModel al crear el ítem.</summary>
    public ICommand OpenCommand { get; }

    public string MetaLine
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(FileName))
                return $"{Description} · {FileName}";
            if (!string.IsNullOrWhiteSpace(FileName))
                return FileName;
            return Description;
        }
    }

    public CatalogGalleryItemViewModel(
        string label,
        string description,
        string fileName,
        string absoluteUrl,
        CatalogMediaKind? kind = null,
        Func<CatalogGalleryItemViewModel, Task>? openAsync = null)
    {
        Label = label;
        Description = description ?? "";
        FileName = fileName ?? "";
        AbsoluteUrl = absoluteUrl;
        Kind = kind;
        OpenCommand = new AsyncRelayCommand(async () =>
        {
            if (openAsync != null)
                await openAsync(this);
        });
    }

    public static CatalogGalleryItemViewModel FromScraped(
        SpectrumComputingFileItem item,
        Func<CatalogGalleryItemViewModel, Task>? openAsync = null) =>
        new(item.Type, item.Description, item.FileName, item.AbsoluteUrl, item.MapToMainSlot(), openAsync);
}
