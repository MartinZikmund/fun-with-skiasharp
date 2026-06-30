using System;
using System.Collections.Generic;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.SkiaSnake;

// Neon Snake. Pure SkiaSharp + System only (no Uno types) so the same code renders
// headless thumbnails (Thumb.cs) and runs on the UI canvas (DemoCanvas.cs).
//
// Public seam (kept stable for DemoCanvas.cs + Thumb.cs):
//   ctor(); Update(dt); Draw(canvas, w, h);
//   PointerDown/Move/Up(x,y); Wheel(delta); KeyDown(key); KeyUp(key); Reset();
internal sealed class GameScene : IDemoScene
{
    // --- grid ---
    private const int Cols = 24;
    private const int Rows = 16;

    // --- state ---
    private enum Phase { Playing, Dead }

    private readonly HashSet<string> _held = new();
    private readonly List<(int x, int y)> _snake = new();
    private readonly Random _rng = new();
    private readonly List<Particle> _particles = new();

    private (int x, int y) _dir = (1, 0);
    private (int x, int y) _pendingDir = (1, 0);
    private (int x, int y) _food;

    private Phase _phase = Phase.Playing;
    private int _score;
    private int _best;
    private float _time;            // global clock for pulses/glow
    private float _tick;            // fixed-step accumulator
    private float _stepInterval = 0.14f;
    private float _flash;           // death flash 1->0
    private float _eatPulse;        // grows on eat, decays
    private bool _started;          // becomes true once a turn key is pressed

    public GameScene() => Reset();

    // Exposed for the headless thumbnail driver (Thumb.cs) so it can steer toward food.
    internal (int x, int y) HeadCell => _snake.Count > 0 ? _snake[^1] : (0, 0);
    internal (int x, int y) FoodCell => _food;
    internal (int x, int y) DirCell => _dir;
    internal int SnakeLength => _snake.Count;
    internal bool IsDead => _phase == Phase.Dead;
    internal IReadOnlyList<(int x, int y)> Body => _snake;
    internal static int GridCols => Cols;
    internal static int GridRows => Rows;

    public void Reset()
    {
        _snake.Clear();
        int cx = Cols / 2, cy = Rows / 2;
        // Start with a small body so it reads as a snake immediately.
        for (int i = 3; i >= 0; i--)
        {
            _snake.Add((cx - i, cy));
        }
        _dir = (1, 0);
        _pendingDir = (1, 0);
        _phase = Phase.Playing;
        _score = 0;
        _tick = 0f;
        _stepInterval = 0.14f;
        _flash = 0f;
        _eatPulse = 0f;
        _started = false;
        _particles.Clear();
        _held.Clear();
        SpawnFood();
    }

    // ---------------------------------------------------------------- input
    public void PointerDown(float x, float y)
    {
        if (_phase == Phase.Dead)
        {
            Reset();
        }
    }

    public void PointerMove(float x, float y) { }
    public void PointerUp(float x, float y) { }
    public void Wheel(int delta) { }

    public void KeyDown(string key)
    {
        _held.Add(key);

        switch (key)
        {
            case "Left":
            case "A":
                TryTurn(-1, 0);
                break;
            case "Right":
            case "D":
                TryTurn(1, 0);
                break;
            case "Up":
            case "W":
                TryTurn(0, -1);
                break;
            case "Down":
            case "S":
                TryTurn(0, 1);
                break;
            case "Space":
            case "Enter":
            case "R":
                if (_phase == Phase.Dead)
                {
                    Reset();
                }
                break;
        }
    }

    public void KeyUp(string key) => _held.Remove(key);

    private void TryTurn(int dx, int dy)
    {
        _started = true;
        // No instant reverse: ignore the opposite of the *current committed* direction.
        if (dx == -_dir.x && dy == -_dir.y)
        {
            return;
        }

        _pendingDir = (dx, dy);
    }

    // ---------------------------------------------------------------- update
    public void Update(float dt)
    {
        if (dt > 0.1f)
        {
            dt = 0.1f; // clamp huge frames so physics stays sane
        }

        _time += dt;
        if (_flash > 0f)
        {
            _flash = Math.Max(0f, _flash - dt * 2.2f);
        }

        if (_eatPulse > 0f)
        {
            _eatPulse = Math.Max(0f, _eatPulse - dt * 3.5f);
        }

        UpdateParticles(dt);

        if (_phase != Phase.Playing || !_started)
        {
            return;
        }

        _tick += dt;
        while (_tick >= _stepInterval)
        {
            _tick -= _stepInterval;
            Step();
            if (_phase != Phase.Playing)
            {
                break;
            }
        }
    }

