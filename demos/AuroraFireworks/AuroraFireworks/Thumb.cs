using System;
using System.IO;
using SkiaSharp;

namespace AuroraFireworks;

// Headless representative-frame renderer. Invoked via:  AuroraFireworks.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();

        // Warm up so auto-launched shells are rising/bursting for a lively frame.
        for (int i = 0; i < 200; i++)
        {
            scene.Update(1f / 60f);
        }

        // Hero centerpiece + supporting bursts at staggered times, so the frame
        // captures bright fresh blooms alongside fading older ones.
        scene.BurstAt(w * 0.50f, h * 0.34f, huge: true);
        for (int i = 0; i < 14; i++)
        {
            scene.Update(1f / 60f);
        }
        scene.BurstAt(w * 0.74f, h * 0.26f);
        scene.BurstAt(w * 0.24f, h * 0.42f);
        for (int i = 0; i < 10; i++)
        {
            scene.Update(1f / 60f);
        }

        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            scene.Draw(canvas, w, h);
        }

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 95);
        using var fs = File.Create(full);
        data.SaveTo(fs);
        Console.WriteLine("thumb-written:" + full);
    }
}
