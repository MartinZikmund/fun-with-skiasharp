using System;
using System.IO;
using SkiaSharp;

namespace SkiaSnake;

// Headless representative-frame renderer:  SkiaSnake.exe --thumb out.png
// Renders one lively, mid-game frame of GameScene to a PNG (no window).
// Also a smoke test: if Draw()/Update() throws, this fails loudly.
internal static class Thumb
{
    public static void Render(string path)
    {
        const int w = 1100, h = 700;

        var scene = new GameScene();
        // Steer the snake toward food each tick using a wall- AND self-aware greedy
        // policy so the thumbnail shows a real mid-game frame: a grown snake (~8
        // segments) near food + score, while still Playing (never dies => no flash).
        int cols = GameScene.GridCols, rows = GameScene.GridRows;
        const int targetLen = 8;
        int safety = 0;
        while (scene.SnakeLength < targetLen && !scene.IsDead && safety++ < 6000)
        {
            var head = scene.HeadCell;
            var food = scene.FoodCell;
            var dir = scene.DirCell;

            int dx = food.x - head.x;
            int dy = food.y - head.y;

            // Rank candidate moves by gap closed to food; pick the first SAFE one.
            var moves = new (string key, int ddx, int ddy, int gain)[]
            {
                ("Right", 1, 0, dx),
                ("Left", -1, 0, -dx),
                ("Down", 0, 1, dy),
                ("Up", 0, -1, -dy),
            };
            Array.Sort(moves, (a, b) => b.gain.CompareTo(a.gain));

            string? chosen = PickSafe(moves, head, dir, scene, cols, rows);
            if (chosen is null)
            {
                break; // no safe move; stop with what we have rather than die
            }

            scene.KeyDown(chosen);
            scene.KeyUp(chosen);
            scene.Update(0.2f); // force one grid step per iteration
        }

        // Render immediately: right after the last eat the head still carries a full
        // eat-pulse and a fresh particle burst, so this single static frame is juicy.
        // (Advancing further could march the head into a wall and trigger game-over.)
        WriteImage(scene, path, w, h);
    }

    // Choose the first move (highest food-gain first) that is in bounds, not an
    // instant reverse, and does not run into the snake's own body (the tail cell is
    // safe because it vacates as the snake advances, unless we're about to eat).
    private static string? PickSafe(
        (string key, int ddx, int ddy, int gain)[] moves,
        (int x, int y) head,
        (int x, int y) dir,
        GameScene scene,
        int cols,
        int rows)
    {
        var body = scene.Body;
        var food = scene.FoodCell;

        foreach (var m in moves)
        {
            if (m.ddx == -dir.x && m.ddy == -dir.y)
            {
                continue; // instant reverse
            }

            int nx = head.x + m.ddx;
            int ny = head.y + m.ddy;
            if (nx < 0 || nx >= cols || ny < 0 || ny >= rows)
            {
                continue; // wall
            }

            bool willEat = nx == food.x && ny == food.y;
            bool hit = false;
            // If eating, the tail stays; otherwise the tail (index 0) vacates.
            int start = willEat ? 0 : 1;
            for (int i = start; i < body.Count; i++)
            {
                if (body[i].x == nx && body[i].y == ny)
                {
                    hit = true;
                    break;
                }
            }

            if (!hit)
            {
                return m.key;
            }
        }

        return null;
    }

    private static void WriteImage(GameScene scene, string path, int w, int h)
    {
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
