using System;
using System.IO;
using SkiaSharp;

namespace LifeBloom;

// Headless representative-frame renderer. Invoked via:  LifeBloom.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Random soup is seeded in the ctor. Run a brisk simulation so the frame shows
        // a mix of fresh births, mature cells, and fading death trails (the "bloom").
        scene.SetSpeed(12f);
        for (int i = 0; i < 150; i++)
        {
            scene.Update(1f / 60f);
        }
        // Park a brush near a lively spot so the pointer ring shows on the thumbnail.
        scene.PointerMove(w * 0.5f, h * 0.5f);

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
