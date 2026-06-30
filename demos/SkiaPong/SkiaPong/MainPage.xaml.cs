using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace SkiaPong;

public sealed partial class MainPage : Page
{
    public MainPage() => this.InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
        => Root.Focus(FocusState.Programmatic);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        Canvas.KeyDown(e.Key.ToString());
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        Canvas.KeyUp(e.Key.ToString());
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Root.Focus(FocusState.Programmatic); // clicking the surface grabs keyboard focus
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
