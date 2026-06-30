using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace LifeBloom;

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
    }

    private void OnPlayPause(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Canvas.TogglePlay();
        PlayButton.Content = Canvas.IsPlaying ? "Pause" : "Play";
    }

    private void OnStep(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Canvas.StepOnce();
        PlayButton.Content = "Play";
    }

    private void OnRandomize(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Canvas.Randomize();

    private void OnClear(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Canvas.ClearGrid();

    private void OnEraseChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => Canvas.SetEraseMode(EraseToggle.IsChecked == true);

    private void OnSpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
        => Canvas?.SetSpeed(e.NewValue);
}
