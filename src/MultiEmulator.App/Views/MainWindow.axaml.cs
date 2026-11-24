using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MultiEmulator.App.ViewModels;
using MultiEmulator.Spectrum.Poke;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Cualquier control del panel Tools: solo TextBox debe cortar la matriz Spectrum.
        AddHandler(InputElement.GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += (_, _) => WireViewModel();
        Opened += (_, _) =>
        {
            WireViewModel();
            EnableSpectrumKeyboard(stealFocus: true);
            // InvalidateVisual when the framebuffer is blitted (same WriteableBitmap instance).
            Vm.Host.FrameRendered += () => ScreenImage.InvalidateVisual();
        };
        Activated += (_, _) => EnableSpectrumKeyboard(stealFocus: true);
        Deactivated += (_, _) => DisableSpectrumKeyboard();
        ScreenFrame.PointerPressed += (_, _) => EnableSpectrumKeyboard(stealFocus: true);
        ScreenFrame.GotFocus += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.Host.CaptureKeyboard = true;
        };
    }

    private MainViewModel? _wiredVm;
    private MainViewModel Vm => (MainViewModel)DataContext!;

    private void WireViewModel()
    {
        if (DataContext is not MainViewModel vm || ReferenceEquals(_wiredVm, vm))
            return;
        if (_wiredVm != null)
            _wiredVm.EmulatorKeyboardFocusRequested -= OnEmulatorKeyboardFocusRequested;
        _wiredVm = vm;
        vm.EmulatorKeyboardFocusRequested += OnEmulatorKeyboardFocusRequested;
    }

    private void OnEmulatorKeyboardFocusRequested() =>
        EnableSpectrumKeyboard(stealFocus: true);

    /// <summary>
    /// Activa el sondeo Win32 → matriz ULA. Con <paramref name="stealFocus"/> el foco
    /// vuelve a la pantalla (p. ej. tras catálogo / Confirm), diferido para ganar al
    /// restore de foco de Avalonia hacia el TextBox de búsqueda.
    /// </summary>
    private void EnableSpectrumKeyboard(bool stealFocus = false)
    {
        if (DataContext is not MainViewModel vm)
            return;
        vm.Host.CaptureKeyboard = true;
        if (!stealFocus)
            return;

        // Input: tras el click/dialogo; Background: segunda pasada por si Avalonia
        // restaura el foco al TextBox del catálogo al cerrar ConfirmWindow.
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not MainViewModel v) return;
            ScreenFrame.Focus();
            v.Host.CaptureKeyboard = true;
        }, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not MainViewModel v) return;
            if (FocusManager?.GetFocusedElement() is TextBox)
            {
                ScreenFrame.Focus();
                v.Host.CaptureKeyboard = true;
            }
        }, DispatcherPriority.Background);
    }

    private void DisableSpectrumKeyboard()
    {
        if (DataContext is not MainViewModel vm)
            return;
        vm.Host.CaptureKeyboard = false;
        vm.Host.ReleaseAllKeys();
    }

    private bool IsTypingInUi(object? source) =>
        source is TextBox || (source is Control c && c.FindAncestorOfType<TextBox>() != null);

    private void OnAnyGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not MainViewModel)
            return;
        if (IsTypingInUi(e.Source))
            DisableSpectrumKeyboard();
        else
            // Lista/botones del panel: capturar sin robar el foco (GetAsyncKeyState).
            Vm.Host.CaptureKeyboard = true;
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        // Atajos de la app; el resto lo lee WindowsKeyboardPoller.
        if (IsTypingInUi(e.Source))
        {
            DisableSpectrumKeyboard();
            return;
        }

        if (e.Key is Key.F5)
        {
            Vm.PauseCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.F6)
        {
            Vm.TapePlayCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.F7)
        {
            Vm.TapeFastForwardCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.F8)
        {
            _ = Vm.SaveStateCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.F9)
        {
            Vm.TapeRewindCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Asegurar captura al escribir hacia el Spectrum.
        if (!Vm.Host.CaptureKeyboard)
            EnableSpectrumKeyboard(stealFocus: false);
        Vm.Host.CaptureKeyboard = true;

        // Evitar que letras activen mnemónicos del menú (_Archivo, etc.).
        if (e.Key is >= Key.A and <= Key.Z
            or >= Key.D0 and <= Key.D9
            or Key.Space or Key.Enter or Key.Return
            or Key.Back or Key.Delete
            or Key.Left or Key.Right or Key.Up or Key.Down
            or Key.LeftShift or Key.RightShift)
            e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e) { }
    private void OnKeyUp(object? sender, KeyEventArgs e) { }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        DisableSpectrumKeyboard();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir medio Spectrum",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Spectrum / comprimidos")
                {
                    Patterns =
                    [
                        "*.tap", "*.tzx", "*.trd", "*.z80", "*.sna", "*.rom", "*.pok",
                        "*.zip", "*.rar", "*.7z", "*.gz", "*.tar", "*.tgz"
                    ]
                },
                new FilePickerFileType("Todos") { Patterns = ["*.*"] }
            ]
        });
        if (files.Count == 0)
        {
            EnableSpectrumKeyboard(stealFocus: true);
            return;
        }
        string? path = files[0].TryGetLocalPath();
        if (path == null)
        {
            EnableSpectrumKeyboard(stealFocus: true);
            return;
        }
        if (path.EndsWith(".pok", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var trainers = PokeService.ParsePok(await File.ReadAllTextAsync(path));
            if (trainers.Count > 0)
                Vm.Host.Machine.ApplyPokes(trainers[0].Entries);
            EnableSpectrumKeyboard(stealFocus: true);
            return;
        }
        try
        {
            Vm.Host.OpenFile(path);
        }
        catch (Exception ex)
        {
            Vm.Status = "Error: " + ex.Message;
        }
        EnableSpectrumKeyboard(stealFocus: true);
    }

    private async void OnLoadState(object? sender, RoutedEventArgs e)
    {
        DisableSpectrumKeyboard();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Cargar estado",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Estado") { Patterns = ["*.json", "*.state.json"] }
            ]
        });
        if (files.Count == 0)
        {
            EnableSpectrumKeyboard(stealFocus: true);
            return;
        }
        string? path = files[0].TryGetLocalPath();
        if (path != null)
            await Vm.Host.LoadStateAsync(path);
        EnableSpectrumKeyboard(stealFocus: true);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnRecientesOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu)
            return;

        menu.Items.Clear();
        var items = Vm.Host.Recents.Snapshot();
        if (items.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "(Vacío)",
                IsEnabled = false,
                Foreground = Brushes.White
            });
            return;
        }

        foreach (var item in items)
        {
            var recent = item;
            var mi = new MenuItem
            {
                Header = recent.DisplayTitle,
                Foreground = Brushes.White,
                Tag = recent
            };
            mi.Click += OnRecentItemClick;
            menu.Items.Add(mi);
        }

        menu.Items.Add(new Separator());
        var clear = new MenuItem
        {
            Header = "Limpiar recientes",
            Foreground = Brushes.White
        };
        clear.Click += OnClearRecentsClick;
        menu.Items.Add(clear);
    }

    private async void OnRecentItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RecentItem item })
            return;
        DisableSpectrumKeyboard();
        try
        {
            await Vm.OpenRecentAsync(item);
        }
        finally
        {
            EnableSpectrumKeyboard(stealFocus: true);
        }
    }

    private void OnClearRecentsClick(object? sender, RoutedEventArgs e) =>
        Vm.ClearRecents();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles()?.ToList();
        if (files is not { Count: > 0 }) return;
        string? path = files[0].TryGetLocalPath();
        if (path != null)
        {
            Vm.Host.OpenFile(path);
            EnableSpectrumKeyboard(stealFocus: true);
        }
    }
}
