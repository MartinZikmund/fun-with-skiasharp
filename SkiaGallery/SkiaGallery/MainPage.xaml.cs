using Microsoft.UI.Xaml.Controls;
using SkiaGallery.Core;

namespace SkiaGallery;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
        Tiles.ItemsSource = DemoCatalog.All;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DemoEntry entry)
        {
            Frame.Navigate(typeof(DemoHostPage), entry.Name);
        }
    }
}
