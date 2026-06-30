using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MandelbrotVoyage;

public sealed partial class MainPage : Page
{
    public MainPage() => this.InitializeComponent();

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
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
    {
        Canvas.Wheel(e.GetCurrentPoint(Canvas).Properties.MouseWheelDelta);
        UpdateAutoZoomButton();
    }

    private void OnToggleAutoZoom(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Canvas.ToggleAutoZoom();
        UpdateAutoZoomButton();
    }

    private void OnReset(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Canvas.ResetScene();
        UpdateAutoZoomButton();
    }

    private void UpdateAutoZoomButton()
        => AutoZoomButton.Content = Canvas.IsAutoZoom ? "Pause Auto-Zoom" : "Resume Auto-Zoom";
}
