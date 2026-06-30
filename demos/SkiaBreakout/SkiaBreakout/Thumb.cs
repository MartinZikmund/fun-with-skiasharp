using System;
using System.IO;
using SkiaSharp;

namespace SkiaBreakout;

// Headless representative-frame renderer:  SkiaBreakout.exe --thumb out.png
// Renders one lively, mid-game frame of GameScene to a PNG (no window).
// Also a smoke test: if Draw()/Update() throws, this fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new GameScene();

        // Establish the canvas size once (so the paddle/ball are placed correctly).
        using var bmp = new SKBitmap(w, h);
        using (var sizing = new SKCanvas(bmp))
        {
            scene.Draw(sizing, w, h);
        }

        // Launch the ball and play for a while so several bricks are gone, the ball is
        // mid-flight, and a fresh particle burst is on screen.
        scene.PointerMove(w * 0.5f, h * 0.6f);
        scene.PointerDown(w * 0.5f, h * 0.6f);

        // Keep the paddle under the ball so the rally stays alive while bricks break,
        // then stop on a frame where the ball is mid-flight (Playing) for a lively shot.
        for (int i = 0; i < 600; i++)
        {
            scene.PointerMove(scene.BallX, h * 0.6f); // follow the ball
            scene.Update(1f / 60f);

            // After enough bricks are cleared, freeze on a mid-flight frame.
            if (i > 320 && scene.BallInPlay)
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
