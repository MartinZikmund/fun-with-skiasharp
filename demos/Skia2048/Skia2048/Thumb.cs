using System;
using System.IO;
using SkiaSharp;

namespace Skia2048;

// Headless representative-frame renderer:  Skia2048.exe --thumb out.png
// Renders one lively, mid-game frame of GameScene to a PNG (no window).
// Also a smoke test: if Draw()/Update() throws, this fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new GameScene(seed: 12345);

        // Play many real moves so the board fills with several higher-value, colored
        // tiles (up to ~128/256) and a healthy score - a believable mid-game frame.
        // Cycling Down/Right keeps merges stacking in a corner like real play.
        string[] cycle = { "Down", "Right", "Down", "Left", "Up", "Right" };

        for (int m = 0; m < 60; m++)
        {
            string key = cycle[m % cycle.Length];
            scene.KeyDown(key);
            scene.KeyUp(key);
            // Let the slide + spawn settle between moves.
            for (int i = 0; i < 12; i++)
            {
                scene.Update(1f / 60f);
            }
        }

        // One last move and only a few frames so merge-pops / particles are
        // visibly in motion in the captured frame.
        scene.KeyDown("Down");
        scene.KeyUp("Down");
        for (int i = 0; i < 5; i++)
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
