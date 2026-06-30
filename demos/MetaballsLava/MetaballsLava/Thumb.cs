using System;
using System.IO;
using SkiaSharp;
// MathF used below for the seeded pointer wiggle.

namespace MetaballsLava;

// Headless representative-frame renderer. Invoked via:  MetaballsLava.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Seed the lava with the natural blobs, then nudge a couple of extra blobs
        // toward the centre so the thumbnail catches them mid-merge.
        for (int i = 0; i < 90; i++) { scene.Update(1f / 60f); }
        scene.PointerDown(w * 0.46f, h * 0.50f);
        scene.PointerUp(w * 0.46f, h * 0.50f);
        scene.PointerDown(w * 0.56f, h * 0.46f);
        scene.PointerUp(w * 0.56f, h * 0.46f);
        // hold + drag the pointer near the cluster to heat it for a glowing frame
        scene.PointerDown(w * 0.51f, h * 0.49f);
        for (int i = 0; i < 70; i++)
        {
            scene.PointerMove(w * (0.49f + 0.02f * MathF.Sin(i * 0.2f)), h * 0.49f);
            scene.Update(1f / 60f);
        }
        scene.PointerUp(w * 0.51f, h * 0.49f);

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
