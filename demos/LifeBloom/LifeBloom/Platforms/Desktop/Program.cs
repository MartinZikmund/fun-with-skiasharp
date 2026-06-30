using Uno.UI.Hosting;

namespace LifeBloom;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Headless thumbnail mode: render one frame to PNG and exit (no window).
        if (args.Length >= 2 && args[0] == "--thumb")
        {
            Thumb.Render(args[1]);
            return;
        }

        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
