using System;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace SkiaBreakout;

// Bridges the pure-Skia GameScene to Uno's hardware-accelerated Skia surface,
// and forwards pointer + keyboard input from MainPage.
public partial class DemoCanvas : SKCanvasElement
{
    private readonly GameScene _scene = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public DemoCanvas()
    {
        _timer.Tick += (_, _) => { _scene.Update(1f / 60f); Invalidate(); };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public void PointerDown(double x, double y) => _scene.PointerDown((float)x, (float)y);
    public void PointerMove(double x, double y) => _scene.PointerMove((float)x, (float)y);
    public void PointerUp(double x, double y) => _scene.PointerUp((float)x, (float)y);
    public void Wheel(int delta) => _scene.Wheel(delta);
    public new void KeyDown(string key) => _scene.KeyDown(key);
    public new void KeyUp(string key) => _scene.KeyUp(key);
    public void ResetScene() => _scene.Reset();

    protected override void RenderOverride(SKCanvas canvas, Size area)
        => _scene.Draw(canvas, (float)area.Width, (float)area.Height);
}
