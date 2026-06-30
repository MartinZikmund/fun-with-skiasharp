using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SkiaGallery.Core;

namespace SkiaGallery;

public sealed partial class DemoHostPage : Page
{
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _active;

    public DemoHostPage()
    {
        this.InitializeComponent();
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); Hud.Opacity = 0; };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        if (e.Parameter is string name && DemoCatalog.ByName(name) is { } entry)
        {
            TitleText.Text = $"{entry.RankLabel}   {entry.Name}";
            Canvas.SetScene(entry.Factory());
        }
        ShowHud();
        GrabFocus();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _active = false;
        _hudTimer.Stop();
        Canvas.Stop(); // detach CompositionTarget.Rendering when leaving the demo
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => GrabFocus();

    // Reveal the overlay, then auto-hide after a pause so it never blocks the demo.
    private void ShowHud()
    {
        Hud.Opacity = 1;
        _hudTimer.Stop();
        _hudTimer.Start();
    }

    // Auto-focus the play surface so keyboard demos are immediately playable.
    // Deferred to after layout (WASM needs the element realized before focus sticks).
    private void GrabFocus()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_active)
            {
                Root.Focus(FocusState.Programmatic);
            }
        });
    }

    // Keep keyboard focus on the surface until we navigate away (UnoDoom-style).
    // The Back button is mouse/touch-driven, so re-grabbing keyboard focus doesn't block it.
    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_active)
        {
            GrabFocus();
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        Canvas.ForwardKeyDown(e.Key.ToString());
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        Canvas.ForwardKeyUp(e.Key.ToString());
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ShowHud();
        GrabFocus();
        var p = e.GetCurrentPoint(Canvas).Position;
        Canvas.PointerDown(p.X, p.Y);
        Root.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowHud();
        var p = e.GetCurrentPoint(Canvas).Position;
        Canvas.PointerMove(p.X, p.Y);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Canvas).Position;
        Canvas.PointerUp(p.X, p.Y);
        Root.ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
        => Canvas.Wheel(e.GetCurrentPoint(Canvas).Properties.MouseWheelDelta);
}
