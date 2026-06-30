using System;
using System.IO;
using SkiaSharp;

namespace ShaderGalaxy;

// Headless representative-frame renderer. Invoked via:  ShaderGalaxy.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Seed input + advance time so the frame is representative (not blank).
        // Warm up the nebula and settle the swirl center off-center for drama.
        scene.PointerMove(w * 0.62f, h * 0.40f);
        for (int i = 0; i < 240; i++)
        {
            scene.Update(1f / 60f);
        }

        // Fire a shockwave, then advance ~0.7s so the ring is mid-expansion.
        scene.PointerDown(w * 0.62f, h * 0.40f);
        for (int i = 0; i < 42; i++)
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
