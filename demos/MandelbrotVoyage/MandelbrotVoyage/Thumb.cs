using System;
using System.IO;
using SkiaSharp;

namespace MandelbrotVoyage;

// Headless representative-frame renderer. Invoked via:  MandelbrotVoyage.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Let the auto-zoom dive toward the interesting point so the thumbnail captures a
        // deep, colorful frame brimming with boundary detail (the dead-black lake recedes
        // as we approach the boundary point). ~17s @ 60fps reaches a richly detailed zoom.
        for (int i = 0; i < 1020; i++) scene.Update(1f / 60f);

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
