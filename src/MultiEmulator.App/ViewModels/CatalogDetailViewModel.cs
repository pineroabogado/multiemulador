using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiEmulator.App.Views;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.ViewModels;

/// <summary>
/// Visor de ficha: thumbs principales desde scrape de la página SC
/// (+ cascada plantilla como fallback), lista Additional Downloads, lightbox.
/// </summary>
public partial class CatalogDetailViewModel : ViewModelBase, IDisposable
{
    private readonly CatalogMediaCache _cache;
    private readonly SpectrumComputingEntryScraper _scraper;
    private CancellationTokenSource? _loadCts;
    private string? _knownLoadUrl;
    private string? _knownInGameUrl;
    private string? _knownCoverUrl;
    private readonly List<string> _knownAdditionalUrls = new();

    [ObservableProperty] private string _title = "Selecciona un juego";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _hasSelection;

    [ObservableProperty] private Bitmap? _loadImage;
    [ObservableProperty] private Bitmap? _inGameImage;
    [ObservableProperty] private Bitmap? _coverImage;

    [ObservableProperty] private string _loadStatus = "—";
    [ObservableProperty] private string _inGameStatus = "—";
    [ObservableProperty] private string _coverStatus = "—";

    [ObservableProperty] private string _availabilityBadge = "";
    [ObservableProperty] private string _availabilityKind = "";
    [ObservableProperty] private bool _showAvailabilityBadge;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private string _favoriteLabel = "☆ Favorito";
    [ObservableProperty] private string _mediaSourceNote = "";
    [ObservableProperty] private bool _hasAdditionalFiles;

    public ObservableCollection<CatalogGalleryItemViewModel> ExtraGallery { get; } = new();

    public CatalogDetailViewModel(CatalogMediaCache? cache = null, SpectrumComputingEntryScraper? scraper = null)
    {
        _cache = cache ?? new CatalogMediaCache();
        _scraper = scraper ?? new SpectrumComputingEntryScraper();
    }

    public void Clear()
    {
        CancelLoad();
        Title = "Selecciona un juego";
        Subtitle = "";
        HasSelection = false;
        ClearBitmaps();
        ClearExtras();
        LoadStatus = "—";
        InGameStatus = "—";
        CoverStatus = "—";
        AvailabilityBadge = "";
        AvailabilityKind = "";
        ShowAvailabilityBadge = false;
        IsFavorite = false;
        FavoriteLabel = "☆ Favorito";
        MediaSourceNote = "";
        HasAdditionalFiles = false;
        ClearKnownMediaUrls();
    }

    /// <summary>URLs de media conocidas de la ficha actual (scrape), para persistir al jugar.</summary>
    public CatalogGameMediaUrls? CaptureKnownMediaUrls()
    {
        if (string.IsNullOrWhiteSpace(_knownLoadUrl) &&
            string.IsNullOrWhiteSpace(_knownInGameUrl) &&
            string.IsNullOrWhiteSpace(_knownCoverUrl) &&
            _knownAdditionalUrls.Count == 0)
            return null;

        return new CatalogGameMediaUrls
        {
            LoadScreenUrl = _knownLoadUrl,
            InGameUrl = _knownInGameUrl,
            CoverUrl = _knownCoverUrl,
            AdditionalMediaUrls = _knownAdditionalUrls.Count == 0
                ? null
                : _knownAdditionalUrls.ToList()
        };
    }

    public void LoadEntry(CatalogEntry? entry, bool isFavorite = false)
    {
        CancelLoad();
        if (entry == null)
        {
            Clear();
            return;
        }

        HasSelection = true;
        Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Id : entry.Title;
        Subtitle = BuildSubtitle(entry);
        var (badge, kind) = SpectrumComputingMedia.ResolveAvailabilityBadge(entry);
        AvailabilityBadge = badge;
        AvailabilityKind = kind;
        ShowAvailabilityBadge = !string.IsNullOrWhiteSpace(badge);
        SetFavoriteState(isFavorite);

        ClearBitmaps();
        ClearExtras();
        ClearKnownMediaUrls();
        LoadStatus = "Cargando…";
        InGameStatus = "Cargando…";
        CoverStatus = "Cargando…";
        MediaSourceNote = "Obteniendo ficha Spectrum Computing…";
        HasAdditionalFiles = false;

        _loadCts = new CancellationTokenSource();
        _ = LoadAllAsync(entry, _loadCts.Token);
    }

    private void ClearKnownMediaUrls()
    {
        _knownLoadUrl = null;
        _knownInGameUrl = null;
        _knownCoverUrl = null;
        _knownAdditionalUrls.Clear();
    }

    public void SetFavoriteState(bool favorite)
    {
        IsFavorite = favorite;
        FavoriteLabel = favorite ? "★ Favorito" : "☆ Favorito";
    }

