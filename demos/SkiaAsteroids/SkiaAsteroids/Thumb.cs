using System;
using System.IO;
using SkiaSharp;

namespace SkiaAsteroids;

// Headless representative-frame renderer:  SkiaAsteroids.exe --thumb out.png
// Renders one lively, mid-game frame of GameScene to a PNG (no window).
// Also a smoke test: if Draw()/Update() throws, this fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new GameScene();
        // Seed input + advance so the frame is representative: ship thrusting among
        // several asteroids with bullets in flight (not an empty board).
        // Establish the playfield size first so wave spawn uses the thumbnail dimensions.
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            // Establish the playfield size so the wave spawns at thumbnail dimensions.
            scene.Draw(canvas, w, h);

            // Sweep-fire: slowly rotate while firing so bullets spray across the field
            // and clip asteroids (score + bursts), keeping the ship near center.
            scene.KeyDown("Space");
            for (int i = 0; i < 70; i++)
            {
                if (i is 18 or 40)
                {
                    scene.KeyDown("Right");
                }
                if (i is 30 or 52)
                {
                    scene.KeyUp("Right");
                }
                scene.Update(1f / 60f);
            }
            scene.KeyUp("Right");
            scene.KeyUp("Space");

            // Final short thrust + fire burst so the flame is lit and fresh bullets fly
            // in the captured frame.
            scene.KeyDown("Up");
            scene.KeyDown("Space");
            for (int i = 0; i < 9; i++)
            {
                scene.Update(1f / 60f);
            }
            scene.KeyUp("Space");
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
