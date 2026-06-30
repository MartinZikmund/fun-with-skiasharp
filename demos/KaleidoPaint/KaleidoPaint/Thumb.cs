using System;
using System.IO;
using SkiaSharp;

namespace KaleidoPaint;

// Headless representative-frame renderer. Invoked via:  KaleidoPaint.exe --thumb out.png
// Renders one lively frame of DemoScene to a PNG without any window/UI.
// This is also a smoke test: if Draw() throws, the thumbnail step fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new DemoScene();
        scene.SetSize(w, h);   // map pointer input to center-relative coords before first Draw
        scene.SetSymmetry(8);

        // Synthesize hand-painted petal gestures so the thumbnail shows a real,
        // dense mandala that fills the disc (not just the auto seed).
        float cx = w / 2f, cy = h / 2f;
        float maxR = MathF.Min(w, h) * 0.40f;

        // Several petals at different starting angles/lengths. Big time gaps between
        // them give each petal a distinct hue as the palette cycles.
        (float ang, float reach, float wob)[] petals =
        {
            (0.10f, 0.95f, 0.55f),
            (0.55f, 0.62f, 0.40f),
            (0.95f, 0.80f, 0.30f),
            (1.40f, 0.45f, 0.25f),
        };

        foreach ((float ang, float reach, float wob) in petals)
        {
            PaintPetal(scene, cx, cy, ang, maxR * reach, maxR * wob);
            for (int i = 0; i < 360; i++) scene.Update(1f / 60f); // hue shift between petals
        }

        // A small inner rosette of short strokes to enrich the core.
        for (int k = 0; k < 3; k++)
        {
            PaintPetal(scene, cx, cy, 0.3f + k * 0.7f, maxR * 0.32f, maxR * 0.10f);
            for (int i = 0; i < 200; i++) scene.Update(1f / 60f);
        }

        // Let the colours cycle and glow breathe into a lively frame.
        for (int i = 0; i < 90; i++) scene.Update(1f / 60f);

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

    // Simulate a dragged "petal": sweep out from the core to 'reach' and curve
    // back, with a sideways bow of 'bow' so it reads as an organic loop.
    private static void PaintPetal(DemoScene scene, float cx, float cy,
        float angle, float reach, float bow)
    {
        const int steps = 48;
        float dirX = MathF.Cos(angle), dirY = MathF.Sin(angle);
        float perpX = -dirY, perpY = dirX;

        const float inner = 34f; // start/end off-center so the core keeps a clean eye
        for (int i = 0; i <= steps; i++)
        {
            float f = i / (float)steps;
            // out-and-back along the radial direction (inner -> reach -> inner)
            float along = inner + MathF.Sin(f * MathF.PI) * (reach - inner);
            // sideways bow flips sign at the tip so the path loops like a leaf
            float side = MathF.Sin(f * MathF.PI * 2f) * bow;

            float x = cx + dirX * along + perpX * side;
            float y = cy + dirY * along + perpY * side;
            if (i == 0)
            {
                scene.PointerDown(x, y);
            }
            else
            {
                scene.PointerMove(x, y);
            }
            scene.Update(1f / 60f);
        }
        scene.PointerUp(cx, cy);
    }
}
