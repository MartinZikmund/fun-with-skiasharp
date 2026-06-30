using System;
using System.IO;
using SkiaSharp;

namespace SkiaFlap;

// Headless representative-frame renderer:  SkiaFlap.exe --thumb out.png
// Renders one lively, mid-game frame of GameScene to a PNG (no window).
// Also a smoke test: if Draw()/Update() throws, this fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new GameScene();
        // Establish the canvas size first so pipes/bird are laid out for this frame.
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            scene.Draw(canvas, w, h); // sizes the scene
        }

        // Autopilot a few seconds so the bird stays alive and clears some pipes,
        // landing on a clean mid-flight frame with a non-zero score.
        const float dt = 1f / 60f;
        scene.StartIfReady();
        for (int i = 0; i < 600 && scene.IsAlive; i++)
        {
            // Aim for the center of the upcoming gap: flap when below target and
            // not already rising too fast. Simple but keeps the bird threading pipes.
            float target = scene.NextGapCenter();
            if (scene.BirdY > target - 6f && scene.BirdVelocity > -120f)
            {
                scene.KeyDown("Space");
                scene.KeyUp("Space");
            }
            scene.Update(dt);

            // Stop on a nice frame: a few points scored and the bird comfortably
            // inside a gap (not clipping a pipe), so the thumb reads clearly.
            if (scene.Score >= 3 && Math.Abs(scene.BirdY - scene.NextGapCenter()) < 24f)
            {
                break;
            }
        }

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
