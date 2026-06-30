using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SkiaGallery.Core;

namespace SkiaGallery;

public sealed partial class DemoHostPage : Page
{
    public DemoHostPage() => this.InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string name && DemoCatalog.ByName(name) is { } entry)
        {
            TitleText.Text = $"{entry.RankLabel}   {entry.Name}";
            ControlsText.Text = entry.Controls;
            Canvas.SetScene(entry.Factory());
        }
        Root.Focus(FocusState.Programmatic);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Canvas.Stop(); // detach CompositionTarget.Rendering when leaving the demo
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Root.Focus(FocusState.Programmatic);

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
        Root.Focus(FocusState.Programmatic);
        var p = e.GetCurrentPoint(Canvas).Position;
        Canvas.PointerDown(p.X, p.Y);
        Root.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
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
