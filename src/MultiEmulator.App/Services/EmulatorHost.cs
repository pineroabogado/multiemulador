using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MultiEmulator.App.Input;
using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Input;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;
using MultiEmulator.Spectrum.State;
using MultiEmulator.Spectrum.Tape;
using MultiEmulator.Spectrum.Ula;
using MultiEmulator.Spectrum.Video;
using NAudio.Wave;

namespace MultiEmulator.App.Services;

public sealed class EmulatorHost : IDisposable
{
    private readonly SpectrumMachine _machine = new();
    private readonly RomBootstrap _roms = new();
    private readonly CatalogService _catalog = new();
    private readonly RecentItemsStore _recents = new();
    private readonly XboxInputService _xbox = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _audio;
    private WriteableBitmap? _bitmap;
    private bool _running;
    private int _presentPending;
    private uint[] _frameCopy = new uint[SpectrumUla.ScreenWidth * SpectrumUla.ScreenHeight];
    private readonly byte[] _audioScratch = new byte[882 * 2]; // PCM16 mono

    public SpectrumMachine Machine => _machine;
    public CatalogService Catalog => _catalog;
    public RecentItemsStore Recents => _recents;
    public string RomNotice => _roms.NoticeText;

    public EmulatorHost()
    {
        try { _recents.Load(); }
        catch { /* recientes corruptos: lista vacía */ }
    }
    public WriteableBitmap? Bitmap => _bitmap;
    public event Action? FrameRendered;
    /// <summary>Notifica cambios de barra de progreso (Descargar y ejecutar / catálogo).</summary>
    public event Action? ProgressChanged;
    public string Status { get; private set; } = "Iniciando…";
    /// <summary>0–100 cuando la descarga tiene Content-Length; irrelevante si <see cref="IsProgressIndeterminate"/>.</summary>
    public double ProgressValue { get; private set; }
    public string ProgressText { get; private set; } = "";
    public bool IsProgressVisible { get; private set; }
    public bool IsProgressIndeterminate { get; private set; }
    /// <summary>Último medio abierto por el usuario (cinta/snapshot), o null si aún no hay juego.</summary>
    public string? CurrentMediaName { get; private set; }
    /// <summary>Última estrategia de carga que pasó el health-check (si aplica).</summary>
    public string? LastSuccessfulLoadStrategy { get; private set; }
    /// <summary>True si ya se cargó una cinta o snapshot en esta sesión.</summary>
    public bool HasLoadedMedia => !string.IsNullOrEmpty(CurrentMediaName)
                                  || _machine.Tape.Tape != null;
    // Leído en el hilo del loop; escrito desde UI (foco / catálogo).
    private volatile bool _captureKeyboard = true;

    /// <summary>Si true, cada frame se sincroniza la matriz con GetAsyncKeyState.</summary>
    public bool CaptureKeyboard
    {
        get => _captureKeyboard;
        set => _captureKeyboard = value;
    }
    /// <summary>Si true, el mando Xbox también inyecta teclas Spectrum según el mapa (en caliente).</summary>
    public bool MapPadToKeyboard { get; set; } = true;
    public bool Mute { get; set; }
    public XboxPadMapping PadMapping => _xbox.Mapping;

    public async Task InitializeAsync()
    {
        try
        {
            Status = "Cargando Spectrum 48K…";

            // Bitmap: si ya estamos en UI, crear en sitio (evita deadlock
            // await InvokeAsync desde el propio hilo UI). Si no, postear.
            await EnsureBitmapAsync().ConfigureAwait(false);

            // ROM + machine fuera del hilo UI (I/O sync + migraciones de AppPaths).
            byte[] rom48 = await Task.Run(async () =>
            {
                AppPaths.EnsureCreated();
                try { LoadPadMappingSettings(); }
                catch { /* ajustes de mando corruptos: defaults */ }
                return await _roms.EnsureRom48Async().ConfigureAwait(false);
            }).ConfigureAwait(false);

            lock (_sync)
            {
                _machine.LoadSystemRoms(rom48, null);
                _machine.SetMachine(MachineType.Spectrum48);
            }

            // Bucle primero: la UI deja de estar en «Cargando…» aunque el audio falle/cuelgue.
            Status = "Spectrum 48K · 50 Hz (sin warp)";
            StartLoop();
            _ = Task.Run(StartAudioSafe);
        }
        catch (Exception ex)
        {
            Status = "Error al iniciar emulador: " + ex.Message;
            throw;
        }

        // 128K + catálogo en segundo plano (no bloquean el boot).
        _ = Task.Run(async () =>
        {
            try
            {
                var rom128 = await _roms.EnsureRom128Async().ConfigureAwait(false);
                var trDos = await _roms.EnsureTrDosRomAsync().ConfigureAwait(false);
                lock (_sync)
                {
                    _machine.LoadSystemRoms(null, rom128);
                    if (trDos != null)
                        _machine.LoadTrDosRom(trDos);
                }

                string localCatalog = ResolveBundledCatalogPath(EmulatorPlatform.ZxSpectrum);
                await _catalog.LoadAsync(null, localCatalog).ConfigureAwait(false);
            }
            catch
            {
                // Boot 48K ya está en marcha; fallos de 128/catálogo no lo detienen.
            }
        });
    }