    private void Step()
    {
        _dir = _pendingDir;
        var head = _snake[^1];
        var next = (x: head.x + _dir.x, y: head.y + _dir.y);

        // Wall collision -> game over.
        if (next.x < 0 || next.x >= Cols || next.y < 0 || next.y >= Rows)
        {
            Die();
            return;
        }

        bool ate = next.x == _food.x && next.y == _food.y;

        // Self collision (tail moves away unless we're eating).
        int selfCheckCount = ate ? _snake.Count : _snake.Count - 1;
        for (int i = 0; i < selfCheckCount; i++)
        {
            if (_snake[i].x == next.x && _snake[i].y == next.y)
            {
                Die();
                return;
            }
        }

        _snake.Add(next);
        if (ate)
        {
            _score += 10;
            _best = Math.Max(_best, _score);
            _eatPulse = 1f;
            _stepInterval = Math.Max(0.06f, _stepInterval - 0.005f);
            BurstAtCell(_food, new SKColor(0xFF, 0x4D, 0x8D));
            SpawnFood();
        }
        else
        {
            _snake.RemoveAt(0); // move forward
        }
    }

    private void Die()
    {
        _phase = Phase.Dead;
        _best = Math.Max(_best, _score);
        _flash = 1f;
        BurstAtCell(_snake[^1], new SKColor(0x66, 0xF0, 0xFF), 40);
    }

    private void SpawnFood()
    {
        // Find a free cell.
        for (int attempt = 0; attempt < 500; attempt++)
        {
            var c = (_rng.Next(Cols), _rng.Next(Rows));
            bool occupied = false;
            foreach (var s in _snake)
            {
                if (s.Item1 == c.Item1 && s.Item2 == c.Item2)
                {
                    occupied = true;
                    break;
                }
            }

            if (!occupied)
            {
                _food = c;
                return;
            }
        }

        _food = (0, 0);
    }

