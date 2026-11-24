using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace MultiEmulator.App.Views;

public partial class MediaLightboxWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8.0;
    private const double ZoomStep = 1.25;

    private Bitmap? _bitmap;
    private double _zoom = 1.0;
    private bool _fitMode = true;
    private bool _panning;
    private bool _applying;
    private Size _lastViewport;
    private Point _panStart;
    private Vector _scrollStart;

    public MediaLightboxWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Scroller.SizeChanged += OnScrollerSizeChanged;
    }

    public MediaLightboxWindow(string title, Bitmap image) : this()
    {
        TitleText.Text = title;
        Title = title;
        _bitmap = image;
        PreviewImage.Source = image;
        _fitMode = true;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Tras el primer layout el Viewport ya tiene tamaño real.
        Dispatcher.UIThread.Post(() => ApplyZoom(), DispatcherPriority.Loaded);
    }

    private void OnScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_fitMode || _bitmap == null || _applying) return;
        var vp = GetViewportSize();
        if (vp.Width < 32 || vp.Height < 32) return;
        if (Math.Abs(vp.Width - _lastViewport.Width) < 0.5 &&
            Math.Abs(vp.Height - _lastViewport.Height) < 0.5)
            return;
        ApplyZoom();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnZoomIn(object? sender, RoutedEventArgs e) => ZoomBy(ZoomStep);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => ZoomBy(1.0 / ZoomStep);

    private void OnZoomFit(object? sender, RoutedEventArgs e)
    {
        _fitMode = true;
        ApplyZoom();
    }

    private void OnZoom100(object? sender, RoutedEventArgs e)
    {
        _fitMode = false;
        SetZoom(1.0, center: true);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.OemPlus:
            case Key.Add:
                ZoomBy(ZoomStep);
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                ZoomBy(1.0 / ZoomStep);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                _fitMode = true;
                ApplyZoom();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                _fitMode = false;
                SetZoom(1.0, center: true);
                e.Handled = true;
                break;
        }
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_bitmap == null) return;

        double factor = e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep;
        ZoomBy(factor, e.GetPosition(Scroller));
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ImageHost).Properties.IsLeftButtonPressed)
            return;

        _panning = true;
        _panStart = e.GetPosition(Scroller);
        _scrollStart = new Vector(Scroller.Offset.X, Scroller.Offset.Y);
        e.Pointer.Capture(ImageHost);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning) return;

        var pos = e.GetPosition(Scroller);
        double dx = _panStart.X - pos.X;
        double dy = _panStart.Y - pos.Y;
        Scroller.Offset = new Vector(_scrollStart.X + dx, _scrollStart.Y + dy);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _panning = false;
        Cursor = Cursor.Default;
    }

    private void ZoomBy(double factor, Point? anchorInScroller = null)
    {
        _fitMode = false;
        SetZoom(_zoom * factor, center: false, anchorInScroller);
    }

    private void SetZoom(double zoom, bool center, Point? anchorInScroller = null)
    {
        double oldZoom = _zoom;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        ApplyZoom(oldZoom, anchorInScroller, center);
    }

    private Size GetViewportSize()
    {
        double w = Scroller.Viewport.Width;
        double h = Scroller.Viewport.Height;
        if (w < 32 || h < 32)
        {
            // Antes del primer arrange Viewport puede ser 0; Bounds del área cliente ayuda.
            w = Math.Max(w, Scroller.Bounds.Width);
            h = Math.Max(h, Scroller.Bounds.Height);
        }

        return new Size(Math.Max(1, w), Math.Max(1, h));
    }

    private void ApplyZoom(double? previousZoom = null, Point? anchorInScroller = null, bool center = false)
    {
        if (_bitmap == null || _applying) return;

        double bw = _bitmap.PixelSize.Width;
        double bh = _bitmap.PixelSize.Height;
        if (bw < 1 || bh < 1) return;

        var vp = GetViewportSize();
        if (vp.Width < 32 || vp.Height < 32)
            return;

        _applying = true;
        try
        {
            _lastViewport = vp;
            double viewportW = vp.Width;
            double viewportH = vp.Height;

            if (_fitMode)
            {
                double fit = Math.Min(viewportW / bw, viewportH / bh);
                // Escala entera cuando sigue llenando ~90% del viewport (pixel art SCR).
                double floor = Math.Floor(fit);
                if (floor >= 1 && floor >= fit * 0.9)
                    _zoom = floor;
                else
                    _zoom = fit * 0.98;

                _zoom = Math.Clamp(_zoom, MinZoom, MaxZoom);
            }

            double w = bw * _zoom;
            double h = bh * _zoom;

            PreviewImage.Width = w;
            PreviewImage.Height = h;

            // Host = imagen; si cabe en el viewport, el host ocupa el viewport para centrar
            // sin crear un área scrollable absurda.
            double hostW = Math.Max(w, viewportW);
            double hostH = Math.Max(h, viewportH);
            ImageHost.Width = hostW;
            ImageHost.Height = hostH;

            double marginX = Math.Max(0, (hostW - w) / 2);
            double marginY = Math.Max(0, (hostH - h) / 2);
            PreviewImage.Margin = new Thickness(marginX, marginY, 0, 0);

            if (_fitMode || center || (w <= viewportW && h <= viewportH))
            {
                Scroller.Offset = new Vector(0, 0);
            }
            else if (anchorInScroller is { } anchor && previousZoom is { } oldZ && oldZ > 0.0001)
            {
                double scale = _zoom / oldZ;
                double contentX = Scroller.Offset.X + anchor.X;
                double contentY = Scroller.Offset.Y + anchor.Y;
                Scroller.Offset = new Vector(
                    ClampOffset(contentX * scale - anchor.X, hostW, viewportW),
                    ClampOffset(contentY * scale - anchor.Y, hostH, viewportH));
            }
            else if (previousZoom is { } ozPrev && (hostW > viewportW || hostH > viewportH))
            {
                // Zoom desde botones: mantener el centro del viewport.
                double cx = Scroller.Offset.X + viewportW / 2;
                double cy = Scroller.Offset.Y + viewportH / 2;
                double scale = ozPrev > 0.0001 ? _zoom / ozPrev : 1.0;
                Scroller.Offset = new Vector(
                    ClampOffset(cx * scale - viewportW / 2, hostW, viewportW),
                    ClampOffset(cy * scale - viewportH / 2, hostH, viewportH));
            }

            ZoomLabel.Text = _fitMode
                ? $"Ajuste {_zoom * 100:0}%"
                : $"{_zoom * 100:0}%";
        }
        finally
        {
            _applying = false;
        }
    }

    private static double ClampOffset(double value, double host, double viewport) =>
        Math.Clamp(value, 0, Math.Max(0, host - viewport));
}
