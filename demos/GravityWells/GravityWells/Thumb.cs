using System;
using System.IO;
using SkiaSharp;

namespace GravityWells;

// Headless representative-frame renderer. Invoked via:  GravityWells.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();

        // Draw once so the scene adopts the thumbnail dimensions (sun is centered
        // on the canvas size), then seed a lively, populated solar system.
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        scene.Draw(canvas, w, h);

        // Seed a bunch of orbiters so trails develop and the frame is busy.
        scene.AddRandom(14);
        // Let it run long enough for orbit trails to form and some merges to flash.
        for (int i = 0; i < 240; i++)
        {
            scene.Update(1f / 60f);
        }

        scene.Draw(canvas, w, h);

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 95);
        using var fs = File.Create(full);
        data.SaveTo(fs);
        Console.WriteLine("thumb-written:" + full);
    }
}