    /// <param name="recordRecent">
    /// Si true, añade la ruta a Archivo → Recientes (cargas locales / drag-drop).
    /// Las descargas de catálogo pasan <c>false</c> y registran la ficha aparte.
    /// </param>
    public void OpenFile(
        string path,
        bool resetMachine = false,
        bool recordRecent = true,
        RomMediaFormat format = RomMediaFormat.Auto,
        IEnumerable<string>? extraCandidates = null,
        CatalogEntry? catalogEntry = null,
        LoadSolutionHint? solutionHint = null)
    {
        // Si el play de catálogo ya muestra barra, no la ocultamos al terminar Abrir.
        bool ownedProgress = !IsProgressVisible;
        if (ownedProgress)
            ReportPlayProgress(null, "Abriendo…");

        string openedPath = path;
        LoadAttemptResult? lastResult = null;
        try
        {
            lock (_sync)
            {
                if (resetMachine)
                    _machine.Reset();

                solutionHint ??= LoadSolutionsStore.Resolve(catalogEntry, path);
                format = LoadSolutionsStore.ApplyPreferredFormat(format, solutionHint);

                ReportPlayProgress(null, "Comprobando carga…");
                // Registro → preflight + health-check + reintento de estrategias/hermanos.
                var result = LoadOrchestrator.OpenWithRetry(
                    _machine,
                    path,
                    format,
                    extraCandidates,
                    status: msg =>
                    {
                        Status = msg;
                        if (msg.StartsWith("Reintentando", StringComparison.OrdinalIgnoreCase)
                            || msg.StartsWith("Carga fallida", StringComparison.OrdinalIgnoreCase))
                            ReportPlayProgress(null, "Reintentando carga…");
                        else if (msg.StartsWith("Carga OK", StringComparison.OrdinalIgnoreCase)
                                 || msg.StartsWith("Solución conocida", StringComparison.OrdinalIgnoreCase))
                            ReportPlayProgress(100, msg.StartsWith("Carga OK", StringComparison.OrdinalIgnoreCase)
                                ? "Carga OK"
                                : "Solución conocida");
                        else
                            ReportPlayProgress(null, "Comprobando carga…");
                    },
                    resetBetweenAttempts: true,
                    solutionHint: solutionHint);

                if (!result.Success)
                    throw new InvalidOperationException(result.StatusMessage);

                lastResult = result;
                openedPath = result.Path;
                LastSuccessfulLoadStrategy = LoadPolicies.StrategyDisplayName(result.Strategy);
                CurrentMediaName = Path.GetFileName(openedPath);
                Status = string.IsNullOrWhiteSpace(result.StatusMessage)
                    ? $"Cargado: {CurrentMediaName}"
                    : result.StatusMessage;
            }

            if (recordRecent)
            {
                try { _recents.AddLocal(openedPath); }
                catch { /* recientes: no tumbar la carga */ }
            }

            if (lastResult is { Success: true })
            {
                try
                {
                    LoadSolutionsStore.RecordSuccessfulPlay(
                        catalogEntry,
                        openedPath,
                        lastResult.Strategy,
                        lastResult.AppliedHint);
                }
                catch { /* registro: no tumbar la partida */ }
            }

            // Tras Load exitoso: vaciar caché local (el medio ya está en RAM).
            ClearLocalCacheAfterSuccessfulLoad();
        }
        finally
        {
            if (ownedProgress)
                HidePlayProgress();
        }
    }

