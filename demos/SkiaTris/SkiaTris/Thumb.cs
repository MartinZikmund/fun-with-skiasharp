using System;
using System.IO;
using SkiaSharp;

namespace SkiaTris;

// Headless representative-frame renderer:  SkiaTris.exe --thumb out.png
// Renders one lively, mid-game frame of GameScene to a PNG (no window).
// Also a smoke test: if Draw()/Update() throws, this fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new GameScene();

        // Play a little: drop several pieces across the well to build a
        // partially-filled stack, then leave an active piece falling with a
        // ghost visible for a true mid-game look.
        var rng = new Random(11);

        void Tap(string k, int steps)
        {
            scene.KeyDown(k);
            for (int i = 0; i < steps; i++)
            {
                scene.Update(1f / 60f);
            }
            scene.KeyUp(k);
        }

        for (int piece = 0; piece < 12; piece++)
        {
            // Rotate a random amount for variety.
            int rotations = rng.Next(0, 3);
            for (int r = 0; r < rotations; r++)
            {
                Tap("Up", 5);
            }
            // Spread pieces across the full board width.
            string dir = rng.Next(2) == 0 ? "Left" : "Right";
            int taps = rng.Next(0, 6);
            for (int m = 0; m < taps; m++)
            {
                Tap(dir, 4);
            }
            // Hard-drop most pieces to build the stack.
            scene.KeyDown("Space");
            scene.KeyUp("Space");
            for (int i = 0; i < 24; i++)
            {
                scene.Update(1f / 60f); // let flash/clears commit
            }
        }

        // Leave a final active piece mid-air with a ghost showing.
        Tap("Left", 6);
        Tap("Up", 5);
        for (int i = 0; i < 26; i++)
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