    // ---------------------------------------------------------------- particles
    private struct Particle
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Size;
        public SKColor Color;
    }

    private void BurstAtCell((int x, int y) cell, SKColor color, int count = 22)
    {
        // Stored in cell-space; converted to pixels at draw time so resizes are fine.
        for (int i = 0; i < count; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            float spd = 1.5f + (float)_rng.NextDouble() * 5f;
            float life = 0.4f + (float)_rng.NextDouble() * 0.5f;
            _particles.Add(new Particle
            {
                X = cell.x + 0.5f,
                Y = cell.y + 0.5f,
                Vx = (float)Math.Cos(ang) * spd,
                Vy = (float)Math.Sin(ang) * spd,
                Life = life,
                MaxLife = life,
                Size = 0.12f + (float)_rng.NextDouble() * 0.18f,
                Color = color,
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }

            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vx *= 0.92f;
            p.Vy *= 0.92f;
            _particles[i] = p;
        }
    }

    // ---------------------------------------------------------------- draw
    public void Draw(SKCanvas canvas, float width, float height)
    {
        canvas.Clear(new SKColor(0x07, 0x0A, 0x14));

        // Compute a centered, square-ish board with padding.
        float padTop = 64f;
        float pad = 24f;
        float availW = width - pad * 2f;
        float availH = height - padTop - pad;
        if (availW < 10f || availH < 10f)
        {
            return;
        }

        float cell = Math.Min(availW / Cols, availH / Rows);
        float boardW = cell * Cols;
        float boardH = cell * Rows;
        float ox = (width - boardW) / 2f;
        float oy = padTop + (availH - boardH) / 2f;

        DrawBackground(canvas, width, height);
        DrawBoard(canvas, ox, oy, boardW, boardH, cell);
        DrawFood(canvas, ox, oy, cell);
        DrawSnake(canvas, ox, oy, cell);
        DrawParticles(canvas, ox, oy, cell);
        DrawHud(canvas, width, height, ox, oy, boardW, boardH);

        if (_flash > 0f)
        {
            using var flash = new SKPaint { Color = new SKColor(0xFF, 0x3B, 0x6B, (byte)(120 * _flash)) };
            canvas.DrawRect(0, 0, width, height, flash);
        }
    }

    private void DrawBackground(SKCanvas canvas, float width, float height)
    {
        // Subtle radial vignette glow.
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(width / 2f, height / 2f),
            Math.Max(width, height) * 0.7f,
            new[] { new SKColor(0x12, 0x1A, 0x33), new SKColor(0x05, 0x07, 0x0F) },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);
    }

    private void DrawBoard(SKCanvas canvas, float ox, float oy, float boardW, float boardH, float cell)
    {
        // Board panel.
        using (var bg = new SKPaint { Color = new SKColor(0x0A, 0x10, 0x20), IsAntialias = true })
        {
            var rect = new SKRoundRect(new SKRect(ox - 6, oy - 6, ox + boardW + 6, oy + boardH + 6), 12);
            canvas.DrawRoundRect(rect, bg);
        }

        // Grid lines.
        using var grid = new SKPaint
        {
            Color = new SKColor(0x1B, 0x28, 0x44),
            IsAntialias = false,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
        };
        for (int c = 0; c <= Cols; c++)
        {
            float x = ox + c * cell;
            canvas.DrawLine(x, oy, x, oy + boardH, grid);
        }

        for (int r = 0; r <= Rows; r++)
        {
            float y = oy + r * cell;
            canvas.DrawLine(ox, y, ox + boardW, y, grid);
        }

        // Neon border.
        using var border = new SKPaint
        {
            Color = new SKColor(0x2E, 0xE6, 0xC8),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f),
        };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(ox - 6, oy - 6, ox + boardW + 6, oy + boardH + 6), 12), border);
        border.MaskFilter = null;
        border.StrokeWidth = 1.5f;
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(ox - 6, oy - 6, ox + boardW + 6, oy + boardH + 6), 12), border);
    }

    private void DrawFood(SKCanvas canvas, float ox, float oy, float cell)
    {
        float pulse = 0.5f + 0.5f * (float)Math.Sin(_time * 5f);
        float cx = ox + (_food.x + 0.5f) * cell;
        float cy = oy + (_food.y + 0.5f) * cell;
        float baseR = cell * 0.32f;
        float r = baseR * (1f + 0.12f * pulse);

        var hot = new SKColor(0xFF, 0x4D, 0x8D);
        var warm = new SKColor(0xFF, 0xC2, 0x4B);

        // Glow halo.
        using (var glow = new SKPaint
        {
            Color = hot.WithAlpha((byte)(110 + 90 * pulse)),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, cell * 0.45f),
            BlendMode = SKBlendMode.Plus,
        })
        {
            canvas.DrawCircle(cx, cy, r * 1.8f, glow);
        }

        // Body with radial gradient.
        using (var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx - r * 0.3f, cy - r * 0.3f),
            r * 1.6f,
            new[] { warm, hot },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp))
        using (var body = new SKPaint { Shader = shader, IsAntialias = true })
        {
            canvas.DrawCircle(cx, cy, r, body);
        }

        // Highlight.
        using (var hi = new SKPaint { Color = SKColors.White.WithAlpha(180), IsAntialias = true })
        {
            canvas.DrawCircle(cx - r * 0.32f, cy - r * 0.32f, r * 0.22f, hi);
        }
    }

    private void DrawSnake(SKCanvas canvas, float ox, float oy, float cell)
    {
        int n = _snake.Count;
        if (n == 0)
        {
            return;
        }

        float inset = cell * 0.10f;
        float round = cell * 0.30f;

        // Trail glow underlay (one soft pass).
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, cell * 0.35f),
            BlendMode = SKBlendMode.Plus,
        })
        {
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 1f : i / (float)(n - 1);
                var col = SnakeColor(t).WithAlpha(70);
                glow.Color = col;
                float x = ox + _snake[i].x * cell;
                float y = oy + _snake[i].y * cell;
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + inset, y + inset, x + cell - inset, y + cell - inset), round), glow);
            }
        }

        // Bodies.
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 1f : i / (float)(n - 1);
            bool isHead = i == n - 1;
            float x = ox + _snake[i].x * cell;
            float y = oy + _snake[i].y * cell;
            float thisInset = inset;
            if (isHead && _eatPulse > 0f)
            {
                thisInset = inset * (1f - 0.6f * _eatPulse); // head swells on eat
            }

            var rect = new SKRect(x + thisInset, y + thisInset, x + cell - thisInset, y + cell - thisInset);
            var top = SnakeColor(Math.Min(1f, t + 0.18f));
            var bottom = SnakeColor(t);

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Right, rect.Bottom),
                new[] { top, bottom },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);
            using var body = new SKPaint { Shader = shader, IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(rect, round), body);

            // Subtle top sheen.
            using var sheen = new SKPaint { Color = SKColors.White.WithAlpha((byte)(40 + 40 * t)), IsAntialias = true };
            var sheenRect = new SKRect(rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.MidY);
            canvas.DrawRoundRect(new SKRoundRect(sheenRect, round * 0.7f), sheen);

            if (isHead)
            {
                DrawEyes(canvas, rect);
            }
        }
    }

    private void DrawEyes(SKCanvas canvas, SKRect head)
    {
        float w = head.Width;
        float eyeR = w * 0.12f;
        float pupR = eyeR * 0.55f;
        // Eye offsets based on direction.
        float fx = _dir.x, fy = _dir.y;
        // Perpendicular for the two eyes.
        float px = -fy, py = fx;
        float cx = head.MidX + fx * w * 0.18f;
        float cy = head.MidY + fy * w * 0.18f;
        float sep = w * 0.20f;

        for (int s = -1; s <= 1; s += 2)
        {
            float ex = cx + px * sep * s;
            float ey = cy + py * sep * s;
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawCircle(ex, ey, eyeR, white);
            using var pupil = new SKPaint { Color = new SKColor(0x07, 0x0A, 0x14), IsAntialias = true };
            canvas.DrawCircle(ex + fx * eyeR * 0.4f, ey + fy * eyeR * 0.4f, pupR, pupil);
        }
    }

    private static SKColor SnakeColor(float t)
    {
        // Tail (teal) -> head (electric green/cyan).
        var a = new SKColor(0x1E, 0x9E, 0xB0); // tail
        var b = new SKColor(0x6B, 0xFF, 0x8E); // head
        byte Lerp(byte x, byte y) => (byte)(x + (y - x) * t);
        return new SKColor(Lerp(a.Red, b.Red), Lerp(a.Green, b.Green), Lerp(a.Blue, b.Blue));
    }

    private void DrawParticles(SKCanvas canvas, float ox, float oy, float cell)
    {
        if (_particles.Count == 0)
        {
            return;
        }

        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        foreach (var p in _particles)
        {
            float a = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
            paint.Color = p.Color.WithAlpha((byte)(220 * a));
            float px = ox + p.X * cell;
            float py = oy + p.Y * cell;
            canvas.DrawCircle(px, py, p.Size * cell * (0.5f + a), paint);
        }
    }

    private void DrawHud(SKCanvas canvas, float width, float height, float ox, float oy, float boardW, float boardH)
    {
        // Title + score row.
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold) ?? SKTypeface.Default, 30);
        using var scoreFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold) ?? SKTypeface.Default, 26);
        using var hintFont = new SKFont(SKTypeface.Default, 16);

        using var neon = new SKPaint { Color = new SKColor(0x6B, 0xFF, 0x8E), IsAntialias = true };
        using var neonGlow = new SKPaint
        {
            Color = new SKColor(0x6B, 0xFF, 0x8E, 150),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f),
        };
        canvas.DrawText("SKIA  SNAKE", 28, 42, SKTextAlign.Left, titleFont, neonGlow);
        canvas.DrawText("SKIA  SNAKE", 28, 42, SKTextAlign.Left, titleFont, neon);

        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var pink = new SKPaint { Color = new SKColor(0xFF, 0x6F, 0xA8), IsAntialias = true };
        canvas.DrawText($"SCORE {_score}", width - 28, 40, SKTextAlign.Right, scoreFont, pink);
        canvas.DrawText($"BEST {_best}", width - 28, 64, SKTextAlign.Right, hintFont, white);

        // Bottom hint.
        using var hintCol = new SKPaint { Color = new SKColor(0x9A, 0xB0, 0xD0), IsAntialias = true };
        string hint = _started
            ? "Arrows / WASD to steer   -   eat the pink food   -   don't bite yourself"
            : "Press an arrow or WASD to start   -   eat the pink food to grow";
        canvas.DrawText(hint, width / 2f, height - 16, SKTextAlign.Center, hintFont, hintCol);

        if (_phase == Phase.Dead)
        {
            DrawGameOver(canvas, width, height, ox, oy, boardW, boardH);
        }
    }

    private void DrawGameOver(SKCanvas canvas, float width, float height, float ox, float oy, float boardW, float boardH)
    {
        using var dim = new SKPaint { Color = new SKColor(0x05, 0x07, 0x0F, 190) };
        canvas.DrawRect(ox - 6, oy - 6, boardW + 12, boardH + 12, dim);

        float pulse = 0.5f + 0.5f * (float)Math.Sin(_time * 4f);
        using var bigFont = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold) ?? SKTypeface.Default, 56);
        using var midFont = new SKFont(SKTypeface.Default, 24);

        float cx = ox + boardW / 2f;
        float cy = oy + boardH / 2f;

        using var glow = new SKPaint
        {
            Color = new SKColor(0xFF, 0x3B, 0x6B, (byte)(160 + 80 * pulse)),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10f),
        };
        using var red = new SKPaint { Color = new SKColor(0xFF, 0x5C, 0x86), IsAntialias = true };
        canvas.DrawText("GAME OVER", cx, cy - 6, SKTextAlign.Center, bigFont, glow);
        canvas.DrawText("GAME OVER", cx, cy - 6, SKTextAlign.Center, bigFont, red);

        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText($"Score {_score}   -   Best {_best}", cx, cy + 34, SKTextAlign.Center, midFont, white);

        using var cta = new SKPaint { Color = new SKColor(0x6B, 0xFF, 0x8E).WithAlpha((byte)(180 + 75 * pulse)), IsAntialias = true };
        canvas.DrawText("Press SPACE / ENTER to play again", cx, cy + 70, SKTextAlign.Center, midFont, cta);
    }
}
