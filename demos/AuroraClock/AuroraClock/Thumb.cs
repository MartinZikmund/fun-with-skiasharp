using System;
using System.IO;
using SkiaSharp;

namespace AuroraClock;

// Headless representative-frame renderer. Invoked via:  AuroraClock.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Lock in the classic "watch advert" pose (~10:09) so the hands form a
        // pleasing symmetric V and the second hand sits high on the dial.
        var poseBase = new DateTime(2026, 6, 30, 10, 9, 35, 800);

        // A gentle pointer glow off to the side for some extra life.
        scene.PointerMove(w * 0.30f, h * 0.34f);

        // Advance ~1.3s of wall time so an aurora has drifted in and a fresh
        // sparkle burst (fired ~0.1s before the captured frame) is mid-flight
        // at the top of the dial.
        const float dt = 1f / 60f;
        for (int i = 0; i < 78; i++)
        {
            scene.ForceTime(poseBase.AddMilliseconds(i * dt * 1000f));
            scene.Update(dt);
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
