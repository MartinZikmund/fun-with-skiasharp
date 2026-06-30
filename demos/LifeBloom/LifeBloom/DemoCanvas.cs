using System;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace LifeBloom;

// Bridges the pure-Skia DemoScene to Uno's hardware-accelerated Skia surface.
// SKCanvasElement draws on the SAME GPU surface as the rest of the app (no buffer copy).
public partial class DemoCanvas : SKCanvasElement
{
    private readonly DemoScene _scene = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public DemoCanvas()
    {
        _timer.Tick += (_, _) => { _scene.Update(1f / 60f); Invalidate(); };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    // Input forwarded from MainPage (pointer events live on a hit-testable Grid).
    public void PointerDown(double x, double y) => _scene.PointerDown((float)x, (float)y);
    public void PointerMove(double x, double y) => _scene.PointerMove((float)x, (float)y);
    public void PointerUp(double x, double y) => _scene.PointerUp((float)x, (float)y);
    public void Wheel(int delta) => _scene.Wheel(delta);
    public void ResetScene() => _scene.Reset();

    // Control surface used by the Fluent overlay buttons/slider in MainPage.
    public bool IsPlaying => _scene.IsPlaying;
    public void TogglePlay() => _scene.TogglePlay();
    public void StepOnce() => _scene.StepOnce();
    public void Randomize() => _scene.Randomize();
    public void ClearGrid() => _scene.Clear();
    public void SetSpeed(double stepsPerSecond) => _scene.SetSpeed((float)stepsPerSecond);
    public void SetEraseMode(bool erase) => _scene.SetEraseMode(erase);

    protected override void RenderOverride(SKCanvas canvas, Size area)
        => _scene.Draw(canvas, (float)area.Width, (float)area.Height);
}
