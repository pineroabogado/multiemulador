using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiEmulator.App.Services;
using MultiEmulator.App.Views;
using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Poke;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly EmulatorHost _host = new();
    private readonly CatalogFavoritesStore _favorites = new();
    private EmulatorPlatform _catalogPlatform = EmulatorPlatform.ZxSpectrum;

    [ObservableProperty] private WriteableBitmap? _screen;
    [ObservableProperty] private string _status = "Iniciando…";
    [ObservableProperty] private string _catalogQuery = "";
    [ObservableProperty] private string _pokeAddress = "23675";
    [ObservableProperty] private string _pokeValue = "0";
    [ObservableProperty] private string _romNotice = "";
    [ObservableProperty] private JoystickType _joystickType = JoystickType.Kempston;
    [ObservableProperty] private bool _mapPadToKeyboard = true;
    [ObservableProperty] private CatalogEntry? _selectedEntry;
    [ObservableProperty] private bool _toolsOpen;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private string _muteLabel = "Audio";
    [ObservableProperty] private string _tapeLabel = "Sin cinta";
    [ObservableProperty] private bool _catalogRebuildBusy;
    [ObservableProperty] private string _catalogRebuildStatus = "";
    [ObservableProperty] private string _catalogListSummary = "Mostrando 0 juegos";
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private IBrush _borderBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0xD7));
    [ObservableProperty] private string _catalogPlatformHint = "";
    [ObservableProperty] private bool _catalogActionsEnabled = true;
    [ObservableProperty] private RomDownloadSourceOption? _selectedRomDownloadSource;
    [ObservableProperty] private RomMediaFormatOption? _selectedRomMediaFormat;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _isProgressVisible;
    [ObservableProperty] private bool _isProgressIndeterminate;

    public ObservableCollection<CatalogEntry> CatalogResults { get; } = new();
    public ObservableCollection<CatalogPlatformItemViewModel> CatalogPlatforms { get; } = new();
    public ObservableCollection<RomDownloadSourceOption> RomDownloadSources { get; } = new();
    public ObservableCollection<RomMediaFormatOption> RomMediaFormats { get; } = new();

    public CatalogDetailViewModel CatalogDetail { get; } = new();

    public EmulatorHost Host => _host;

    /// <summary>
    /// La vista debe devolver el foco a la pantalla Spectrum (p. ej. tras catálogo).
    /// </summary>
    public event Action? EmulatorKeyboardFocusRequested;

    private void RequestEmulatorKeyboardFocus() =>
        EmulatorKeyboardFocusRequested?.Invoke();

    public MainViewModel()
    {
        foreach (var p in EmulatorPlatformInfo.All)
        {
            var item = new CatalogPlatformItemViewModel(p)
            {
                IsSelected = p == EmulatorPlatform.ZxSpectrum
            };
            CatalogPlatforms.Add(item);
        }

        foreach (var src in RomDownloadSourceInfo.All)
            RomDownloadSources.Add(new RomDownloadSourceOption(src));
        foreach (var fmt in RomMediaFormatInfo.All)
            RomMediaFormats.Add(new RomMediaFormatOption(fmt));

        _favorites.Platform = EmulatorPlatform.ZxSpectrum;
        try { _favorites.Load(); }
        catch { /* favoritos corruptos no deben tumbar el arranque */ }

        RomDownloadSource persistedSource = RomDownloadSource.SpectrumComputing;
        RomMediaFormat persistedFormat = RomMediaFormat.Auto;
        try
        {
            var settings = CatalogSettingsStore.Load();
            persistedSource = settings.RomDownloadSource;
            persistedFormat = settings.RomMediaFormat;
        }
        catch { /* ajustes corruptos → defaults */ }

        SelectedRomDownloadSource = RomDownloadSources.FirstOrDefault(o => o.Source == persistedSource)
                                    ?? RomDownloadSources[0];
        SelectedRomMediaFormat = RomMediaFormats.FirstOrDefault(o => o.Format == persistedFormat)
                                 ?? RomMediaFormats[0];

        ApplyCatalogPlatformUi(EmulatorPlatform.ZxSpectrum);
        _ = InitAsync();
    }

    partial void OnSelectedRomDownloadSourceChanged(RomDownloadSourceOption? value)
    {
        if (value == null) return;
        try { CatalogSettingsStore.SaveRomDownloadSource(value.Source); }
        catch { /* no tumbar UI si falla el guardado */ }
    }

    partial void OnSelectedRomMediaFormatChanged(RomMediaFormatOption? value)
    {
        if (value == null) return;
        try { CatalogSettingsStore.SaveRomMediaFormat(value.Format); }
        catch { /* no tumbar UI si falla el guardado */ }
    }

    partial void OnSelectedEntryChanged(CatalogEntry? value) =>
        CatalogDetail.LoadEntry(value, value != null && _favorites.IsFavorite(value));

    partial void OnCatalogQueryChanged(string value) => RefreshCatalog();

    partial void OnFavoritesOnlyChanged(bool value) => RefreshCatalog();

    private async Task InitAsync()
    {
        try
        {
            Status = "Cargando Spectrum 48K…";
            _host.ProgressChanged += SyncProgressFromHost;
            _host.FrameRendered += () =>
            {
                if (!ReferenceEquals(Screen, _host.Bitmap))
                    Screen = _host.Bitmap;
                if (Status != _host.Status)
                    Status = _host.Status;
                string tape = _host.TapeTransportLabel;
                if (TapeLabel != tape)
                    TapeLabel = tape;
                uint argb = _host.Machine.Ula.BorderArgb;
                BorderBrush = new SolidColorBrush(Color.FromUInt32(argb));
                SyncProgressFromHost();
            };

            // Ceder el hilo UI antes del boot (ctor corre en UI).
            await Task.Yield();
            await _host.InitializeAsync().ConfigureAwait(true);

            MapPadToKeyboard = _host.MapPadToKeyboard;
            RomNotice = _host.RomNotice;
            Screen = _host.Bitmap;
            Status = string.IsNullOrWhiteSpace(_host.Status) ? "Listo" : _host.Status;
            try { RefreshCatalog(); }
            catch { Status = $"{Status} · Catálogo no disponible"; }
            _ = RefreshCatalogWhenReadyAsync();
            _ = EnsureFullSpectrumComputingCatalogAsync();
        }
        catch (Exception ex)
        {
            // Nunca dejar la UI colgada en «Iniciando…» / «Cargando…».
            Status = "Error al iniciar: " + ex.Message;
        }
        finally
        {
            // Garantizar salida de «Cargando Spectrum 48K…» aunque el host no actualice.
            if (Status.StartsWith("Cargando Spectrum", StringComparison.OrdinalIgnoreCase))
            {
                string hostStatus = _host.Status;
                Status = string.IsNullOrWhiteSpace(hostStatus)
                         || hostStatus.StartsWith("Cargando", StringComparison.OrdinalIgnoreCase)
                    ? "Listo"
                    : hostStatus;
            }
        }
    }

    private async Task RefreshCatalogWhenReadyAsync()
    {
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(200);
            var items = FilterCatalog(_host.Catalog.Search(CatalogQuery)).ToList();
            if (items.Count == 0 && i < 49) continue;
            CatalogResults.Clear();
            foreach (var e in items)
                CatalogResults.Add(e);
            UpdateCatalogListSummary();
            if (items.Count > 0) break;
        }
    }

    private IEnumerable<CatalogEntry> FilterCatalog(IEnumerable<CatalogEntry> source)
    {
        foreach (var e in source)
        {
            if (FavoritesOnly && !_favorites.IsFavorite(e))
                continue;
            yield return e;
        }
    }

    [RelayCommand]
    private void RefreshCatalog()
    {
        var keep = SelectedEntry;
        CatalogResults.Clear();
        foreach (var e in FilterCatalog(_host.Catalog.Search(CatalogQuery)))
            CatalogResults.Add(e);
        UpdateCatalogListSummary();

        if (keep != null)
        {
            var match = CatalogResults.FirstOrDefault(e =>
                string.Equals(e.Id, keep.Id, StringComparison.OrdinalIgnoreCase) ||
                (e.ScId is int a && keep.ScId is int b && a == b));
            if (match != null)
                SelectedEntry = match;
        }
    }

    private void UpdateCatalogListSummary()
    {
        int n = CatalogResults.Count;
        CatalogListSummary = FavoritesOnly
            ? $"Mostrando {n} favorito{(n == 1 ? "" : "s")}"
            : $"Mostrando {n} juego{(n == 1 ? "" : "s")}";
    }

    [RelayCommand]
    private async Task SelectCatalogPlatformAsync(CatalogPlatformItemViewModel? item)
    {
        if (item == null) return;

        if (!item.IsCatalogActive)
        {
            CatalogPlatformHint =
                $"{item.DisplayName}: catálogo próximamente. Por ahora solo ZX Spectrum.";
            Status = CatalogPlatformHint;
            return;
        }

        if (item.Platform == _catalogPlatform)
            return;

        _catalogPlatform = item.Platform;
        foreach (var p in CatalogPlatforms)
            p.IsSelected = p.Platform == item.Platform;

        ApplyCatalogPlatformUi(item.Platform);
        SelectedEntry = null;
        CatalogResults.Clear();
        UpdateCatalogListSummary();
        CatalogRebuildStatus = "Cargando catálogo…";

        try
        {
            _favorites.Platform = item.Platform;
            _favorites.Load();
            await _host.SetCatalogPlatformAsync(item.Platform);
            CatalogQuery = "";
            RefreshCatalog();
            CatalogRebuildStatus = CatalogResults.Count > 0
                ? $"{item.DisplayName}: {CatalogResults.Count} juegos"
                : $"{item.DisplayName}: catálogo vacío";
            Status = CatalogRebuildStatus;
        }
        catch (Exception ex)
        {
            CatalogRebuildStatus = $"Error al cambiar plataforma: {ex.Message}";
            Status = CatalogRebuildStatus;
        }
    }

    private void ApplyCatalogPlatformUi(EmulatorPlatform platform)
    {
        bool active = EmulatorPlatformInfo.IsCatalogActive(platform);
        CatalogActionsEnabled = active;
        CatalogPlatformHint = active
            ? $"Catálogo · {EmulatorPlatformInfo.DisplayName(platform)}"
            : $"{EmulatorPlatformInfo.DisplayName(platform)} · Próximamente";
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedEntry == null || !CatalogActionsEnabled) return;
        bool now = _favorites.Toggle(SelectedEntry);
        CatalogDetail.SetFavoriteState(now);
        Status = now ? $"Favorito: {SelectedEntry.Title}" : $"Quitado de favoritos: {SelectedEntry.Title}";
        if (FavoritesOnly)
            RefreshCatalog();
    }

    /// <summary>
    /// El índice embebido solo trae una semilla (~6 juegos). Si no hay caché SC,
    /// descarga solo el listado A–Z de Spectrum Computing.
    /// </summary>
    private async Task EnsureFullSpectrumComputingCatalogAsync()
    {
        try
        {
            // Dar tiempo a que el host termine de cargar el catálogo embebido/shards.
            await Task.Delay(800);
            if (!_host.Catalog.NeedsFullCatalogRebuild || !CatalogActionsEnabled)
                return;

            CatalogRebuildStatus =
                $"Catálogo incompleto ({CatalogResults.Count} juegos). Descargando listado Spectrum Computing…";
            Status = CatalogRebuildStatus;
            await RebuildCatalogAsync();
        }
        catch (Exception ex)
        {
            CatalogRebuildStatus =
                $"No se pudo completar el listado SC: {ex.Message}. Usa «Rehacer listado».";
            Status = CatalogRebuildStatus;
        }
    }

    [RelayCommand]
    private async Task RebuildCatalogAsync()
    {
        if (CatalogRebuildBusy || !CatalogActionsEnabled) return;
        CatalogRebuildBusy = true;
        CatalogRebuildStatus = "Rehaciendo listado Spectrum Computing…";
        try
        {
            var progress = new Progress<string>(msg =>
            {
                CatalogRebuildStatus = msg;
                Status = msg;
            });
            int n = await _host.RebuildCatalogAsync(progress);
            CatalogQuery = "";
            RefreshCatalog();
            CatalogRebuildStatus = $"Listado Spectrum Computing: {n} juegos";
            Status = CatalogRebuildStatus;
        }
        catch (Exception ex)
        {
            CatalogRebuildStatus = $"Error al rehacer listado: {ex.Message}";
            Status = CatalogRebuildStatus;
        }
        finally
        {
            CatalogRebuildBusy = false;
        }
    }

    [RelayCommand]
    private async Task LaunchSelectedAsync()
    {
        if (SelectedEntry == null) return;

        if (_host.HasLoadedMedia)
        {
            string current = _host.CurrentMediaName ?? "el juego actual";
            bool ok = await ConfirmAsync(
                "¿Cargar otro juego?",
                $"Ya hay un juego en marcha («{current}»).\n\n" +
                $"¿Resetear el emulador y cargar «{SelectedEntry.Title}»?");
            if (!ok)
            {
                Status = "Carga cancelada";
                RequestEmulatorKeyboardFocus();
                return;
            }
        }

        try
        {
            var source = SelectedRomDownloadSource?.Source
                         ?? RomDownloadSource.SpectrumComputing;
            var format = SelectedRomMediaFormat?.Format ?? RomMediaFormat.Auto;
            await _host.OpenCatalogEntryAsync(
                SelectedEntry,
                resetMachine: true,
                CatalogDetail.CaptureKnownMediaUrls(),
                source,
                format);
            Status = _host.Status;
            AppendControlHintIfNeeded(SelectedEntry.Title);
        }
        catch
        {
            // Status ya lo rellena EmulatorHost
            Status = _host.Status;
        }
        // Tras Confirm/TextBox del catálogo: reactivar sondeo Win32 → ULA.
        RequestEmulatorKeyboardFocus();
    }

    public async Task OpenRecentAsync(RecentItem item)
    {
        if (item == null) return;

        if (_host.HasLoadedMedia)
        {
            string current = _host.CurrentMediaName ?? "el juego actual";
            bool ok = await ConfirmAsync(
                "¿Cargar otro juego?",
                $"Ya hay un juego en marcha («{current}»).\n\n" +
                $"¿Resetear el emulador y cargar «{item.DisplayTitle}»?");
            if (!ok)
            {
                Status = "Carga cancelada";
                RequestEmulatorKeyboardFocus();
                return;
            }
        }

        try
        {
            if (item.Kind == RecentKind.Local)
            {
                string? path = item.LocalPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    Status = $"Recientes: no se encuentra «{item.DisplayTitle}»";
                    RequestEmulatorKeyboardFocus();
                    return;
                }

                _host.OpenFile(path, resetMachine: true, recordRecent: true);
            }
            else
            {
                var entry = item.ToCatalogEntry();
                var source = item.RomDownloadSource
                             ?? SelectedRomDownloadSource?.Source
                             ?? RomDownloadSource.SpectrumComputing;
                var format = SelectedRomMediaFormat?.Format ?? RomMediaFormat.Auto;
                await _host.OpenCatalogEntryAsync(
                    entry, resetMachine: true, knownMedia: null, source, format);
            }

            Status = _host.Status;
            AppendControlHintIfNeeded(item.DisplayTitle);
        }
        catch
        {
            // Status ya lo rellena EmulatorHost / OpenFile
            Status = _host.Status;
        }

        RequestEmulatorKeyboardFocus();
    }

    private void SyncProgressFromHost()
    {
        if (IsProgressVisible != _host.IsProgressVisible)
            IsProgressVisible = _host.IsProgressVisible;
        if (IsProgressIndeterminate != _host.IsProgressIndeterminate)
            IsProgressIndeterminate = _host.IsProgressIndeterminate;
        if (ProgressValue != _host.ProgressValue)
            ProgressValue = _host.ProgressValue;
        if (!string.Equals(ProgressText, _host.ProgressText, StringComparison.Ordinal))
            ProgressText = _host.ProgressText;
    }

    /// <summary>
    /// 1942 (Elite) ofrece Kempston / Cursor / Sinclair / teclado en menú.
    /// El teclado PC ya alimenta la matriz; flechas = Cursor (Caps+5/6/7/8).
    /// </summary>
    private void AppendControlHintIfNeeded(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        if (!title.Contains("1942", StringComparison.OrdinalIgnoreCase)) return;
        string baseStatus = _host.Status;
        if (baseStatus.Contains("1942:", StringComparison.Ordinal)) return;
        Status = baseStatus.TrimEnd() +
                 " · 1942: menú → Keyboard o Cursor (flechas+Space) / Kempston (mando)";
    }

    public void ClearRecents()
    {
        try
        {
            _host.Recents.Clear();
            Status = "Recientes limpiados";
        }
        catch (Exception ex)
        {
            Status = "No se pudieron limpiar recientes: " + ex.Message;
        }
    }

    private static async Task<bool> ConfirmAsync(string title, string message)
    {
        var win = new ConfirmWindow(title, message);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            var result = await win.ShowDialog<bool?>(desktop.MainWindow);
            return result == true || win.Confirmed;
        }

        win.Show();
        return false;
    }

    [RelayCommand]
    private void OpenSpectrumComputing()
    {
        if (SelectedEntry == null)
        {
            Status = "Selecciona un juego del catálogo";
            return;
        }

        string? url = SelectedEntry.ResolveSpectrumComputingPageUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            Status = "No hay enlace Spectrum Computing para esta entrada";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            Status = $"Spectrum Computing: {SelectedEntry.Title}";
        }
        catch (Exception ex)
        {
            Status = $"No se pudo abrir el navegador: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Machine48() => _host.SetMachine(MachineType.Spectrum48);

    [RelayCommand]
    private void Machine128() => _host.SetMachine(MachineType.Spectrum128);

    [RelayCommand]
    private void Reset() => _host.Reset();

    [RelayCommand]
    private void Pause() => _host.TogglePause();

    [RelayCommand]
    private async Task SaveState() => await _host.SaveStateAsync();

    [RelayCommand]
    private void ApplyPoke()
    {
        if (!ushort.TryParse(PokeAddress, out var addr)) return;
        if (!byte.TryParse(PokeValue, out var val)) return;
        _host.Machine.ApplyPoke(addr, val);
        Status = $"POKE {addr},{val}";
    }

    [RelayCommand]
    private void SetKempston()
    {
        JoystickType = JoystickType.Kempston;
        _host.Machine.Joystick.Type = JoystickType.Kempston;
    }

    [RelayCommand]
    private void SetSinclair1()
    {
        JoystickType = JoystickType.Sinclair1;
        _host.Machine.Joystick.Type = JoystickType.Sinclair1;
    }

    [RelayCommand]
    private void TogglePadKeyboard()
    {
        MapPadToKeyboard = !MapPadToKeyboard;
        _host.MapPadToKeyboard = MapPadToKeyboard;
        _host.SavePadMappingSettings();
        Status = MapPadToKeyboard ? "Mando = teclado: ON" : "Mando = teclado: OFF";
    }

    [RelayCommand]
    private async Task OpenPadMappingAsync()
    {
        _host.CaptureKeyboard = false;
        _host.ReleaseAllKeys();
        var vm = new PadMappingViewModel(_host, () => Status = "Mapeo mando actualizado (en caliente)");
        var win = new PadMappingWindow(vm);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
            await win.ShowDialog(desktop.MainWindow);
        else
            win.Show();
        _host.CaptureKeyboard = true;
        RequestEmulatorKeyboardFocus();
    }

    [RelayCommand]
    private void ToggleTools() => ToolsOpen = !ToolsOpen;

    partial void OnToolsOpenChanged(bool value)
    {
        // Al cerrar herramientas, devolver teclado al Spectrum.
        if (!value)
            RequestEmulatorKeyboardFocus();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        Muted = !Muted;
        _host.Mute = Muted;
        MuteLabel = Muted ? "Audio off" : "Audio";
        Status = Muted ? "Sonido: silencio" : "Sonido: activado";
    }

    [RelayCommand]
    private void TapePlay()
    {
        _host.TapePlay();
        Status = _host.Status;
        TapeLabel = _host.TapeTransportLabel;
    }

    [RelayCommand]
    private void TapeFastForward()
    {
        _host.TapeFastForward();
        Status = _host.Status;
        TapeLabel = _host.TapeTransportLabel;
    }

    [RelayCommand]
    private void TapeRewind()
    {
        _host.TapeRewind();
        Status = _host.Status;
        TapeLabel = _host.TapeTransportLabel;
    }

    [RelayCommand]
    private void TapeRewindOneBlock()
    {
        _host.TapeRewindOneBlock();
        Status = _host.Status;
        TapeLabel = _host.TapeTransportLabel;
    }

    [RelayCommand]
    private void TapeRewindToStart()
    {
        _host.TapeRewindToStart();
        Status = _host.Status;
        TapeLabel = _host.TapeTransportLabel;
    }

    public void Dispose()
    {
        _host.ProgressChanged -= SyncProgressFromHost;
        CatalogDetail.Dispose();
        _host.Dispose();
    }
}