    public async Task OpenCatalogEntryAsync(
        CatalogEntry entry,
        bool resetMachine = true,
        CatalogGameMediaUrls? knownMedia = null,
        RomDownloadSource source = RomDownloadSource.SpectrumComputing,
        RomMediaFormat format = RomMediaFormat.Auto)
    {
        // Capturar SyncContext (UI) antes de awaits para que Progress marshalee bien.
        var playProgress = new Progress<(double? percent, string phase)>(p =>
            ReportPlayProgress(p.percent, p.phase));

        try
        {
            // 0) Registro de soluciones técnicas (antes de Auto / descarga).
            var solutionHint = LoadSolutionsStore.Resolve(entry);
            format = LoadSolutionsStore.ApplyPreferredFormat(format, solutionHint);
            if (solutionHint.Solution != null
                && !string.IsNullOrWhiteSpace(solutionHint.Solution.PreferredSource)
                && source == RomDownloadSource.SpectrumComputing
                && string.Equals(solutionHint.Solution.PreferredSource, "tosec", StringComparison.OrdinalIgnoreCase))
            {
                source = RomDownloadSource.ArchiveOrgTosec;
            }

            // 1) Preferir copia(s) local(es) en JUEGOS (sin red) con health + retry.
            ReportPlayProgress(null, "Buscando en JUEGOS…");
            IReadOnlyList<string> localCandidates = Array.Empty<string>();
            try
            {
                localCandidates = LocalJuegosMatcher.FindLocalCandidates(
                    entry, format: format, localFileHint: solutionHint.Solution?.LocalFileHint);
            }
            catch
            {
                localCandidates = Array.Empty<string>();
            }

            if (localCandidates.Count > 0)
            {
                string primary = localCandidates[0];
                var extras = localCandidates.Skip(1).ToList();
                try
                {
                    ReportPlayProgress(100, "JUEGOS listo");
                    Status = $"Abriendo desde JUEGOS: {Path.GetFileName(primary)}…";
                    ReportPlayProgress(null, "Abriendo…");
                    OpenFile(primary, resetMachine, recordRecent: true, format, extras, entry, solutionHint);
                    CurrentMediaName = entry.Title;
                    PersistSuccessfulStrategy(entry, knownMedia);
                    Status = $"Abierto desde JUEGOS: {Path.GetFileName(primary)}"
                             + (LastSuccessfulLoadStrategy is { } s ? $" · {s}" : "");
                    return;
                }
                catch (Exception ex)
                {
                    Status = $"JUEGOS: carga no saludable ({ex.Message}). Descargando…";
                }
            }

            string sourceName = RomDownloadSourceInfo.DisplayName(source);
            string formatName = RomMediaFormatInfo.DisplayName(format);
            Status = $"Descargando {entry.Title} (preferida: {sourceName} · {formatName}, cascada)…";
            try
            {
                string path = await _catalog.DownloadAsync(entry, source, format, playProgress)
                    .ConfigureAwait(false);
                var extras = localCandidates
                    .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                ReportPlayProgress(null, "Abriendo…");
                OpenFile(path, resetMachine, recordRecent: false, format, extras, entry, solutionHint);
                CurrentMediaName = entry.Title;

                PersistSuccessfulStrategy(entry, knownMedia);

                try { _recents.AddCatalog(entry, source); }
                catch { /* recientes: no tumbar la carga */ }

                string from = ResolveDownloadedFromLabel(entry, source);
                Status = $"Descargado desde {from}: {entry.Title}"
                         + (LastSuccessfulLoadStrategy is { } s ? $" · {s}" : "");
            }
            catch (Exception ex)
            {
                Status = $"Catálogo: error — {ex.Message}";
                throw;
            }
        }
        finally
        {
            HidePlayProgress();
        }
    }

    private void PersistSuccessfulStrategy(CatalogEntry entry, CatalogGameMediaUrls? knownMedia)
    {
        try
        {
            CatalogGameMetadataStore.SaveFromSuccessfulPlay(
                entry,
                knownMedia,
                successfulLoadStrategy: LastSuccessfulLoadStrategy);
        }
        catch
        {
            // La partida ya corre; no fallar por un fallo de escritura de metadatos.
        }
    }

