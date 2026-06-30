using System;
using System.IO;
using SkiaSharp;

namespace NeonWaveform;

// Headless representative-frame renderer. Invoked via:  NeonWaveform.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Drag the pointer well away from center so the energy ramps up for a vivid,
        // high-tempo frame. Hold it down so the energy target stays pumped.
        scene.PointerDown(w * 0.82f, h * 0.80f);
        scene.PointerMove(w * 0.82f, h * 0.80f);

        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            // Advance enough frames for the spectrum/energy to settle and land mid-beat.
            // Draw() each frame so the energy target updates from the held pointer.
            for (int i = 0; i < 240; i++)
            {
                scene.Update(1f / 60f);
                scene.Draw(canvas, w, h);
            }
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