    private static string BuildSubtitle(CatalogEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Machine))
            parts.Add(entry.Machine);
        if (entry.ScId is int id and > 0)
            parts.Add($"SC #{id}");
        return string.Join(" · ", parts);
    }

    private async Task LoadAllAsync(CatalogEntry entry, CancellationToken ct)
    {
        IReadOnlyList<SpectrumComputingFileItem> scraped = Array.Empty<SpectrumComputingFileItem>();
        try
        {
            if (SpectrumComputingEntryScraper.ResolveEntryPageUrl(entry) != null)
                scraped = await _scraper.GetAdditionalFilesAsync(entry, ct);
        }
        catch (OperationCanceledException) { return; }
        catch
        {
            scraped = Array.Empty<SpectrumComputingFileItem>();
        }

        if (ct.IsCancellationRequested) return;

        if (scraped.Count > 0)
        {
            try { CatalogGameMetadataStore.MergeScrapedMedia(entry, scraped); }
            catch { /* persistencia best-effort */ }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            MediaSourceNote = scraped.Count > 0
                ? $"Ficha SC: {scraped.Count} archivos adicionales"
                : "Sin scrape (plantillas URL)";
        });

        // Preferencias: scrape fresco + metadatos persistidos del proyecto
        var preferred = new Dictionary<CatalogMediaKind, List<string>>();
        void Prefer(CatalogMediaKind k, SpectrumComputingFileItem item)
        {
            if (!preferred.TryGetValue(k, out var list))
            {
                list = new List<string>();
                preferred[k] = list;
            }
            // Para carga: preferir .scr (decode nativo), luego preview, luego href
            if (k == CatalogMediaKind.LoadScreen)
            {
                if (item.AbsoluteUrl.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
                    list.Insert(0, item.AbsoluteUrl);
                else
                    list.Add(item.AbsoluteUrl);
                if (!string.IsNullOrWhiteSpace(item.PreviewUrl))
                    list.Add(item.PreviewUrl!);
            }
            else
            {
                list.Add(item.AbsoluteUrl);
                if (!string.IsNullOrWhiteSpace(item.PreviewUrl) &&
                    !string.Equals(item.PreviewUrl, item.AbsoluteUrl, StringComparison.OrdinalIgnoreCase))
                    list.Add(item.PreviewUrl!);
            }
        }

        foreach (var item in scraped)
        {
            var slot = item.MapToMainSlot();
            if (slot is CatalogMediaKind mk &&
                (mk == CatalogMediaKind.LoadScreen || mk == CatalogMediaKind.InGame || mk == CatalogMediaKind.Cover))
            {
                // Solo el primero de cada tipo principal (cover = primer Inlay Front)
                if (!preferred.ContainsKey(mk) || mk != CatalogMediaKind.Cover || preferred[mk].Count == 0)
                    Prefer(mk, item);
                else if (mk == CatalogMediaKind.Cover)
                    Prefer(mk, item); // acumular variantes front como candidatas
            }
        }

        // Añadir URLs persistidas del proyecto (si el scrape no las trajo).
        void PrefersPersisted(CatalogMediaKind k)
        {
            foreach (string u in CatalogGameMetadataStore.GetPreferredMediaUrls(entry, k))
            {
                if (!preferred.TryGetValue(k, out var list))
                {
                    list = new List<string>();
                    preferred[k] = list;
                }
                if (!list.Contains(u, StringComparer.OrdinalIgnoreCase))
                    list.Add(u);
            }
        }
        PrefersPersisted(CatalogMediaKind.LoadScreen);
        PrefersPersisted(CatalogMediaKind.InGame);
        PrefersPersisted(CatalogMediaKind.Cover);

        // Recordar URLs scrapeadas/persistidas para guardar metadatos al jugar.
        _knownLoadUrl = preferred.GetValueOrDefault(CatalogMediaKind.LoadScreen)?.FirstOrDefault();
        _knownInGameUrl = preferred.GetValueOrDefault(CatalogMediaKind.InGame)?.FirstOrDefault();
        _knownCoverUrl = preferred.GetValueOrDefault(CatalogMediaKind.Cover)?.FirstOrDefault();
        _knownAdditionalUrls.Clear();
        foreach (var item in scraped.Where(i => i.IsImageLike))
        {
            if (!string.IsNullOrWhiteSpace(item.AbsoluteUrl) &&
                !_knownAdditionalUrls.Contains(item.AbsoluteUrl, StringComparer.OrdinalIgnoreCase))
                _knownAdditionalUrls.Add(item.AbsoluteUrl);
            if (!string.IsNullOrWhiteSpace(item.PreviewUrl) &&
                !_knownAdditionalUrls.Contains(item.PreviewUrl!, StringComparer.OrdinalIgnoreCase))
                _knownAdditionalUrls.Add(item.PreviewUrl!);
        }

        var mainTasks = new[]
        {
            LoadSlotAsync(entry, CatalogMediaKind.LoadScreen,
                preferred.GetValueOrDefault(CatalogMediaKind.LoadScreen), ct),
            LoadSlotAsync(entry, CatalogMediaKind.InGame,
                preferred.GetValueOrDefault(CatalogMediaKind.InGame), ct),
            LoadSlotAsync(entry, CatalogMediaKind.Cover,
                preferred.GetValueOrDefault(CatalogMediaKind.Cover), ct)
        };
        await Task.WhenAll(mainTasks);
        if (ct.IsCancellationRequested) return;

        // Lista Additional: todos los image-like del scrape; si no hay scrape, extras plantilla
        if (scraped.Count > 0)
        {
            var imageItems = scraped.Where(i => i.IsImageLike).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                HasAdditionalFiles = imageItems.Count > 0;
            });

            await Task.WhenAll(imageItems.Select(item => LoadScrapedGalleryItemAsync(entry, item, ct)));
        }
        else
        {
            var extras = new[] { CatalogMediaKind.CoverBack, CatalogMediaKind.SideA, CatalogMediaKind.SideB };
            await Task.WhenAll(extras.Select(k => LoadTemplateExtraAsync(entry, k, ct)));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                    HasAdditionalFiles = ExtraGallery.Count > 0;
            });
        }
    }

    private async Task LoadSlotAsync(
        CatalogEntry entry,
        CatalogMediaKind kind,
        IReadOnlyList<string>? preferredUrls,
        CancellationToken ct)
    {
        try
        {
            // Preferir URLs durables del proyecto + scrape de la ficha.
            var merged = new List<string>();
            foreach (string u in CatalogGameMetadataStore.GetPreferredMediaUrls(entry, kind))
            {
                if (!merged.Contains(u, StringComparer.OrdinalIgnoreCase))
                    merged.Add(u);
            }
            if (preferredUrls != null)
            {
                foreach (string u in preferredUrls)
                {
                    if (!string.IsNullOrWhiteSpace(u) &&
                        !merged.Contains(u, StringComparer.OrdinalIgnoreCase))
                        merged.Add(u);
                }
            }

            string? path = await _cache.TryGetPathAsync(
                entry, kind, ct, merged.Count > 0 ? merged : preferredUrls);
            if (ct.IsCancellationRequested) return;

            if (path == null)
            {
                await SetSlotAsync(kind, null, "Sin imagen", ct);
                return;
            }

            Bitmap? bmp = await DecodePathAsync(path, ct);
            if (ct.IsCancellationRequested)
            {
                bmp?.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    bmp?.Dispose();
                    return;
                }
                ApplySlot(kind, bmp, bmp == null ? "Error al leer" : "");
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!ct.IsCancellationRequested)
                await SetSlotAsync(kind, null, "Error", ct);
        }
    }

    private async Task LoadScrapedGalleryItemAsync(
        CatalogEntry entry,
        SpectrumComputingFileItem item,
        CancellationToken ct)
    {
        var vm = CatalogGalleryItemViewModel.FromScraped(item, OpenGalleryLightbox);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ct.IsCancellationRequested)
                ExtraGallery.Add(vm);
        });

        try
        {
            string cacheName = "afd_" + Path.GetFileNameWithoutExtension(
                string.IsNullOrWhiteSpace(item.FileName) ? Guid.NewGuid().ToString("N") : item.FileName);

            // Preferir archivo real; para .scr usar el .scr (decode nativo). Preview si falla.
            string? path = null;
            if (item.AbsoluteUrl.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
            {
                path = await _cache.TryDownloadUrlAsync(entry, item.AbsoluteUrl, cacheName, ct);
                if (path == null && !string.IsNullOrWhiteSpace(item.PreviewUrl))
                    path = await _cache.TryDownloadUrlAsync(entry, item.PreviewUrl!, cacheName + "_prev", ct);
            }
            else
            {
                path = await _cache.TryDownloadUrlAsync(entry, item.AbsoluteUrl, cacheName, ct);
                if (path == null && !string.IsNullOrWhiteSpace(item.PreviewUrl))
                    path = await _cache.TryDownloadUrlAsync(entry, item.PreviewUrl!, cacheName + "_prev", ct);
            }

            if (ct.IsCancellationRequested) return;

            if (path == null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => vm.Status = "Sin imagen");
                return;
            }

            Bitmap? bmp = await DecodePathAsync(path, ct);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    bmp?.Dispose();
                    return;
                }
                if (bmp == null)
                {
                    vm.Status = "Error al leer";
                    return;
                }
                vm.Image = bmp;
                vm.Status = "";
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                    vm.Status = "Error";
            });
        }
    }

    private async Task LoadTemplateExtraAsync(CatalogEntry entry, CatalogMediaKind kind, CancellationToken ct)
    {
        try
        {
            string? path = await _cache.TryGetPathAsync(entry, kind, ct);
            if (ct.IsCancellationRequested || path == null) return;

            Bitmap? bmp = await DecodePathAsync(path, ct);
            if (ct.IsCancellationRequested || bmp == null)
            {
                bmp?.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    bmp.Dispose();
                    return;
                }
                var vm = new CatalogGalleryItemViewModel(
                    SpectrumComputingMedia.DisplayLabel(kind), "", "", path, kind,
                    OpenGalleryLightbox)
                {
                    Image = bmp,
                    Status = ""
                };
                ExtraGallery.Add(vm);
            });
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static Task<Bitmap?> DecodePathAsync(string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (path.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
                return DecodeScrToBitmap(path);
            return new Bitmap(path);
        }, ct);

    private static Bitmap? DecodeScrToBitmap(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (!ScrDecoder.LooksLikeScr(data))
                return null;

            uint[] argb = ScrDecoder.ToArgb(data.AsSpan(0, ScrDecoder.ScrSize));
            var bmp = new WriteableBitmap(
                new PixelSize(ScrDecoder.Width, ScrDecoder.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var fb = bmp.Lock())
            {
                unsafe
                {
                    var dest = (uint*)fb.Address;
                    int stridePixels = fb.RowBytes / 4;
                    for (int y = 0; y < ScrDecoder.Height; y++)
                    {
                        for (int x = 0; x < ScrDecoder.Width; x++)
                        {
                            // AARRGGBB en LE = bytes BGRA
                            dest[y * stridePixels + x] = argb[y * ScrDecoder.Width + x];
                        }
                    }
                }
            }

            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private async Task SetSlotAsync(CatalogMediaKind kind, Bitmap? bmp, string status, CancellationToken ct)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            ApplySlot(kind, bmp, status);
        });
    }

    private void ApplySlot(CatalogMediaKind kind, Bitmap? bmp, string status)
    {
        switch (kind)
        {
            case CatalogMediaKind.LoadScreen:
                DisposeBitmap(LoadImage);
                LoadImage = bmp;
                LoadStatus = status;
                break;
            case CatalogMediaKind.InGame:
                DisposeBitmap(InGameImage);
                InGameImage = bmp;
                InGameStatus = status;
                break;
            case CatalogMediaKind.Cover:
                DisposeBitmap(CoverImage);
                CoverImage = bmp;
                CoverStatus = status;
                break;
        }
    }

    [RelayCommand]
    private async Task OpenLightbox(string? slot)
    {
        Bitmap? bmp = slot?.ToLowerInvariant() switch
        {
            "load" => LoadImage,
            "ingame" => InGameImage,
            "cover" => CoverImage,
            _ => null
        };
        string label = slot?.ToLowerInvariant() switch
        {
            "load" => "Carga",
            "ingame" => "In-game",
            "cover" => "Carátula",
            _ => "Imagen"
        };
        if (bmp == null) return;
        await ShowLightboxAsync($"{Title} — {label}", bmp);
    }

    [RelayCommand]
    private async Task OpenGalleryLightbox(CatalogGalleryItemViewModel? item)
    {
        if (item?.Image == null) return;
        await ShowLightboxAsync($"{Title} — {item.Label}", item.Image);
    }

    private static async Task ShowLightboxAsync(string title, Bitmap image)
    {
        var win = new MediaLightboxWindow(title, image);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
            await win.ShowDialog(desktop.MainWindow);
        else
            win.Show();
    }

    private void ClearBitmaps()
    {
        DisposeBitmap(LoadImage);
        DisposeBitmap(InGameImage);
        DisposeBitmap(CoverImage);
        LoadImage = null;
        InGameImage = null;
        CoverImage = null;
    }

    private void ClearExtras()
    {
        foreach (var item in ExtraGallery)
            DisposeBitmap(item.Image);
        ExtraGallery.Clear();
    }

    private static void DisposeBitmap(Bitmap? bmp)
    {
        try { bmp?.Dispose(); } catch { /* ignore */ }
    }

    private void CancelLoad()
    {
        try { _loadCts?.Cancel(); } catch { /* ignore */ }
        try { _loadCts?.Dispose(); } catch { /* ignore */ }
        _loadCts = null;
    }

    public void Dispose()
    {
        CancelLoad();
        ClearBitmaps();
        ClearExtras();
    }
}