    /// <summary>
    /// Actualiza la barra de progreso del play/catálogo.
    /// <paramref name="percent"/> null = modo indeterminado (p.ej. índice Archive.org).
    /// Seguro tras ConfigureAwait(false): posta al hilo UI si hace falta.
    /// </summary>
    public void ReportPlayProgress(double? percent, string phase)
    {
        void Apply()
        {
            IsProgressVisible = true;
            ProgressText = phase ?? "";
            if (percent is double p)
            {
                IsProgressIndeterminate = false;
                ProgressValue = Math.Clamp(p, 0, 100);
            }
            else
            {
                IsProgressIndeterminate = true;
            }

            ProgressChanged?.Invoke();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    public void HidePlayProgress()
    {
        void Apply()
        {
            IsProgressVisible = false;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            ProgressText = "";
            ProgressChanged?.Invoke();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private static string ResolveDownloadedFromLabel(CatalogEntry entry, RomDownloadSource preferred)
    {
        var detected = RomDownloadSourceInfo.TryDetectFromUrl(entry.Url);
        if (detected is RomDownloadSource known)
            return RomDownloadSourceInfo.DisplayName(known);
        return RomDownloadSourceInfo.DisplayName(preferred);
    }

    private void ClearLocalCacheAfterSuccessfulLoad()
    {
        try
        {
            AppPaths.ClearLocalCache();
            if (!Status.Contains("Caché local limpiada", StringComparison.Ordinal))
                Status = $"{Status} · Caché local limpiada";
        }
        catch
        {
            // La carga ya tuvo éxito; no fallar el play por un borrado parcial.
        }
    }

    public async Task<int> RebuildCatalogAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var linked = new Progress<string>(msg =>
        {
            Status = msg;
            progress?.Report(msg);
        });
        return await _catalog.RebuildFromSpectrumComputingAsync(linked, ct);
    }

    /// <summary>
    /// Cambia la plataforma del catálogo en memoria. No bloquea el arranque;
    /// plataformas inactivas quedan vacías («Próximamente»).
    /// </summary>
    public async Task SetCatalogPlatformAsync(EmulatorPlatform platform, CancellationToken ct = default)
    {
        AppPaths.Platform = platform;
        ProjectCatalogPaths.DefaultPlatform = platform;
        _catalog.Platform = platform;
        try { _recents.Load(); }
        catch { /* recientes de otra plataforma / corruptos */ }

        if (!EmulatorPlatformInfo.IsCatalogActive(platform))
        {
            _catalog.ClearEntries();
            return;
        }

        string localCatalog = ResolveBundledCatalogPath(platform);
        await _catalog.LoadAsync(null, localCatalog, ct);
    }

    public static string ResolveBundledCatalogPath(EmulatorPlatform platform)
    {
        string id = EmulatorPlatformInfo.Id(platform);
        string platformPath = Path.Combine(AppContext.BaseDirectory, "catalog", id, "index.json");
        if (File.Exists(platformPath))
            return platformPath;

        // Compat: publicadores antiguos solo tenían catalog/index.json (ZX).
        string legacy = Path.Combine(AppContext.BaseDirectory, "catalog", "index.json");
        return legacy;
    }

    public async Task SaveStateAsync(string? name = null)
    {
        AppPaths.EnsureCreated();
        name ??= DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".state.json";
        string path = Path.Combine(AppPaths.States, name);
        SaveStateData data;
        lock (_sync) data = _machine.CaptureState();
        await SaveStateService.SaveToFileAsync(path, data);
        Status = $"Estado guardado: {name}";
    }

    public async Task LoadStateAsync(string path)
    {
        var data = await SaveStateService.LoadFromFileAsync(path);
        lock (_sync) _machine.RestoreState(data);
        Status = $"Estado cargado: {Path.GetFileName(path)}";
    }

    public void ReleaseAllKeys()
    {
        lock (_sync) _machine.Keyboard.ReleaseAll();
    }

    public void LoadPadMappingSettings()
    {
        var s = XboxPadMappingStore.Load();
        MapPadToKeyboard = s.MapPadToKeyboard;
        PadMapping.LoadFrom(s.Bindings);
    }

    public void SavePadMappingSettings() =>
        XboxPadMappingStore.Save(PadMapping, MapPadToKeyboard);

    public void ResetPadMappingToDefaults()
    {
        PadMapping.ResetToDefaults();
        SavePadMappingSettings();
    }

    public void SetMachine(MachineType type)
    {
        lock (_sync) _machine.SetMachine(type);
        Status = type == MachineType.Spectrum128 ? "Spectrum 128K en ejecución" : "Spectrum 48K en ejecución";
    }

    public void Reset()
    {
        lock (_sync) _machine.Reset();
        Status = "Reset";
    }

    public void TogglePause()
    {
        lock (_sync) _machine.Paused = !_machine.Paused;
        Status = _machine.Paused ? "Pausa" : "Ejecutando";
    }

    public string TapeTransportLabel
    {
        get
        {
            lock (_sync)
            {
                var tr = _machine.Tape.Transport;
                var tape = _machine.Tape.Tape;
                if (tape == null)
                    return tr.IsShuttle ? $"{tr.StatusLabel} (sin cinta)" : "Sin cinta";
                int pos = tape.Position;
                int n = tape.Blocks.Count;
                int shown = n == 0 ? 0 : Math.Min(pos + 1, n);
                return $"{tr.StatusLabel}  ·  bloque {shown}/{n}";
            }
        }
    }

    public void TapePlay()
    {
        lock (_sync)
        {
            _machine.Tape.PlayTape();
            Status = TapeTransportLabel;
        }
    }

    public void TapeFastForward()
    {
        lock (_sync)
        {
            // En caliente aunque no haya cinta: modo deck + efecto VHS.
            _machine.Tape.CycleFastForward();
            Status = TapeTransportLabel;
        }
    }

    public void TapeRewind()
    {
        lock (_sync)
        {
            _machine.Tape.CycleRewind();
            Status = TapeTransportLabel;
        }
    }

    public void TapeRewindOneBlock()
    {
        lock (_sync)
        {
            if (_machine.Tape.Tape == null) { Status = "No hay cinta insertada"; return; }
            _machine.Tape.RewindOneBlock();
            Status = $"Cinta: bloque anterior · {TapeTransportLabel}";
        }
    }

    public void TapeRewindToStart()
    {
        lock (_sync)
        {
            if (_machine.Tape.Tape == null) { Status = "No hay cinta insertada"; return; }
            _machine.Tape.RewindToStart();
            Status = "Cinta rebobinada al inicio";
        }
    }

    private void StartLoop()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    private void Loop(CancellationToken ct)
    {
        // Timer ~1 ms: Sleep(20) si no es demasiado grueso en Windows (15.6 ms).
        try { _ = NativeTimeBeginPeriod(1); } catch { /* no crítico */ }

        var sw = Stopwatch.StartNew();
        double next = 0;
        // 69888 T @ 3.5 MHz ≈ 19.968 ms (50.08 Hz). Usamos eso, no 20.0 redondeado.
        const double frameMs = SpectrumUla.FrameTStates48 * 1000.0 / 3_500_000.0;
        double fpsWindowStart = 0;
        long fpsWindowFrames = 0;
        while (!ct.IsCancellationRequested)
        {
            _xbox.Poll(_machine.Joystick);
            lock (_sync)
            {
                // ReleaseAll + teclas PC, luego mando encima (nivel, sin edges).
                if (CaptureKeyboard)
                    WindowsKeyboardPoller.Apply(_machine.Keyboard);
                else
                    _machine.Keyboard.ReleaseAll();
                if (MapPadToKeyboard)
                    _xbox.ApplyKeyboard(_machine.Keyboard);

                // Siempre exactamente 1 frame por tick. Nunca warp ×N (aceleraba la partida).
                _machine.RunFrame();
            }
            PresentFrame();
            PumpAudio();
            fpsWindowFrames++;
            double now = sw.Elapsed.TotalMilliseconds;
            if (now - fpsWindowStart >= 1000.0)
            {
                double hz = fpsWindowFrames * 1000.0 / Math.Max(1.0, now - fpsWindowStart);
                // No pisar mensajes de carga/error: solo el letrero de ritmo.
                if (Status.Contains("Hz", StringComparison.Ordinal)
                    || Status.StartsWith("Spectrum", StringComparison.Ordinal))
                    Status = $"Spectrum 48K · {hz:F0} Hz (obj. 50)";
                fpsWindowStart = now;
                fpsWindowFrames = 0;
            }
            next += frameMs;
            for (;;)
            {
                double delay = next - sw.Elapsed.TotalMilliseconds;
                if (delay <= 0)
                    break;
                if (delay > 2)
                    Thread.Sleep((int)(delay - 1));
                else
                    Thread.SpinWait(50);
            }
            if (next < sw.Elapsed.TotalMilliseconds - 50)
                next = sw.Elapsed.TotalMilliseconds;
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint NativeTimeBeginPeriod(uint period);

    private async Task EnsureBitmapAsync()
    {
        // Si ya estamos en UI: crear en sitio (evita deadlock await InvokeAsync→UI).
        if (Dispatcher.UIThread.CheckAccess())
        {
            EnsureBitmap();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(EnsureBitmap);
    }

    private void EnsureBitmap()
    {
        Dispatcher.UIThread.VerifyAccess();
        _bitmap ??= new WriteableBitmap(
            new Avalonia.PixelSize(SpectrumUla.ScreenWidth, SpectrumUla.ScreenHeight),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
    }

    private void PresentFrame()
    {
        if (_bitmap == null) return;
        // Drop frames if UI is busy — avoids Lock backlog and keeps 50 Hz feel.
        if (Interlocked.CompareExchange(ref _presentPending, 1, 0) != 0)
            return;

        lock (_sync)
        {
            _machine.Ula.Framebuffer.CopyTo(_frameCopy);
            // Efecto VHS solo en FF/REW (PresentFrame, en caliente).
            VhsTransportEffect.Apply(_frameCopy.AsSpan(), _machine.Tape.Transport);
        }

        var bmp = _bitmap;
        var src = _frameCopy;
        // Render > Input: Background was starved by KeyDown while typing, so the
        // framebuffer froze until the UI went idle and then jumped ("burst" text).
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                using var fb = bmp.Lock();
                unsafe
                {
                    int w = SpectrumUla.ScreenWidth;
                    int h = SpectrumUla.ScreenHeight;
                    int srcStride = w * 4;
                    fixed (uint* pSrc = src)
                    {
                        byte* pDst = (byte*)fb.Address;
                        if (fb.RowBytes == srcStride)
                        {
                            Buffer.MemoryCopy(pSrc, pDst, fb.RowBytes * h, srcStride * h);
                        }
                        else
                        {
                            for (int y = 0; y < h; y++)
                                Buffer.MemoryCopy(pSrc + y * w, pDst + y * fb.RowBytes, srcStride, srcStride);
                        }
                    }
                }
                FrameRendered?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref _presentPending, 0);
            }
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// NAudio WaveOut puede bloquear indefinidamente con drivers/Bluetooth defectuosos.
    /// Se ejecuta en Task.Run desde InitializeAsync; fallo → emulador sin sonido.
    /// </summary>
    private void StartAudioSafe()
    {
        try
        {
            // PCM 16-bit: más fiable con WaveOut que IEEE float (a veces sale en silencio).
            // Sin DiscardOnBufferOverflow: el catch-up no debe tirar ráfagas FX (Arkanoid).
            var provider = new BufferedWaveProvider(new WaveFormat(44100, 16, 1))
            {
                BufferLength = 44100 * 2, // 1 s
                DiscardOnBufferOverflow = false
            };
            var waveOut = new WaveOutEvent { DesiredLatency = 80 };
            waveOut.Init(provider);
            waveOut.Play();
            _audio = provider;
            _waveOut = waveOut;
        }
        catch (Exception ex)
        {
            try
            {
                _waveOut?.Dispose();
            }
            catch { /* ignore */ }
            _waveOut = null;
            _audio = null;
            if (!Status.Contains("Audio", StringComparison.OrdinalIgnoreCase))
                Status = $"{Status} · Audio no disponible ({ex.GetType().Name})";
        }
    }

    private void PumpAudio()
    {
        if (_audio == null) return;
        const int samples = 882;
        // Margen holgado sobre DesiredLatency (80 ms). Un tope de 120 ms dejaba
        // 1 frame de holgura y en la práctica descartaba clics de Arkanoid.
        const int maxBufferedBytes = 44100 * 2 * 400 / 1000; // 400 ms
        Span<float> pcm = stackalloc float[samples];
        lock (_sync)
            _machine.ReadAudioSamples(pcm);
        if (_audio.BufferedBytes >= maxBufferedBytes)
            return;
        if (Mute)
            pcm.Clear();
        var buf = _audioScratch;
        for (int i = 0; i < samples; i++)
        {
            float s = Math.Clamp(pcm[i], -1f, 1f);
            short v = (short)(s * 32000f);
            buf[i * 2] = (byte)v;
            buf[i * 2 + 1] = (byte)(v >> 8);
        }
        try
        {
            _audio.AddSamples(buf, 0, samples * 2);
        }
        catch (InvalidOperationException)
        {
            // Buffer lleno: saltar chunk (no DiscardOnBufferOverflow).
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { /* ignore */ }
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _cts?.Dispose();
    }
}
