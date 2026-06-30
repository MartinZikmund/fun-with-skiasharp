using System;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace SkiaGallery.Core;

// Hosts an IDemoScene on Uno's hardware-accelerated Skia surface.
// Uses CompositionTarget.Rendering (not a 60fps DispatcherTimer) so it can run
// above 60 FPS on high-refresh displays; it attaches on Loaded and DETACHES on
// Unloaded (and Stop()) to avoid leaks / background work after navigating away.
public partial class GalleryCanvas : SKCanvasElement
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IDemoScene? _scene;
    private double _lastSeconds;
    private bool _running;

    public GalleryCanvas()
    {
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    public void SetScene(IDemoScene scene)
    {
        _scene = scene;
        Invalidate();
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }
        _running = true;
        _lastSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(0.05, now - _lastSeconds); // clamp big gaps (tab switch, breakpoint)
        _lastSeconds = now;
        if (dt <= 0f)
        {
            return;
        }
        _scene?.Update(dt);
        Invalidate();
    }

    // Input forwarded from the hosting page.
    public void PointerDown(double x, double y) => _scene?.PointerDown((float)x, (float)y);
    public void PointerMove(double x, double y) => _scene?.PointerMove((float)x, (float)y);
    public void PointerUp(double x, double y) => _scene?.PointerUp((float)x, (float)y);
    public void Wheel(int delta) => _scene?.Wheel(delta);
    public void ForwardKeyDown(string key) => _scene?.KeyDown(key);
    public void ForwardKeyUp(string key) => _scene?.KeyUp(key);

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_scene is null)
        {
            canvas.Clear(new SKColor(0x0A, 0x0C, 0x14));
            return;
        }
        _scene.Draw(canvas, (float)area.Width, (float)area.Height);
    }
}
