using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SkiaGallery.Core;

namespace SkiaGallery;

public sealed partial class DemoHostPage : Page
{
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _active;

    public DemoHostPage()
    {
        this.InitializeComponent();
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); SetChromeVisible(false); };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        if (e.Parameter is string name && DemoCatalog.ByName(name) is { } entry)
        {
            TitleText.Text = $"{entry.RankLabel}   {entry.Name}";
            var scene = entry.Factory();
            Canvas.SetScene(scene);
            BuildControls(scene);
        }
        ShowChrome();
        GrabFocus();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _active = false;
        _hudTimer.Stop();
        Canvas.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => GrabFocus();

    // --- chrome (Back pill + controls bar) auto-hide ---------------------------

    private void ShowChrome()
    {
        SetChromeVisible(true);
        _hudTimer.Stop();
        _hudTimer.Start();
    }

    private void SetChromeVisible(bool visible)
    {
        Hud.Opacity = visible ? 1 : 0;
        Hud.IsHitTestVisible = visible;
        if (ControlsBar.Visibility == Visibility.Visible)
        {
            ControlsBar.Opacity = visible ? 1 : 0;
            ControlsBar.IsHitTestVisible = visible;
        }
    }

    private void BuildControls(IDemoScene scene)
    {
        ControlsPanel.Children.Clear();
        if (scene is not IDemoControls c)
        {
            ControlsBar.Visibility = Visibility.Collapsed;
            return;
        }

        bool any = false;

        foreach (var s in c.Sliders)
        {
            any = true;
            ControlsPanel.Children.Add(new TextBlock
            {
                Text = s.Label,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var slider = new Slider
            {
                Minimum = s.Min,
                Maximum = s.Max,
                Value = s.Value,
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (s.Step > 0)
            {
                slider.StepFrequency = s.Step;
            }
            var set = s.Set;
            slider.ValueChanged += (_, e) => set(e.NewValue);
            ControlsPanel.Children.Add(slider);
        }

        foreach (var t in c.Toggles)
        {
            any = true;
            var toggle = new ToggleButton { Content = t.Label, IsChecked = t.Initial };
            var set = t.Set;
            toggle.Checked += (_, _) => set(true);
            toggle.Unchecked += (_, _) => set(false);
            ControlsPanel.Children.Add(toggle);
        }

        foreach (var b in c.Buttons)
        {
            any = true;
            var button = new Button { Content = b.Label };
            var invoke = b.Invoke;
            button.Click += (_, _) => invoke();
            ControlsPanel.Children.Add(button);
        }

        ControlsBar.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- focus -----------------------------------------------------------------

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

    // Reclaim focus only if it drifted to nothing - never when it moved to a real
    // control (Back / a control-bar button), or we'd steal focus mid-click.
    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_active)
        {
            return;
        }

        object? focused = null;
        try
        {
            focused = XamlRoot is not null ? FocusManager.GetFocusedElement(XamlRoot) : FocusManager.GetFocusedElement();
        }
        catch
        {
            // ignore
        }

        if (focused is null)
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

    // --- input forwarding ------------------------------------------------------

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
        ShowChrome();
        GrabFocus();
        var p = e.GetCurrentPoint(Canvas).Position;
        Canvas.PointerDown(p.X, p.Y);
        InputLayer.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowChrome();
        var p = e.GetCurrentPoint(Canvas).Position;
        Canvas.PointerMove(p.X, p.Y);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Canvas).Position;
        Canvas.PointerUp(p.X, p.Y);
        InputLayer.ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
        => Canvas.Wheel(e.GetCurrentPoint(Canvas).Properties.MouseWheelDelta);
}
