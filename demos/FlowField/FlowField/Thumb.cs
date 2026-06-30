using System;
using System.IO;
using SkiaSharp;

namespace FlowField;

// Headless representative-frame renderer. Invoked via:  FlowField.exe --thumb out.png
// Renders many accumulated frames of DemoScene to a PNG so the silky trails build up.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        // Seed a swirling pointer so the field shows a beautiful vortex in the thumb.
        scene.PointerDown(w * 0.62f, h * 0.46f);

        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);

        // Trails accumulate on the persistent canvas: update + draw each frame.
        const int frames = 320;
        for (int i = 0; i < frames; i++)
        {
            // gently move the pointer along an arc so streams curve elegantly
            float a = i / (float)frames * MathF.PI * 1.2f;
            scene.PointerMove(
                w * 0.5f + MathF.Cos(a) * w * 0.16f,
                h * 0.5f + MathF.Sin(a) * h * 0.16f);

            scene.Update(1f / 60f);
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
