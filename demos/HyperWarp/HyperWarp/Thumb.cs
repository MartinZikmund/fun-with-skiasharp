using System;
using System.IO;
using SkiaSharp;

namespace HyperWarp;

// Headless representative-frame renderer. Invoked via:  HyperWarp.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Seed input so the thumbnail shows full-warp streaks radiating from center:
        // pointer near center (slight steer) + held burn + wheel throttle, then run
        // enough frames for streaks to develop. Force a warp jump to peak by hitting
        // the auto-jump schedule, so the captured frame is at its most dramatic.
        scene.PointerMove(w * 0.53f, h * 0.50f);
        scene.PointerDown(w * 0.53f, h * 0.50f);
        scene.Wheel(600); // crank sustained throttle into warp territory
        for (int i = 0; i < 300; i++)
        {
            scene.PointerMove(w * 0.53f, h * 0.50f); // keep pointer "inside" each frame
            scene.Wheel(40);                          // sustain throttle vs. wheel decay
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
