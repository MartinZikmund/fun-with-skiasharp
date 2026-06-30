using System;
using System.IO;
using SkiaSharp;

namespace SkiaPong;

// Headless representative-frame renderer:  SkiaPong.exe --thumb out.png
// Renders one lively, mid-game frame of GameScene to a PNG (no window).
// Also a smoke test: if Draw()/Update() throws, this fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new GameScene();

        // Establish the real size first so paddles/ball centre correctly.
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            scene.Draw(canvas, w, h);

            // Play a lively match: serve, chase the ball with the mouse (P1) but lag
            // imperfectly so a few points get scored, then capture a mid-rally frame.
            scene.PointerDown(w * 0.5f, h * 0.5f); // first serve
            for (int i = 0; i < 4000; i++)
            {
                scene.Update(1f / 60f);

                // Re-serve whenever the round is waiting so the match keeps going.
                if (!scene.IsBallInFlight)
                {
                    scene.KeyDown("Space");
                    scene.KeyUp("Space");
                }

                // P1 tracks the ball with a wandering lag so it misses sometimes.
                float lag = (float)Math.Sin(i * 0.07f) * 120f;
                scene.PointerMove(w * 0.5f, scene.BallY + lag);

                // Capture once a couple of points are on the board and the ball is
                // in flight near mid-court (so both paddles + ball are clearly visible).
                if (scene.TotalScore >= 3 && scene.IsBallInFlight &&
                    scene.BallX > w * 0.34f && scene.BallX < w * 0.66f)
                {
                    break;
                }
            }

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
